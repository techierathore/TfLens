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
/// The F-EFFORT columns against the real PostgreSQL 16 database
/// (REQ-FN-088, REQ-FN-095, REQ-FN-103, REQ-FN-104).
/// </summary>
/// <remarks>
/// <para>
/// Integration tests by intent. Three of the four things this cluster adds are properties of the
/// database and nothing else: <c>jsonb</c> round-tripping a per-model map, a <b>partial</b> unique
/// index that lets many TechieFlow rows carry a null hash while still rejecting a repeated Playbook
/// one, and a purge that reaches tables no rebuild replays. An in-memory double would prove none of
/// them, and each is the sort of defect that surfaces as a wrong number rather than as an exception.
/// </para>
/// <para>
/// <b>Non-destructive.</b> Everything runs under reserved id 90006, above any id AppManager will
/// issue, and every <c>(user, repo)</c> pair written here is purged in <see cref="DisposeAsync"/>.
/// Nothing calls <c>RebuildAsync</c>.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PhaseEffortStoreTests : IAsyncLifetime
{
    private const string PhaseRepo = "tflenstest/StorePhaseEffort";

    private readonly StreamParser objParser = new();
    private readonly string objDataRoot = Path.Combine(
        Path.GetTempPath(), "tflens-phase-tests", Guid.NewGuid().ToString("N"));

    private PostgresStore objStore = null!;

    /// <summary>Applies the schema and clears the reserved pair before the first test runs.</summary>
    /// <returns>A task that completes when the store is ready.</returns>
    public async Task InitializeAsync()
    {
        objStore = NewStore();
        await objStore.EnsureSchemaAsync();
        await ResetAsync();
    }

    /// <summary>Purges the reserved pair, so the shared database is left as it was found.</summary>
    /// <returns>A task that completes when the rows and the temporary archive are gone.</returns>
    public async Task DisposeAsync()
    {
        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, recursive: true);
        }
    }

    /// <summary>
    /// A run carrying the three §2.6 fields round-trips them, and a run without them reads back
    /// <c>null</c> rather than zero.
    /// </summary>
    [Fact]
    public async Task TheThreeSchemaFieldsRoundTripAndAbsentStaysNull()
    {
        await ResetAsync();

        const string vObserved = """
            {"v":1,"ts":"2026-09-01T10:00:00Z","app":"tflens","cmd":"build-phase","tokens_scope":"tree",
             "subagent_runs":3,"tokens_out_subagents":610,
             "model_tokens_out":{"claude-opus-5":900,"claude-haiku-4":120}}
            """;

        const string vUnobserved = """
            {"v":1,"ts":"2026-09-01T11:00:00Z","app":"tflens","cmd":"verify-phase","tokens_scope":"main"}
            """;

        await StoreRunsAsync(vObserved.ReplaceLineEndings(" "), vUnobserved.ReplaceLineEndings(" "));

        var vRuns = await objStore.ReadRunsAsync(
            Fixtures.PhaseStoreTestUserId, FrameworkNames.TechieFlow, PhaseRepo);

        var vTree = vRuns.Single(aR => aR.Cmd == "build-phase");
        vTree.SubagentRuns.Should().Be(3);
        vTree.TokensOutSubagents.Should().Be(610);
        vTree.ModelTokensOut.Should().NotBeNull();
        vTree.ModelTokensOut!["claude-opus-5"].Should().Be(900L);
        vTree.ModelTokensOut["claude-haiku-4"].Should().Be(120L);

        var vMain = vRuns.Single(aR => aR.Cmd == "verify-phase");
        vMain.SubagentRuns.Should().BeNull("a main-scope window did not look, which is not a measured zero");
        vMain.TokensOutSubagents.Should().BeNull();
        vMain.ModelTokensOut.Should().BeNull();
    }

    /// <summary>The two cross-edition axes reach their own columns and come back distinct.</summary>
    [Fact]
    public async Task TheCrossEditionAxesRoundTripInTheirOwnColumns()
    {
        await ResetAsync();

        await objStore.UpsertAsync(new ParseResult
        {
            UserId = Fixtures.PhaseStoreTestUserId,
            Repo = PhaseRepo,
            SourceSha = Fixtures.SourceSha,
            Stream = StreamKind.Misses,
            Misses =
            [
                Miss("MISS-pb-20260901-01", aItemId: "ITEM-42", aFoundPhaseGate: "plan-review",
                    aFoundGate: null, aHash: "a1b2c3"),
                Miss("MISS-tf-20260901-02", aItemId: null, aFoundPhaseGate: null,
                    aFoundGate: "acceptance", aHash: null)
            ]
        });

        var vMisses = await objStore.ReadMissesAsync(
            Fixtures.PhaseStoreTestUserId, FrameworkNames.TechieFlow, PhaseRepo);

        var vPlaybook = vMisses.Single(aM => aM.MissId == "MISS-pb-20260901-01");
        vPlaybook.ItemId.Should().Be("ITEM-42");
        vPlaybook.ReqId.Should().BeNull("the Playbook names its requirement axis item, not req");
        vPlaybook.FoundPhaseGate.Should().Be("plan-review");
        vPlaybook.FoundGate.Should().BeNull("a process gate never occupies the assertion-gate column");
        vPlaybook.SourceLineHash.Should().Be("a1b2c3");

        var vTechieFlow = vMisses.Single(aM => aM.MissId == "MISS-tf-20260901-02");
        vTechieFlow.FoundGate.Should().Be("acceptance");
        vTechieFlow.FoundPhaseGate.Should().BeNull();
        vTechieFlow.ItemId.Should().BeNull();
        vTechieFlow.SourceLineHash.Should().BeNull();
    }

    /// <summary>
    /// The partial index lets many null-hash TechieFlow rows coexist while collapsing a repeated
    /// Playbook hash — the two natural keys share one table without colliding (ADR-024).
    /// </summary>
    [Fact]
    public async Task ThePartialIndexSeparatesTheTwoNaturalKeys()
    {
        await ResetAsync();

        await objStore.UpsertAsync(Misses(
            Miss("MISS-tf-1", null, null, "build", null),
            Miss("MISS-tf-2", null, null, "build", null),
            Miss("MISS-tf-3", null, null, "build", null),
            Miss("MISS-pb-1", "ITEM-1", "verify", null, "hash-one")));

        var vAfterFirst = await objStore.ReadMissesAsync(
            Fixtures.PhaseStoreTestUserId, FrameworkNames.TechieFlow, PhaseRepo);
        vAfterFirst.Should().HaveCount(4, "three null hashes must not collide with each other");

        // A different miss id, but the SAME source line: the Playbook key must reject it.
        await objStore.UpsertAsync(Misses(Miss("MISS-pb-2", "ITEM-1", "verify", null, "hash-one")));

        var vAfterRepeat = await objStore.ReadMissesAsync(
            Fixtures.PhaseStoreTestUserId, FrameworkNames.TechieFlow, PhaseRepo);
        vAfterRepeat.Should().HaveCount(4, "the same source line is the same miss, whatever id it carries");
        vAfterRepeat.Should().ContainSingle(aM => aM.SourceLineHash == "hash-one");
    }

    /// <summary>
    /// <c>DeleteRepoDataAsync</c> leaves zero rows in all three phase tables — a table the purge forgets
    /// is a table whose rows go on feeding every figure for a repository the owner removed (BRD-115).
    /// </summary>
    [Fact]
    public async Task DeleteRepoDataPurgesAllThreePhaseTables()
    {
        await ResetAsync();
        await SeedPhaseRowsAsync();

        (await CountPhaseRowsAsync()).Should().Be(3, "the seed writes one row into each phase table");

        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        (await CountPhaseRowsAsync()).Should().Be(0);
    }

    /// <summary>Provider cost survives the round trip at full decimal precision, with no float drift.</summary>
    [Fact]
    public async Task PhaseCostKeepsItsDecimalPrecision()
    {
        await ResetAsync();
        await SeedPhaseRowsAsync();

        await using var vConnection = new NpgsqlConnection(ConnectionString());
        await vConnection.OpenAsync();

        var vCost = await vConnection.ExecuteScalarAsync<decimal>(
            """SELECT "CostUsd" FROM "PbPhaseExecution" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""",
            new { aUserId = Fixtures.PhaseStoreTestUserId, aRepo = PhaseRepo });

        vCost.Should().Be(
            0.0123456789m,
            "money is stored as exact decimal; a binary float would not come back as the value written");
    }

    /// <summary>Writes one row into each of the three phase tables for the reserved pair.</summary>
    /// <returns>A task that completes when the rows exist.</returns>
    private async Task SeedPhaseRowsAsync()
    {
        await using var vConnection = new NpgsqlConnection(ConnectionString());
        await vConnection.OpenAsync();

        var vArgs = new { aUserId = Fixtures.PhaseStoreTestUserId, aRepo = PhaseRepo };

        await vConnection.ExecuteAsync(
            """
            INSERT INTO "PbPhaseExecution" ("UserId","Repo","PhaseExecutionId","Phase","ElapsedMs",
                "ObservedActiveMs","CostUsd","SubagentsSpawned","TokensScope")
            VALUES (@aUserId,@aRepo,'PE-1','build-phase',5000,4000,0.0123456789,2,'tree')
            ON CONFLICT DO NOTHING
            """,
            vArgs);

        await vConnection.ExecuteAsync(
            """
            INSERT INTO "PbPhaseModelUsage" ("UserId","Repo","PhaseExecutionId","Model","TokensOut","CostUsd")
            VALUES (@aUserId,@aRepo,'PE-1','claude-opus-5',900,0.0100000000)
            ON CONFLICT DO NOTHING
            """,
            vArgs);

        await vConnection.ExecuteAsync(
            """
            INSERT INTO "PbPhaseSubagent" ("UserId","Repo","PhaseExecutionId","SessionId",
                "ParentSessionId","Agent","TokensOut","CostUsd")
            VALUES (@aUserId,@aRepo,'PE-1','S-child','S-root','builder',120,0.0023456789)
            ON CONFLICT DO NOTHING
            """,
            vArgs);
    }

    /// <summary>Counts the reserved pair's rows across the three phase tables.</summary>
    /// <returns>The total row count.</returns>
    private async Task<int> CountPhaseRowsAsync()
    {
        await using var vConnection = new NpgsqlConnection(ConnectionString());
        await vConnection.OpenAsync();

        var vTotal = 0;
        foreach (var vTable in new[] { "PbPhaseExecution", "PbPhaseModelUsage", "PbPhaseSubagent" })
        {
            vTotal += await vConnection.ExecuteScalarAsync<int>(
                $"""SELECT COUNT(*)::int FROM "{vTable}" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""",
                new { aUserId = Fixtures.PhaseStoreTestUserId, aRepo = PhaseRepo });
        }

        return vTotal;
    }

    /// <summary>Parses and stores the given <c>runs.jsonl</c> lines under the reserved pair.</summary>
    /// <param name="aLines">The JSONL lines.</param>
    /// <returns>A task that completes when the rows are written.</returns>
    private async Task StoreRunsAsync(params string[] aLines)
    {
        var vParsed = objParser.Parse(
            Fixtures.PhaseStoreTestUserId,
            PhaseRepo,
            Fixtures.SourceSha,
            StreamKind.Runs,
            string.Join('\n', aLines));

        await objStore.UpsertAsync(vParsed);
    }

    /// <summary>Wraps miss records in the parse result the store's upsert takes.</summary>
    /// <param name="aMisses">The records to write.</param>
    /// <returns>The parse result.</returns>
    private static ParseResult Misses(params MissRecord[] aMisses) => new()
    {
        UserId = Fixtures.PhaseStoreTestUserId,
        Repo = PhaseRepo,
        SourceSha = Fixtures.SourceSha,
        Stream = StreamKind.Misses,
        Misses = aMisses
    };

    /// <summary>Builds one miss carrying the axes under test.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aItemId">The Playbook requirement axis, or <c>null</c> for a TechieFlow row.</param>
    /// <param name="aFoundPhaseGate">The Playbook process gate, or <c>null</c>.</param>
    /// <param name="aFoundGate">The TechieFlow assertion gate, or <c>null</c>.</param>
    /// <param name="aHash">The Playbook source-line hash, or <c>null</c> for a TechieFlow row.</param>
    /// <returns>The record.</returns>
    private static MissRecord Miss(
        string aMissId, string? aItemId, string? aFoundPhaseGate, string? aFoundGate, string? aHash) => new()
    {
        UserId = Fixtures.PhaseStoreTestUserId,
        Repo = PhaseRepo,
        SourceSha = Fixtures.SourceSha,
        Ts = "2026-09-01T12:00:00Z",
        MissId = aMissId,
        ItemId = aItemId,
        FoundPhaseGate = aFoundPhaseGate,
        FoundGate = aFoundGate,
        SourceLineHash = aHash
    };

    /// <summary>Clears the reserved pair's rows and re-registers the repository.</summary>
    /// <returns>A task that completes when the repository is registered and empty.</returns>
    private async Task ResetAsync()
    {
        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);
        await objStore.WriteUserRepoAsync(new UserRepo
        {
            UserId = Fixtures.PhaseStoreTestUserId,
            Repo = PhaseRepo,
            Owner = PhaseRepo.Split('/')[0],
            Name = PhaseRepo.Split('/')[1],
            Branch = "main",
            Kind = FrameworkNames.TechieFlow,
            Framework = FrameworkNames.TechieFlow,
            ConnectedTs = "2026-09-01T00:00:00Z"
        });
    }

    /// <summary>The connection string, resolved the way the application resolves it.</summary>
    /// <returns>The configured connection string, or an empty one when nothing is configured.</returns>
    private static string ConnectionString() => TestDatabase.ConnectionStringOrNull() ?? string.Empty;

    /// <summary>Builds a store bound to the test database.</summary>
    /// <returns>The store.</returns>
    private PostgresStore NewStore()
    {
        var vOptions = new TfLensOptions { DbConnection = ConnectionString(), DataRoot = objDataRoot };
        return new PostgresStore(Options.Create(vOptions), objParser, NullLogger<PostgresStore>.Instance);
    }
}
