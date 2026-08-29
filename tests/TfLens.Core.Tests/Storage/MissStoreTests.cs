using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TfLens.Core;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Storage;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Storage;

/// <summary>
/// The three miss tables against the real PostgreSQL 16 database (REQ-FN-074, BRD-115).
/// </summary>
/// <remarks>
/// <para>
/// These are integration tests by intent: the unique indexes, the <c>COALESCE("FixRunId", '')</c>
/// expression key, <c>jsonb</c> round-tripping and NULL-versus-zero are properties of the database and
/// an in-memory double would prove none of them.
/// </para>
/// <para>
/// <b>Non-destructive.</b> Everything here runs under the reserved ids 90004/90005, which are above any
/// id AppManager will issue, and every <c>(user, repo)</c> pair this class writes is purged in
/// <see cref="DisposeAsync"/>. Nothing calls <c>RebuildAsync</c> for a real account — nor for the pair
/// <c>PostgresStoreTests</c> owns, because a rebuild empties every stream row for the user it is given
/// and the two classes can run at the same time.
/// </para>
/// </remarks>
public sealed class MissStoreTests : IAsyncLifetime
{
    // No hard-coded connection: resolved the way the app resolves it (TestDatabase, 2026-08-29).
    private static string DefaultConnection => TestDatabase.ConnectionStringOrNull() ?? string.Empty;

    /// <summary>Miss records the TrSetup fixture holds after its own dedupe.</summary>
    private const int FixtureMisses = 3;

    /// <summary>Fix records the TrSetup fixture holds.</summary>
    private const int FixtureMissFixes = 2;

    /// <summary>Amendment records the TrSetup fixture holds.</summary>
    private const int FixtureMissAmends = 2;

    private readonly StreamParser objParser = new();
    private readonly string objDataRoot = Path.Combine(
        Path.GetTempPath(), "tflens-miss-tests", Guid.NewGuid().ToString("N"));

    private readonly HashSet<(int UserId, string Repo)> objTouched = [];

    private PostgresStore objStore = null!;

    /// <summary>Applies the schema before the first test in the class runs.</summary>
    /// <returns>A task that completes when the store is ready.</returns>
    public async Task InitializeAsync()
    {
        objStore = NewStore(objDataRoot);
        await objStore.EnsureSchemaAsync();
    }

    /// <summary>Purges every pair this class wrote, so the shared database is left as it was found.</summary>
    /// <returns>A task that completes when the rows and the temporary archive are gone.</returns>
    public async Task DisposeAsync()
    {
        foreach (var (vUserId, vRepo) in objTouched)
        {
            await objStore.DeleteRepoDataAsync(vUserId, vRepo);
        }

        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, recursive: true);
        }
    }

    /// <summary>All three kinds store and read back through their own <c>ITelemetryStore</c> method.</summary>
    [Fact]
    public async Task AllThreeMissKindsRoundTrip()
    {
        const string vRepo = "tflenstest/StoreMisses";
        await ResetAsync(Fixtures.MissStoreTestUserId, vRepo);
        await StoreMissesAsync(Fixtures.MissStoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        var vMisses = await objStore.ReadMissesAsync(Fixtures.MissStoreTestUserId, FrameworkNames.TechieFlow, vRepo);
        var vFixes = await objStore.ReadMissFixesAsync(Fixtures.MissStoreTestUserId, FrameworkNames.TechieFlow, vRepo);
        var vAmends = await objStore.ReadMissAmendsAsync(Fixtures.MissStoreTestUserId, FrameworkNames.TechieFlow, vRepo);

        vMisses.Should().HaveCount(FixtureMisses);
        vFixes.Should().HaveCount(FixtureMissFixes);
        vAmends.Should().HaveCount(FixtureMissAmends);
        vMisses.Should().OnlyContain(aM => aM.UserId == Fixtures.MissStoreTestUserId);
    }

    /// <summary>Storing the same file twice writes nothing the second time (REQ-FN-035).</summary>
    [Fact]
    public async Task MissUpsertIsIdempotent()
    {
        const string vRepo = "tflenstest/StoreMissIdempotence";
        await ResetAsync(Fixtures.MissStoreTestUserId, vRepo);

        var vFirst = await StoreMissesAsync(Fixtures.MissStoreTestUserId, vRepo, Fixtures.TrSetupRepo);
        var vSecond = await StoreMissesAsync(Fixtures.MissStoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        vFirst.Should().Be(FixtureMisses + FixtureMissFixes + FixtureMissAmends);
        vSecond.Should().Be(0, "every row already exists under its unique index");
    }

    /// <summary>An absent optional round-trips as NULL and a present zero round-trips as zero (REQ-FN-036).</summary>
    [Fact]
    public async Task MissNullAndZeroSurviveTheRoundTrip()
    {
        const string vRepo = "tflenstest/StoreMissNullVsZero";
        await ResetAsync(Fixtures.MissStoreTestUserId, vRepo);
        await StoreMissesAsync(Fixtures.MissStoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        var vMisses = await objStore.ReadMissesAsync(Fixtures.MissStoreTestUserId, FrameworkNames.TechieFlow, vRepo);
        var vUnassessed = vMisses.Single(aM => aM.MissId == "MISS-TrSetup-20260825-01");
        vUnassessed.WhyMissed.Should().BeNull("null means not assessed and is never coerced to a bucket");

        var vNoReq = vMisses.Single(aM => aM.MissId == "MISS-TrSetup-20260820-01");
        vNoReq.ReqId.Should().BeNull();
        vNoReq.OriginModel.Should().BeNull();

        var vFixes = await objStore.ReadMissFixesAsync(Fixtures.MissStoreTestUserId, FrameworkNames.TechieFlow, vRepo);
        var vUnattributable = vFixes.Single(aF => aF.CostAttribution == "none");
        vUnattributable.FixRunId.Should().BeNull();
        vUnattributable.TokensOut.Should().BeNull("an unmeasured window is not zero tokens");

        var vSole = vFixes.Single(aF => aF.CostAttribution == "sole");
        vSole.TokensCacheWrite.Should().Be(0, "a measured zero is a measurement");
        vSole.CostUsd.Should().Be(0.0412m);
    }

    /// <summary>
    /// <b>The guardrail.</b> <c>DeleteRepoDataAsync</c> leaves zero rows in all three miss tables
    /// (REQ-FN-074, BRD-115).
    /// </summary>
    /// <remarks>
    /// Missing one leaves orphaned rows that reappear in every figure — the worst class of bug in a
    /// product whose promise is correct numbers. The counts are read straight from the tables rather
    /// than through the store's framework-scoped reads, so a purge that left rows behind but broke the
    /// <c>"UserRepo"</c> join could not pass by looking empty.
    /// </remarks>
    [Fact]
    public async Task DeleteRepoDataLeavesZeroRowsInAllThreeMissTables()
    {
        const string vRepo = "tflenstest/StoreMissPurge";
        await ResetAsync(Fixtures.MissStoreTestUserId, vRepo);
        await StoreMissesAsync(Fixtures.MissStoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        (await RawCountAsync("Miss", Fixtures.MissStoreTestUserId, vRepo)).Should().Be(FixtureMisses);
        (await RawCountAsync("MissFix", Fixtures.MissStoreTestUserId, vRepo)).Should().Be(FixtureMissFixes);
        (await RawCountAsync("MissAmend", Fixtures.MissStoreTestUserId, vRepo)).Should().Be(FixtureMissAmends);

        await objStore.DeleteRepoDataAsync(Fixtures.MissStoreTestUserId, vRepo);

        (await RawCountAsync("Miss", Fixtures.MissStoreTestUserId, vRepo)).Should().Be(0);
        (await RawCountAsync("MissFix", Fixtures.MissStoreTestUserId, vRepo)).Should().Be(0);
        (await RawCountAsync("MissAmend", Fixtures.MissStoreTestUserId, vRepo)).Should().Be(0);
    }

    /// <summary>A purge for one user leaves another user's copy of the same repository alone (ADR-013).</summary>
    [Fact]
    public async Task PurgingOneUsersMissesLeavesTheOthersAlone()
    {
        const string vRepo = "tflenstest/StoreMissIsolation";
        await ResetAsync(Fixtures.MissStoreTestUserId, vRepo);
        await ResetAsync(Fixtures.MissStoreTestSecondUserId, vRepo);
        await StoreMissesAsync(Fixtures.MissStoreTestUserId, vRepo, Fixtures.TrSetupRepo);
        await StoreMissesAsync(Fixtures.MissStoreTestSecondUserId, vRepo, Fixtures.TrBlazeUiRepo);

        await objStore.DeleteRepoDataAsync(Fixtures.MissStoreTestUserId, vRepo);

        (await RawCountAsync("Miss", Fixtures.MissStoreTestUserId, vRepo)).Should().Be(0);
        (await RawCountAsync("Miss", Fixtures.MissStoreTestSecondUserId, vRepo)).Should().Be(1);
    }

    /// <summary>A rebuild from the raw archive reproduces the miss counts exactly (REQ-FN-074).</summary>
    /// <remarks>
    /// Amendments are folded at read time and never at ingest, so the replay writes the same rows and a
    /// fold over them re-derives the same values — which is what makes a rebuild safe to run at all.
    /// </remarks>
    [Fact]
    public async Task RebuildReplaysTheMissStreamWithIdenticalCounts()
    {
        var vDataRoot = Path.Combine(objDataRoot, "rebuild");
        var vStore = NewStore(vDataRoot);
        const string vRepo = "tflenstest/StoreMissRebuild";

        await ResetAsync(Fixtures.MissStoreTestUserId, vRepo, vStore);
        await WriteRawArchiveAsync(vDataRoot, Fixtures.MissStoreTestUserId, vRepo);
        await StoreMissesAsync(Fixtures.MissStoreTestUserId, vRepo, Fixtures.TrSetupRepo, vStore);

        var vLive = await CountAsync(vStore, Fixtures.MissStoreTestUserId, vRepo);

        await vStore.RebuildAsync(Fixtures.MissStoreTestUserId);

        var vRebuilt = await CountAsync(vStore, Fixtures.MissStoreTestUserId, vRepo);
        vRebuilt.Should().Be(vLive, "a rebuild reads only data/raw and must land on the same numbers");
        vRebuilt.Should().Be((FixtureMisses, FixtureMissFixes, FixtureMissAmends));
    }

    /// <summary>Coverage reports the misses stream as one row over the three tables (BRD-127).</summary>
    [Fact]
    public async Task CoverageReportsMissesAsOneStreamRow()
    {
        const string vRepo = "tflenstest/StoreMissCoverage";
        await ResetAsync(Fixtures.MissStoreTestUserId, vRepo);
        await StoreMissesAsync(Fixtures.MissStoreTestUserId, vRepo, Fixtures.TrSetupRepo);

        var vFacts = await objStore.ReadCoverageFactsAsync(Fixtures.MissStoreTestUserId);
        var vRows = vFacts.Streams.Where(aS => aS.Repo == vRepo && aS.Stream == StreamNames.Misses).ToList();

        vRows.Should().HaveCount(1, "the stream is one file, so Coverage gains one row and not three");
        vRows[0].Records.Should().Be(FixtureMisses + FixtureMissFixes + FixtureMissAmends);
        vFacts.UnknownFields.Should().NotContain(aF => aF.Repo == vRepo && aF.Stream == StreamNames.Misses);
    }

    /// <summary>The schema-only import columns round-trip without breaking a fetched source.</summary>
    /// <remarks>
    /// REQ-FN-084 / REQ-FN-085 own the behaviour; this only proves the columns exist, default correctly
    /// and do not disturb the <c>LastSha</c>-or-<c>BundleSha</c> invariant for anything TfLens writes.
    /// </remarks>
    [Fact]
    public async Task UserRepoCarriesTheImportColumnsAndDefaultsToSynced()
    {
        const string vRepo = "tflenstest/StoreMissSourceKind";
        await ResetAsync(Fixtures.MissStoreTestUserId, vRepo);

        var vRows = await objStore.ReadUserReposAsync(Fixtures.MissStoreTestUserId);
        var vRow = vRows.Single(aR => aR.Repo == vRepo);

        vRow.SourceKind.Should().Be(SourceKinds.Api);
        vRow.BundleSha.Should().BeNull("a fetched source's dataset identity is its commit SHA");
        vRow.LastImportTs.Should().BeNull();
    }

    /// <summary>Builds a store over a given data root.</summary>
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

    /// <summary>Clears a repository's rows and re-registers it for this user.</summary>
    /// <param name="aUserId">The reserved test user id.</param>
    /// <param name="aRepo">The repository key under test.</param>
    /// <param name="aStore">The store to use, defaulting to the class-level one.</param>
    /// <returns>A task that completes when the repository is registered and empty.</returns>
    private async Task ResetAsync(int aUserId, string aRepo, PostgresStore? aStore = null)
    {
        var vStore = aStore ?? objStore;
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
            ConnectedTs = "2026-08-28T00:00:00Z"
        });
    }

    /// <summary>Parses and upserts one fixture repository's misses stream under a test repo key.</summary>
    /// <param name="aUserId">The reserved test user id.</param>
    /// <param name="aRepo">The repository key rows are stored under.</param>
    /// <param name="aFixtureRepo">Which fixture directory supplies the text.</param>
    /// <param name="aStore">The store to use, defaulting to the class-level one.</param>
    /// <returns>How many rows the database actually wrote.</returns>
    private async Task<int> StoreMissesAsync(
        int aUserId, string aRepo, string aFixtureRepo, PostgresStore? aStore = null)
    {
        var vParsed = objParser.Parse(
            aUserId,
            aRepo,
            Fixtures.SourceSha,
            StreamKind.Misses,
            Fixtures.Read(aFixtureRepo, StreamKind.Misses));

        return await (aStore ?? objStore).UpsertAsync(vParsed);
    }

    /// <summary>Writes the TrSetup misses fixture into a raw archive laid out as the fetcher writes it.</summary>
    /// <param name="aDataRoot">The data root the store rebuilds from.</param>
    /// <param name="aUserId">The reserved test user id.</param>
    /// <param name="aRepo">The repository key.</param>
    /// <returns>A task that completes when the file exists.</returns>
    private static async Task WriteRawArchiveAsync(string aDataRoot, int aUserId, string aRepo)
    {
        var vDirectory = Path.Combine(
            aDataRoot, "raw", aUserId.ToString(), aRepo.Replace("/", "__", StringComparison.Ordinal));
        Directory.CreateDirectory(vDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(vDirectory, $"{StreamNames.Misses}-{Fixtures.SourceSha}.jsonl"),
            Fixtures.Read(Fixtures.TrSetupRepo, StreamKind.Misses));
    }

    /// <summary>Reads back the three miss row counts through the store's own reads.</summary>
    /// <param name="aStore">The store to read through.</param>
    /// <param name="aUserId">The reserved test user id.</param>
    /// <param name="aRepo">The repository key.</param>
    /// <returns>The counts of misses, fixes and amendments.</returns>
    private static async Task<(int Misses, int Fixes, int Amends)> CountAsync(
        PostgresStore aStore, int aUserId, string aRepo) =>
        ((await aStore.ReadMissesAsync(aUserId, FrameworkNames.TechieFlow, aRepo)).Count,
            (await aStore.ReadMissFixesAsync(aUserId, FrameworkNames.TechieFlow, aRepo)).Count,
            (await aStore.ReadMissAmendsAsync(aUserId, FrameworkNames.TechieFlow, aRepo)).Count);

    /// <summary>
    /// Counts rows in one table directly, bypassing the framework join the store's reads use.
    /// </summary>
    /// <param name="aTable">The quoted table name.</param>
    /// <param name="aUserId">The reserved test user id.</param>
    /// <param name="aRepo">The repository key.</param>
    /// <returns>The row count.</returns>
    private static async Task<int> RawCountAsync(string aTable, int aUserId, string aRepo)
    {
        var vConnectionString = Environment.GetEnvironmentVariable("TfLensDbConnection") ?? DefaultConnection;
        await using var vConnection = new NpgsqlConnection(vConnectionString);
        await vConnection.OpenAsync();

        var vSql = $"""SELECT COUNT(*)::int FROM "{aTable}" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""";
        return await vConnection.ExecuteScalarAsync<int>(vSql, new { aUserId, aRepo });
    }
}
