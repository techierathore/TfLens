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

    private readonly StreamParser objParser = new();
    private readonly ITestOutputHelper objOutput;
    private readonly string objDataRoot = Path.Combine(
        Path.GetTempPath(), "tflens-store-tests", Guid.NewGuid().ToString("N"));

    private PostgresStore objStore = null!;

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

    /// <summary>Removes the temporary raw archive this class created.</summary>
    /// <returns>A task that completes when the directory is gone.</returns>
    public Task DisposeAsync()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, recursive: true);
        }

        return Task.CompletedTask;
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
        await ResetAsync(Fixtures.DemoUserId, vRepo);

        var vFirst = await StoreEveryStreamAsync(Fixtures.DemoUserId, vRepo, Fixtures.TrSetupRepo);
        var vCountsAfterFirst = await CountAsync(Fixtures.DemoUserId, vRepo);

        var vSecond = await StoreEveryStreamAsync(Fixtures.DemoUserId, vRepo, Fixtures.TrSetupRepo);
        var vCountsAfterSecond = await CountAsync(Fixtures.DemoUserId, vRepo);

        vFirst.Should().BeGreaterThan(0);
        vSecond.Should().Be(0, "every row already exists under its unique index");
        vCountsAfterSecond.Should().Be(vCountsAfterFirst);
    }

    /// <summary>Two users may hold the same repo name and neither sees the other's rows (ADR-013).</summary>
    [Fact]
    public async Task CrossUserIsolationKeepsRowsApart()
    {
        const string vRepo = "tflenstest/StoreIsolation";
        await ResetAsync(Fixtures.DemoUserId, vRepo);
        await ResetAsync(Fixtures.SecondUserId, vRepo);

        await StoreEveryStreamAsync(Fixtures.DemoUserId, vRepo, Fixtures.TrSetupRepo);
        await StoreEveryStreamAsync(Fixtures.SecondUserId, vRepo, Fixtures.TrBlazeUiRepo);

        var vDemo = await CountAsync(Fixtures.DemoUserId, vRepo);
        var vSecond = await CountAsync(Fixtures.SecondUserId, vRepo);

        vDemo.Runs.Should().Be(6);
        vSecond.Runs.Should().Be(2, "the second user stored the smaller fixture, and only that one");
        vDemo.Commits.Should().Be(5);
        vSecond.Commits.Should().Be(2);

        var vDemoSessions = await objStore.ReadSessionsAsync(
            Fixtures.DemoUserId, FrameworkNames.TechieFlow, vRepo);
        vDemoSessions.Should().OnlyContain(aS => aS.UserId == Fixtures.DemoUserId);
    }

    /// <summary>Reads are scoped to one framework, so a figure cannot pool across them (ADR-016).</summary>
    [Fact]
    public async Task ReadsAreScopedToOneFramework()
    {
        const string vRepo = "tflenstest/StoreFramework";
        await ResetAsync(Fixtures.DemoUserId, vRepo);

        await StoreEveryStreamAsync(Fixtures.DemoUserId, vRepo, Fixtures.TrSetupRepo);

        var vTechieFlow = await objStore.ReadRunsAsync(Fixtures.DemoUserId, FrameworkNames.TechieFlow, vRepo);
        var vPlaybook = await objStore.ReadRunsAsync(Fixtures.DemoUserId, FrameworkNames.Playbook, vRepo);

        vTechieFlow.Should().NotBeEmpty();
        vPlaybook.Should().BeEmpty("the repository is registered as techieflow");
    }

    /// <summary>An absent optional round-trips as NULL and a present zero round-trips as zero (REQ-FN-036).</summary>
    [Fact]
    public async Task NullAndZeroSurviveTheRoundTrip()
    {
        const string vRepo = "tflenstest/StoreNullVsZero";
        await ResetAsync(Fixtures.DemoUserId, vRepo);

        await StoreEveryStreamAsync(Fixtures.DemoUserId, vRepo, Fixtures.TrSetupRepo);

        var vRuns = await objStore.ReadRunsAsync(Fixtures.DemoUserId, FrameworkNames.TechieFlow, vRepo);
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

        await ResetAsync(Fixtures.DemoUserId, vRepo, vStore);
        await WriteRawArchiveAsync(vDataRoot, Fixtures.DemoUserId, vRepo, Fixtures.TrSetupRepo);

        // The live sync path: parse each archived file and upsert it.
        await StoreEveryStreamAsync(Fixtures.DemoUserId, vRepo, Fixtures.TrSetupRepo, vStore);
        var vLive = await CountAsync(Fixtures.DemoUserId, vRepo, vStore);

        var vReport = await vStore.RebuildAsync(Fixtures.DemoUserId);
        var vRebuilt = await CountAsync(Fixtures.DemoUserId, vRepo, vStore);

        vReport.FilesReplayed.Should().Be(4);
        vRebuilt.Should().Be(vLive, "a rebuild reads only data/raw and must land on the same numbers");
        vReport.InvalidLines.Should().Be(4, "one malformed line per stream file");
        vReport.DuplicatesCollapsed.Should().Be(7);
    }

    /// <summary>Sync bookkeeping round-trips per user and repository (REQ-FN-025).</summary>
    [Fact]
    public async Task SyncStateRoundTrips()
    {
        const string vRepo = "tflenstest/StoreSyncState";
        await ResetAsync(Fixtures.DemoUserId, vRepo);

        await objStore.WriteSyncStateAsync(new SyncState
        {
            UserId = Fixtures.DemoUserId,
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

        var vRows = await objStore.ReadSyncStateAsync(Fixtures.DemoUserId);
        var vRow = vRows.Single(aS => aS.Repo == vRepo);

        vRow.LastSha.Should().Be(Fixtures.SourceSha);
        vRow.LastError.Should().BeNull();
        vRow.GatesCount.Should().Be(14);

        await objStore.DeleteRepoDataAsync(Fixtures.DemoUserId, vRepo);
        (await objStore.ReadSyncStateAsync(Fixtures.DemoUserId)).Should().NotContain(aS => aS.Repo == vRepo);
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

        foreach (var vUserId in new[] { Fixtures.DemoUserId, Fixtures.SecondUserId })
        {
            await ResetAsync(vUserId, vBusyRepo, vStore);
            await ResetAsync(vUserId, vStaleRepo, vStore);
            await WriteRawArchiveAsync(vDataRoot, vUserId, vBusyRepo, Fixtures.TrSetupRepo);
            await WriteRawArchiveAsync(vDataRoot, vUserId, vStaleRepo, Fixtures.TrBlazeUiRepo);
        }

        var vFirstPass = 0;
        foreach (var vUserId in new[] { Fixtures.DemoUserId, Fixtures.SecondUserId })
        {
            vFirstPass += await StoreEveryStreamAsync(vUserId, vBusyRepo, Fixtures.TrSetupRepo, vStore);
            vFirstPass += await StoreEveryStreamAsync(vUserId, vStaleRepo, Fixtures.TrBlazeUiRepo, vStore);
        }

        objOutput.WriteLine($"PASS 1 rows written = {vFirstPass}");
        await PrintCountsAsync("after pass 1", vBusyRepo, vStaleRepo, vStore);

        var vSecondPass = 0;
        foreach (var vUserId in new[] { Fixtures.DemoUserId, Fixtures.SecondUserId })
        {
            vSecondPass += await StoreEveryStreamAsync(vUserId, vBusyRepo, Fixtures.TrSetupRepo, vStore);
            vSecondPass += await StoreEveryStreamAsync(vUserId, vStaleRepo, Fixtures.TrBlazeUiRepo, vStore);
        }

        objOutput.WriteLine($"PASS 2 rows written = {vSecondPass} (idempotence: must be 0)");
        var vBeforeRebuild = await PrintCountsAsync("after pass 2", vBusyRepo, vStaleRepo, vStore);

        var vReport = await vStore.RebuildAsync(Fixtures.DemoUserId);
        objOutput.WriteLine(
            $"REBUILD user {Fixtures.DemoUserId}: files={vReport.FilesReplayed} records={vReport.RecordsWritten} "
            + $"duplicatesCollapsed={vReport.DuplicatesCollapsed} invalidLines={vReport.InvalidLines}");
        var vAfterRebuild = await PrintCountsAsync("after rebuild", vBusyRepo, vStaleRepo, vStore);

        vSecondPass.Should().Be(0);
        vAfterRebuild.Should().Be(vBeforeRebuild, "REQ-FN-029: a rebuild reproduces the live-sync counts");

        foreach (var vUserId in new[] { Fixtures.DemoUserId, Fixtures.SecondUserId })
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
        foreach (var vUserId in new[] { Fixtures.DemoUserId, Fixtures.SecondUserId })
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

    /// <summary>Row counts per stream for one user and repository.</summary>
    /// <param name="Runs">Rows in <c>"Run"</c>.</param>
    /// <param name="Gates">Rows in <c>"Gate"</c>.</param>
    /// <param name="Sessions">Rows in <c>"Session"</c>.</param>
    /// <param name="Commits">Rows in <c>"Commit"</c>.</param>
    private sealed record StreamCounts(int Runs, int Gates, int Sessions, int Commits);
}
