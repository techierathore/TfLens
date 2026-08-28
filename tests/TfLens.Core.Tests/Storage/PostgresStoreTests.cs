using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Storage;
using TfLens.Core.Tests.TestSupport;
using Xunit.Abstractions;

namespace TfLens.Core.Tests.Storage;

/// <summary>
/// Exercises the store against the real PostgreSQL 16 database — idempotent upsert (REQ-FN-035),
/// cross-user isolation (REQ-NFR-010's engine half) and rebuild count-identity (REQ-FN-029).
/// </summary>
/// <remarks>
/// These are integration tests by intent: <c>ON CONFLICT DO NOTHING</c> against expression indexes,
/// <c>jsonb</c> round-tripping and NULL-versus-zero are properties of the database, not of C#, and an
/// in-memory double would prove none of them. The connection string comes from
/// <c>TfLensDbConnection</c> when set, and otherwise from the documented local compose service.
/// </remarks>
public sealed class PostgresStoreTests : IAsyncLifetime
{
    private const string DefaultConnection =
        "Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=tflensdev";

    /// <summary>Valid session records in the TrSetup fixture — one further line is deliberately malformed.</summary>
    private const int FixtureSessionRecords = 7;

    /// <summary>Distinct session ids among those records; the rest are the snapshots BRD-27 expects.</summary>
    private const int FixtureDistinctSessions = 4;

    private readonly StreamParser objParser = new();
    private readonly ITestOutputHelper objOutput;
    private readonly string objDataRoot = Path.Combine(
        Path.GetTempPath(), "tflens-store-tests", Guid.NewGuid().ToString("N"));

    private PostgresStore objStore = null!;

    /// <summary>
    /// Every <c>(user, repo)</c> pair this class has written to, so teardown can purge exactly those.
    /// </summary>
    /// <remarks>
    /// Populated by <see cref="ResetAsync"/> rather than by a hard-coded list, so a fixture repo added
    /// by a future test is cleaned up without anyone having to remember this set exists.
    /// </remarks>
    private readonly HashSet<(int UserId, string Repo)> objTouched = [];

    /// <summary>
    /// Creates the test class.
    /// </summary>
    /// <param name="aOutput">xUnit output sink, used by the end-to-end smoke test to print row counts.</param>
    public PostgresStoreTests(ITestOutputHelper aOutput) => objOutput = aOutput;

    /// <summary>Applies the schema before the first test in the class runs.</summary>
    /// <returns>A task that completes when the store is ready.</returns>
    public async Task InitializeAsync()
    {
        objStore = NewStore(objDataRoot);
        await objStore.EnsureSchemaAsync();
    }

    /// <summary>
    /// Removes everything this class wrote — the database rows as well as the temporary raw archive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These tests run against the real shared database, so they must leave it as they found it.</b>
    /// Each test calls <see cref="ResetAsync"/>, which clears the repo <i>before</i> it runs and then
    /// writes a <c>"UserRepo"</c> row for it — so before this teardown existed the rows simply survived
    /// the run. That had two visible consequences on the owner's live app, both real: the demo account
    /// showed <b>8 connected repositories instead of 3</b> (five phantom <c>tflenstest/Store*</c> entries
    /// whose sync always failed, because they are not real GitHub repositories), and the OpenCode
    /// measured-dollars figure on <c>/harness</c> was inflated by the <c>cost_usd</c> these fixtures
    /// carry — the "only measured dollars in the system" reading roughly <c>$1.02</c> instead of the true
    /// <c>$0.04</c>. Cleaning up before a test is not enough; a test suite has to clean up after itself.
    /// </para>
    /// <para>
    /// <see cref="ITelemetryStore.DeleteRepoDataAsync"/> is the whole job: it removes the stream rows,
    /// the <c>"SyncState"</c> row and the <c>"UserRepo"</c> row for exactly one <c>(user, repo)</c> pair,
    /// so another user's copy of the same repository is untouched (ADR-013). Every pair passed through
    /// <see cref="ResetAsync"/> is tracked in <see cref="objTouched"/>, which means a test added later
    /// that introduces a new fixture repo is cleaned up automatically without anyone remembering to
    /// extend a hard-coded list.
    /// </para>
    /// <para>
    /// Teardown never fails the run: a cleanup error is reported through the test output and swallowed,
    /// because a green suite that could not tidy up is a housekeeping problem, and turning it into a
    /// spurious test failure would hide whatever the tests actually proved.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes when the rows and the directory are gone.</returns>
    public async Task DisposeAsync()
    {
        foreach (var (vUserId, vRepo) in objTouched)
        {
            try
            {
                await objStore.DeleteRepoDataAsync(vUserId, vRepo);
            }
            catch (Exception vEx)
            {
                objOutput.WriteLine($"cleanup: could not purge user {vUserId} repo {vRepo} — {vEx.Message}");
            }
        }

        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, recursive: true);
        }
    }

    /// <summary>The schema script is found and applied, and the database answers (REQ-FN-045).</summary>
    [Fact]
    public async Task SchemaAppliesAndDatabaseAnswers()
    {
        await objStore.EnsureSchemaAsync();

        (await objStore.PingAsync()).Should().BeTrue();
    }

    /// <summary>Parsing and upserting the same files twice writes nothing the second time (REQ-FN-035).</summary>
    [Fact]
    public async Task UpsertIsIdempotentAcrossRepeatedParses()
    {
        const string vRepo = "tflenstest/StoreIdempotence";
        await ResetAsync(Fixtures.StoreTestUserId, vRepo);

        var vFirst = await StoreEveryStreamAsync(Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo);
        var vCountsAfterFirst = await CountAsync(Fixtures.StoreTestUserId, vRepo);

        var vSecond = await StoreEveryStreamAsync(Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo);
        var vCountsAfterSecond = await CountAsync(Fixtures.StoreTestUserId, vRepo);

        vFirst.Should().BeGreaterThan(0);
        vSecond.Should().Be(0, "every row already exists under its unique index");
        vCountsAfterSecond.Should().Be(vCountsAfterFirst);
    }

    /// <summary>Two users may hold the same repo name and neither sees the other's rows (ADR-013).</summary>
    [Fact]
    public async Task CrossUserIsolationKeepsRowsApart()
    {
        const string vRepo = "tflenstest/StoreIsolation";
        await ResetAsync(Fixtures.StoreTestUserId, vRepo);
        await ResetAsync(Fixtures.StoreTestSecondUserId, vRepo);

        await StoreEveryStreamAsync(Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo);
        await StoreEveryStreamAsync(Fixtures.StoreTestSecondUserId, vRepo, Fixtures.TrBlazeUiRepo);

        var vDemo = await CountAsync(Fixtures.StoreTestUserId, vRepo);
        var vSecond = await CountAsync(Fixtures.StoreTestSecondUserId, vRepo);

        vDemo.Runs.Should().Be(6);
        vSecond.Runs.Should().Be(2, "the second user stored the smaller fixture, and only that one");
        vDemo.Commits.Should().Be(5);
        vSecond.Commits.Should().Be(2);

        var vDemoSessions = await objStore.ReadSessionsAsync(
            Fixtures.StoreTestUserId, FrameworkNames.TechieFlow, vRepo);
        vDemoSessions.Should().OnlyContain(aS => aS.UserId == Fixtures.StoreTestUserId);
    }

    /// <summary>Reads are scoped to one framework, so a figure cannot pool across them (ADR-016).</summary>
    [Fact]
    public async Task ReadsAreScopedToOneFramework()
    {
        const string vRepo = "tflenstest/StoreFramework";
        await ResetAsync(Fixtures.StoreTestUserId, vRepo);

        await StoreEveryStreamAsync(Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        var vTechieFlow = await objStore.ReadRunsAsync(Fixtures.StoreTestUserId, FrameworkNames.TechieFlow, vRepo);
        var vPlaybook = await objStore.ReadRunsAsync(Fixtures.StoreTestUserId, FrameworkNames.Playbook, vRepo);

        vTechieFlow.Should().NotBeEmpty();
        vPlaybook.Should().BeEmpty("the repository is registered as techieflow");
    }

    /// <summary>An absent optional round-trips as NULL and a present zero round-trips as zero (REQ-FN-036).</summary>
    [Fact]
    public async Task NullAndZeroSurviveTheRoundTrip()
    {
        const string vRepo = "tflenstest/StoreNullVsZero";
        await ResetAsync(Fixtures.StoreTestUserId, vRepo);

        await StoreEveryStreamAsync(Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        var vRuns = await objStore.ReadRunsAsync(Fixtures.StoreTestUserId, FrameworkNames.TechieFlow, vRepo);
        var vTriage = vRuns.Single(aR => aR.Cmd == "triage-issues");

        vTriage.FilesWritten.Should().Be(0);
        vTriage.TokensIn.Should().BeNull();
        vTriage.CostUsd.Should().BeNull();
        vTriage.Routed.Should().BeNull();
        vTriage.Harness.Should().BeNull();

        var vDrifted = vRuns.Single(aR => aR.Routed == false);
        vDrifted.Overflow.Should().NotBeNull().And.Subject.ToString()!.Should().Contain("routed_reason");
    }

    /// <summary>A rebuild from the raw archive reproduces the live-sync row counts exactly (REQ-FN-029).</summary>
    [Fact]
    public async Task RebuildReproducesLiveSyncCounts()
    {
        var vDataRoot = Path.Combine(objDataRoot, "rebuild");
        var vStore = NewStore(vDataRoot);
        const string vRepo = "tflenstest/StoreRebuild";

        await ResetAsync(Fixtures.StoreTestUserId, vRepo, vStore);
        await WriteRawArchiveAsync(vDataRoot, Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        // The live sync path: parse each archived file and upsert it.
        await StoreEveryStreamAsync(Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo, vStore);
        var vLive = await CountAsync(Fixtures.StoreTestUserId, vRepo, vStore);

        var vReport = await vStore.RebuildAsync(Fixtures.StoreTestUserId);
        var vRebuilt = await CountAsync(Fixtures.StoreTestUserId, vRepo, vStore);

        vReport.FilesReplayed.Should().Be(5, "the fifth stream, misses, joined the set on 2026-08-28");
        vRebuilt.Should().Be(vLive, "a rebuild reads only data/raw and must land on the same numbers");
        vReport.InvalidLines.Should().Be(
            6, "one malformed line per stream file, plus the misses fixture's unknown `kind` line");
        vReport.DuplicatesCollapsed.Should().Be(8, "the misses fixture opens one miss twice");
    }

    /// <summary>
    /// A rebuild persists how many session records ingest collapsed, and repeating it does not double
    /// the figure (REQ-FN-063).
    /// </summary>
    /// <remarks>
    /// This is the number the export reports as <c>pooled.session_duplicates_collapsed</c>. It cannot be
    /// recovered by reading the store — the duplicates were never written — so a rebuild has to leave it
    /// behind in <c>"SyncState"</c> or the figure is lost. A rebuild replays the whole archive, so it is
    /// authoritative and <b>sets</b> the count; running it twice must therefore land on the same number,
    /// which is the half of the contract an accumulate-on-every-pass implementation would fail.
    /// </remarks>
    [Fact]
    public async Task RebuildRecordsSessionCollapsesAndRepeatingItDoesNotDoubleThem()
    {
        var vDataRoot = Path.Combine(objDataRoot, "collapses");
        var vStore = NewStore(vDataRoot);
        const string vRepo = "tflenstest/StoreSessionCollapses";

        await ResetAsync(Fixtures.StoreTestUserId, vRepo, vStore);
        await SeedSyncStateAsync(Fixtures.StoreTestUserId, vRepo, vStore);
        await WriteRawArchiveAsync(vDataRoot, Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        await vStore.RebuildAsync(Fixtures.StoreTestUserId);
        var vFirst = await SessionCollapsesAsync(Fixtures.StoreTestUserId, vRepo, vStore);
        var vStored = await CountAsync(Fixtures.StoreTestUserId, vRepo, vStore);

        // The fixture's sessions.jsonl holds 7 valid records over 4 distinct session ids.
        vStored.Sessions.Should().Be(FixtureDistinctSessions);
        vFirst.Should().Be(
            FixtureSessionRecords - FixtureDistinctSessions,
            "the collapse count is what ingest threw away: records presented minus rows kept");

        await vStore.RebuildAsync(Fixtures.StoreTestUserId);
        var vSecond = await SessionCollapsesAsync(Fixtures.StoreTestUserId, vRepo, vStore);

        vSecond.Should().Be(vFirst, "a rebuild is authoritative and sets the count; it never accumulates");
    }

    /// <summary>
    /// The collapse count includes duplicates spread across two archived snapshots (REQ-FN-063).
    /// </summary>
    /// <remarks>
    /// This is the case that forces the measurement to be <i>records presented minus rows stored</i>
    /// rather than a sum of what each parse collapsed. A session id that appears once in each of two
    /// snapshots is not a duplicate inside either file — no parse can see it — and is collapsed only by
    /// the store's <c>UcSessionUserRepoId</c> index. It is exactly the shape of the one duplicate the
    /// TechieFlow dataset carries across its two archived <c>sessions.jsonl</c> fetches.
    /// </remarks>
    [Fact]
    public async Task RebuildCountsSessionDuplicatesSpreadAcrossArchivedSnapshots()
    {
        var vDataRoot = Path.Combine(objDataRoot, "snapshots");
        var vStore = NewStore(vDataRoot);
        const string vRepo = "tflenstest/StoreSessionSnapshots";
        const string vSecondSha = "a2b3c4d";

        await ResetAsync(Fixtures.StoreTestUserId, vRepo, vStore);
        await SeedSyncStateAsync(Fixtures.StoreTestUserId, vRepo, vStore);
        await WriteRawArchiveAsync(vDataRoot, Fixtures.StoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        // A second fetch of the same sessions stream, as the poller would archive it under a new SHA.
        var vDirectory = Path.Combine(
            vDataRoot, "raw", Fixtures.StoreTestUserId.ToString(), vRepo.Replace("/", "__", StringComparison.Ordinal));
        await File.WriteAllTextAsync(
            Path.Combine(vDirectory, $"{StreamNames.Sessions}-{vSecondSha}.jsonl"),
            Fixtures.Read(Fixtures.TrSetupRepo, StreamKind.Sessions));

        await vStore.RebuildAsync(Fixtures.StoreTestUserId);

        var vStored = await CountAsync(Fixtures.StoreTestUserId, vRepo, vStore);
        var vCollapsed = await SessionCollapsesAsync(Fixtures.StoreTestUserId, vRepo, vStore);

        vStored.Sessions.Should().Be(
            FixtureDistinctSessions, "the second snapshot carries no session id the first did not");
        vCollapsed.Should().Be(
            (2 * FixtureSessionRecords) - FixtureDistinctSessions,
            "every record the second snapshot presented was collapsed by the unique index, and no parse saw it");
    }

    /// <summary>Sync bookkeeping round-trips per user and repository (REQ-FN-025).</summary>
    [Fact]
    public async Task SyncStateRoundTrips()
    {
        const string vRepo = "tflenstest/StoreSyncState";
        await ResetAsync(Fixtures.StoreTestUserId, vRepo);

        await objStore.WriteSyncStateAsync(new SyncState
        {
            UserId = Fixtures.StoreTestUserId,
            Repo = vRepo,
            Kind = FrameworkNames.TechieFlow,
            Branch = "main",
            LastSha = Fixtures.SourceSha,
            LastSyncTs = "2026-08-26T12:00:00Z",
            LastError = null,
            RunsCount = 6,
            GatesCount = 14,
            SessionsCount = 4,
            CommitsCount = 5,
            EventsCount = 0
        });

        var vRows = await objStore.ReadSyncStateAsync(Fixtures.StoreTestUserId);
        var vRow = vRows.Single(aS => aS.Repo == vRepo);

        vRow.LastSha.Should().Be(Fixtures.SourceSha);
        vRow.LastError.Should().BeNull();
        vRow.GatesCount.Should().Be(14);

        await objStore.DeleteRepoDataAsync(Fixtures.StoreTestUserId, vRepo);
        (await objStore.ReadSyncStateAsync(Fixtures.StoreTestUserId)).Should().NotContain(aS => aS.Repo == vRepo);
    }

    /// <summary>
    /// The end-to-end smoke: two users store both fixture repositories, the counts are read back per
    /// user, the same files are stored again, and a rebuild replays the archive — printing every number.
    /// </summary>
    [Fact]
    public async Task SmokeStoresReadsBackRepeatsAndRebuilds()
    {
        var vDataRoot = Path.Combine(objDataRoot, "smoke");
        var vStore = NewStore(vDataRoot);
        const string vBusyRepo = "tflenstest/SmokeTrSetup";
        const string vStaleRepo = "tflenstest/SmokeTrBlazeUI";

        foreach (var vUserId in new[] { Fixtures.StoreTestUserId, Fixtures.StoreTestSecondUserId })
        {
            await ResetAsync(vUserId, vBusyRepo, vStore);
            await ResetAsync(vUserId, vStaleRepo, vStore);
            await WriteRawArchiveAsync(vDataRoot, vUserId, vBusyRepo, Fixtures.TrSetupRepo);
            await WriteRawArchiveAsync(vDataRoot, vUserId, vStaleRepo, Fixtures.TrBlazeUiRepo);
        }

        var vFirstPass = 0;
        foreach (var vUserId in new[] { Fixtures.StoreTestUserId, Fixtures.StoreTestSecondUserId })
        {
            vFirstPass += await StoreEveryStreamAsync(vUserId, vBusyRepo, Fixtures.TrSetupRepo, vStore);
            vFirstPass += await StoreEveryStreamAsync(vUserId, vStaleRepo, Fixtures.TrBlazeUiRepo, vStore);
        }

        objOutput.WriteLine($"PASS 1 rows written = {vFirstPass}");
        await PrintCountsAsync("after pass 1", vBusyRepo, vStaleRepo, vStore);

        var vSecondPass = 0;
        foreach (var vUserId in new[] { Fixtures.StoreTestUserId, Fixtures.StoreTestSecondUserId })
        {
            vSecondPass += await StoreEveryStreamAsync(vUserId, vBusyRepo, Fixtures.TrSetupRepo, vStore);
            vSecondPass += await StoreEveryStreamAsync(vUserId, vStaleRepo, Fixtures.TrBlazeUiRepo, vStore);
        }

        objOutput.WriteLine($"PASS 2 rows written = {vSecondPass} (idempotence: must be 0)");
        var vBeforeRebuild = await PrintCountsAsync("after pass 2", vBusyRepo, vStaleRepo, vStore);

        var vReport = await vStore.RebuildAsync(Fixtures.StoreTestUserId);
        objOutput.WriteLine(
            $"REBUILD user {Fixtures.StoreTestUserId}: files={vReport.FilesReplayed} records={vReport.RecordsWritten} "
            + $"duplicatesCollapsed={vReport.DuplicatesCollapsed} invalidLines={vReport.InvalidLines}");
        var vAfterRebuild = await PrintCountsAsync("after rebuild", vBusyRepo, vStaleRepo, vStore);

        vSecondPass.Should().Be(0);
        vAfterRebuild.Should().Be(vBeforeRebuild, "REQ-FN-029: a rebuild reproduces the live-sync counts");

        foreach (var vUserId in new[] { Fixtures.StoreTestUserId, Fixtures.StoreTestSecondUserId })
        {
            await vStore.DeleteRepoDataAsync(vUserId, vBusyRepo);
            await vStore.DeleteRepoDataAsync(vUserId, vStaleRepo);
        }
    }

    /// <summary>
    /// Prints the per-user, per-repository, per-stream row counts and returns their sum.
    /// </summary>
    /// <param name="aLabel">What stage of the smoke the numbers describe.</param>
    /// <param name="aBusyRepo">The repository holding the busy fixture.</param>
    /// <param name="aStaleRepo">The repository holding the stale fixture.</param>
    /// <param name="aStore">The store to read through.</param>
    /// <returns>The total row count across both users and both repositories.</returns>
    private async Task<int> PrintCountsAsync(
        string aLabel, string aBusyRepo, string aStaleRepo, PostgresStore aStore)
    {
        var vTotal = 0;
        objOutput.WriteLine($"--- {aLabel} -------------------------------------------");
        foreach (var vUserId in new[] { Fixtures.StoreTestUserId, Fixtures.StoreTestSecondUserId })
        {
            foreach (var vRepo in new[] { aBusyRepo, aStaleRepo })
            {
                var vCounts = await CountAsync(vUserId, vRepo, aStore);
                vTotal += vCounts.Runs + vCounts.Gates + vCounts.Sessions + vCounts.Commits;
                objOutput.WriteLine(
                    $"user {vUserId} {vRepo,-28} Run={vCounts.Runs} Gate={vCounts.Gates} "
                    + $"Session={vCounts.Sessions} Commit={vCounts.Commits}");
            }
        }

        objOutput.WriteLine($"TOTAL rows = {vTotal}");
        return vTotal;
    }

    /// <summary>
    /// Builds a store over a given data root.
    /// </summary>
    /// <param name="aDataRoot">Root of the raw archive the store rebuilds from.</param>
    /// <returns>A store bound to the test database.</returns>
    private PostgresStore NewStore(string aDataRoot)
    {
        var vOptions = new TfLensOptions
        {
            DbConnection = Environment.GetEnvironmentVariable("TfLensDbConnection") ?? DefaultConnection,
            DataRoot = aDataRoot
        };

        return new PostgresStore(Options.Create(vOptions), objParser, NullLogger<PostgresStore>.Instance);
    }

    /// <summary>
    /// Clears a repository's rows and re-registers it as a TechieFlow repository for this user.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo">The repository key under test.</param>
    /// <param name="aStore">The store to use, defaulting to the class-level one.</param>
    /// <returns>A task that completes when the repository is registered and empty.</returns>
    private async Task ResetAsync(int aUserId, string aRepo, PostgresStore? aStore = null)
    {
        var vStore = aStore ?? objStore;

        // Record the pair before writing it: DisposeAsync purges exactly what this class touched, so the
        // shared database is left as it was found (see the remarks on DisposeAsync).
        objTouched.Add((aUserId, aRepo));

        await vStore.DeleteRepoDataAsync(aUserId, aRepo);
        await vStore.WriteUserRepoAsync(new UserRepo
        {
            UserId = aUserId,
            Repo = aRepo,
            Owner = aRepo.Split('/')[0],
            Name = aRepo.Split('/')[1],
            Branch = "main",
            Kind = FrameworkNames.TechieFlow,
            Framework = FrameworkNames.TechieFlow,
            ConnectedTs = "2026-08-26T00:00:00Z"
        });
    }

    /// <summary>
    /// Parses and upserts all four TechieFlow streams of a fixture repository under a test repo key.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo">The repository key rows are stored under.</param>
    /// <param name="aFixtureRepo">Which fixture directory supplies the text.</param>
    /// <param name="aStore">The store to use, defaulting to the class-level one.</param>
    /// <returns>How many rows the database actually wrote.</returns>
    private async Task<int> StoreEveryStreamAsync(
        int aUserId, string aRepo, string aFixtureRepo, PostgresStore? aStore = null)
    {
        var vStore = aStore ?? objStore;
        var vWritten = 0;

        foreach (var vName in StreamNames.TechieFlow)
        {
            var vStream = StreamNames.ToKind(vName);
            var vParsed = objParser.Parse(
                aUserId, aRepo, Fixtures.SourceSha, vStream, Fixtures.Read(aFixtureRepo, vStream));
            vWritten += await vStore.UpsertAsync(vParsed);
        }

        return vWritten;
    }

    /// <summary>
    /// Writes the fixture streams into a raw archive laid out exactly as the fetcher writes it.
    /// </summary>
    /// <param name="aDataRoot">The data root the store rebuilds from.</param>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo">The repository key.</param>
    /// <param name="aFixtureRepo">Which fixture directory supplies the text.</param>
    /// <returns>A task that completes when the four files exist.</returns>
    private static async Task WriteRawArchiveAsync(
        string aDataRoot, int aUserId, string aRepo, string aFixtureRepo)
    {
        var vDirectory = Path.Combine(
            aDataRoot, "raw", aUserId.ToString(), aRepo.Replace("/", "__", StringComparison.Ordinal));
        Directory.CreateDirectory(vDirectory);

        foreach (var vName in StreamNames.TechieFlow)
        {
            var vPath = Path.Combine(vDirectory, $"{vName}-{Fixtures.SourceSha}.jsonl");
            await File.WriteAllTextAsync(vPath, Fixtures.Read(aFixtureRepo, StreamNames.ToKind(vName)));
        }
    }

    /// <summary>
    /// Reads back the stored row counts per stream for one user and repository.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo">The repository key.</param>
    /// <param name="aStore">The store to use, defaulting to the class-level one.</param>
    /// <returns>The per-stream counts.</returns>
    private async Task<StreamCounts> CountAsync(int aUserId, string aRepo, PostgresStore? aStore = null)
    {
        var vStore = aStore ?? objStore;
        return new StreamCounts(
            (await vStore.ReadRunsAsync(aUserId, FrameworkNames.TechieFlow, aRepo)).Count,
            (await vStore.ReadGatesAsync(aUserId, FrameworkNames.TechieFlow, aRepo)).Count,
            (await vStore.ReadSessionsAsync(aUserId, FrameworkNames.TechieFlow, aRepo)).Count,
            (await vStore.ReadCommitsAsync(aUserId, FrameworkNames.TechieFlow, aRepo)).Count);
    }

    /// <summary>
    /// Gives a repository the <c>"SyncState"</c> row a sync would have left, so a rebuild has something
    /// to recompute into.
    /// </summary>
    /// <remarks>
    /// The stream counts are seeded deliberately <b>wrong</b> — a rebuild recomputes all of them, and
    /// <see cref="SyncState.SessionDuplicatesCollapsed"/> is seeded non-zero so a rebuild that added to
    /// the stored figure instead of replacing it would be caught rather than flattered by a zero start.
    /// </remarks>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo">The repository key.</param>
    /// <param name="aStore">The store to write through.</param>
    /// <returns>A task that completes when the row exists.</returns>
    private static Task SeedSyncStateAsync(int aUserId, string aRepo, PostgresStore aStore) =>
        aStore.WriteSyncStateAsync(new SyncState
        {
            UserId = aUserId,
            Repo = aRepo,
            Kind = FrameworkNames.TechieFlow,
            Branch = "main",
            LastSha = Fixtures.SourceSha,
            LastSyncTs = "2026-08-27T00:00:00Z",
            RunsCount = 999,
            GatesCount = 999,
            SessionsCount = 999,
            CommitsCount = 999,
            EventsCount = 999,
            SessionDuplicatesCollapsed = 999
        });

    /// <summary>
    /// Reads back the session-collapse count ingest recorded for one user and repository.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo">The repository key.</param>
    /// <param name="aStore">The store to use, defaulting to the class-level one.</param>
    /// <returns>The stored collapse count, or zero when the repository has no state row.</returns>
    private async Task<int> SessionCollapsesAsync(int aUserId, string aRepo, PostgresStore? aStore = null)
    {
        var vRows = await (aStore ?? objStore).ReadSyncStateAsync(aUserId);
        return vRows.FirstOrDefault(aRow => aRow.Repo == aRepo)?.SessionDuplicatesCollapsed ?? 0;
    }

    /// <summary>Row counts per stream for one user and repository.</summary>
    /// <param name="Runs">Rows in <c>"Run"</c>.</param>
    /// <param name="Gates">Rows in <c>"Gate"</c>.</param>
    /// <param name="Sessions">Rows in <c>"Session"</c>.</param>
    /// <param name="Commits">Rows in <c>"Commit"</c>.</param>
    private sealed record StreamCounts(int Runs, int Gates, int Sessions, int Commits);
}
