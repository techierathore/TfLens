using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TfLens.Core;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Playbook;
using TfLens.Core.Storage;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Storage;

/// <summary>
/// REQ-FN-094 (BRD-153, ADR-023) — schema-2 <c>phase-metric</c> ingest against the real PostgreSQL 16:
/// re-import upserts rather than duplicating, a more complete reading of a window replaces the partial
/// one, and every retained value survives the round trip.
/// </summary>
/// <remarks>
/// <para>
/// Integration by intent. The idempotence claim is a property of an <c>ON CONFLICT … DO UPDATE …
/// WHERE</c> clause and of nothing else: an in-memory double would report whatever the double was
/// written to report, and the failure mode being guarded — a re-import counted as new rows, or an
/// identical bundle silently rewriting every row — is a wrong number rather than an exception.
/// </para>
/// <para>
/// <b>Non-destructive.</b> Everything runs under reserved id 90006 against a repository name no other
/// class uses, and the pair is purged in <see cref="DisposeAsync"/>. Nothing calls <c>RebuildAsync</c>.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PhaseIngestStoreTests : IAsyncLifetime
{
    /// <summary>The repository this class alone writes under.</summary>
    private const string PhaseRepo = "tflenstest/StorePhaseIngest";

    /// <summary>The bundle sha256 the fixture export arrives under.</summary>
    private const string BundleSha = "9f8e7d6c5b4a39281706f5e4d3c2b1a09f8e7d6c5b4a39281706f5e4d3c2b1a0";

    private readonly StreamParser objParser = new();
    private readonly string objDataRoot = Path.Combine(
        Path.GetTempPath(), "tflens-phase-ingest-tests", Guid.NewGuid().ToString("N"));

    private PostgresStore objStore = null!;

    /// <summary>Applies the schema and clears the reserved pair before the first test runs.</summary>
    /// <returns>A task that completes when the store is ready.</returns>
    public async Task InitializeAsync()
    {
        objStore = NewStore();
        await objStore.EnsureSchemaAsync();
        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);
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
    /// Acceptance 1 — re-importing an unchanged bundle adds no rows and duplicates no execution.
    /// </summary>
    [Fact]
    public async Task ReimportOfTheSameBundleWritesNoNewRows()
    {
        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        var vText = string.Join('\n', Line("PE-1"), Line("PE-2"));

        var vFirst = await objStore.UpsertAsync(Parse(vText));
        var vSecond = await objStore.UpsertAsync(Parse(vText));

        vFirst.Should().BeGreaterThan(0, "the first import writes the rows");
        vSecond.Should().Be(0, "an identical re-import changes nothing and must not count as new rows");

        var vStored = await objStore.ReadPhaseExecutionsAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);
        vStored.Should().HaveCount(2);
        vStored.Select(aE => aE.PhaseExecutionId).Should().BeEquivalentTo(["PE-1", "PE-2"]);
    }

    /// <summary>
    /// A window read further on replaces the partial reading stored for the same execution id.
    /// </summary>
    /// <remarks>
    /// This is why the statement is an upsert rather than <c>DO NOTHING</c>: the exporter re-emits every
    /// currently readable window, so the EOF row stored on Monday is legitimately superseded by the
    /// closed one on Tuesday, and refusing the update would freeze the incomplete row forever.
    /// </remarks>
    [Fact]
    public async Task AMoreCompleteWindowReplacesTheStoredOne()
    {
        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        await objStore.UpsertAsync(Parse(Line("PE-open", aComplete: false)));
        var vUpdated = await objStore.UpsertAsync(Parse(Line("PE-open")));

        var vStored = await objStore.ReadPhaseExecutionsAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        vUpdated.Should().Be(1, "the window closed, so the row genuinely changed");
        vStored.Should().ContainSingle();
        vStored.Single().Complete.Should().BeTrue();
        vStored.Single().ElapsedMs.Should().Be(120000L);
        vStored.Single().EndReason.Should().Be("idle");
    }

    /// <summary>
    /// Every retained value survives the round trip: provenance, decimal cost, the per-model split, the
    /// sub-agent sessions, and a source <c>null</c> that stays <c>null</c>.
    /// </summary>
    [Fact]
    public async Task TheThreeTablesRoundTripEveryRetainedValue()
    {
        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        await objStore.UpsertAsync(Parse(Line("PE-round")));

        var vExecution = (await objStore.ReadPhaseExecutionsAsync(
            Fixtures.PhaseStoreTestUserId, PhaseRepo)).Single();
        var vModels = await objStore.ReadPhaseModelUsagesAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);
        var vSubagents = await objStore.ReadPhaseSubagentsAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        vExecution.SourceSchema.Should().Be(2);
        vExecution.SourceHarness.Should().Be("opencode");
        vExecution.ImportedAt.Should().NotBeNullOrWhiteSpace();
        vExecution.CostUsd.Should().Be(0.4100000000m, "money is fixed-precision decimal, never a float");
        vExecution.Overflow.Should().Contain(PlaybookPhaseAdapter.ImporterVersion);
        vExecution.Tier.Should().BeNull("a value the producer did not send stays null, never zero or \"\"");

        vModels.Should().HaveCount(2);
        vModels.Single(aM => aM.Model == "quiet").TokensOut.Should().Be(200L);
        vSubagents.Should().HaveCount(2, "the grandchild is stored once, beneath its parent");
        vSubagents.Single(aS => aS.SessionId == "c2").Agent
            .Should().BeNull("an absent agent type is never inferred");
    }

    /// <summary>
    /// A quarantined row is stored and readable, with its reason on the row (REQ-FN-096).
    /// </summary>
    [Fact]
    public async Task AQuarantinedRowIsStoredWithItsReason()
    {
        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        await objStore.UpsertAsync(Parse(Line("PE-bad", aTokensIn: 7)));

        var vStored = (await objStore.ReadPhaseExecutionsAsync(
            Fixtures.PhaseStoreTestUserId, PhaseRepo)).Single();

        vStored.DataQualityIssues.Should().Contain(PlaybookPhaseInvariants.TokensInMismatch);
        PlaybookPhaseInvariants.Validate(vStored).IsQuarantined
            .Should().BeTrue("the verdict is re-derived from the stored row, never trusted from a column");
    }

    /// <summary>
    /// The report is built from the stored rows through one engine entry point, with the quarantined
    /// row visible and out of every total (REQ-FN-096, REQ-FN-102).
    /// </summary>
    [Fact]
    public async Task TheReportIsBuiltFromTheStoredRows()
    {
        await objStore.DeleteRepoDataAsync(Fixtures.PhaseStoreTestUserId, PhaseRepo);

        await objStore.UpsertAsync(Parse(string.Join(
            '\n', Line("PE-1"), Line("PE-2"), Line("PE-3"), Line("PE-bad", aTokensIn: 7))));

        var vReport = await PlaybookPhaseEffort.ReadAsync(objStore, Fixtures.PhaseStoreTestUserId, PhaseRepo);

        vReport.Harness.IsSupported.Should().BeTrue("the rows name a harness with a normalized producer");
        vReport.Executions.Should().HaveCount(4, "a quarantined row is displayed, never dropped");
        vReport.Quality.Quarantined.Should().Be(1);
        vReport.ElapsedMsMedian.N.Should().Be(3);
        vReport.ElapsedMsMedian.Exclusions.Should().Contain(aE => aE.Code == "quarantined" && aE.Records == 1);
        vReport.Models.Should().HaveCount(2, "per-model figures come from the per-model rows");
        vReport.MeasuredCostUsd.Usd.Should().Be(1.23m, "three complete windows at 0.41 each");
    }

    /// <summary>Parses one NDJSON text through the shared parser, exactly as an import does.</summary>
    /// <param name="aText">The NDJSON text.</param>
    /// <returns>The parse result.</returns>
    private ParseResult Parse(string aText) => objParser.Parse(
        Fixtures.PhaseStoreTestUserId, PhaseRepo, BundleSha, StreamKind.PhaseMetrics, aText);

    /// <summary>Builds one schema-2 line carrying two models and a nested sub-agent session.</summary>
    /// <param name="aId">The phase execution id.</param>
    /// <param name="aComplete">Whether the window closed.</param>
    /// <param name="aTokensIn">The input-side compatibility total; the true sum by default.</param>
    /// <returns>One NDJSON line.</returns>
    private static string Line(string aId, bool aComplete = true, long aTokensIn = 48213) =>
        $$"""
        {"schema":2,"kind":"phase-metric","phase_execution_id":"{{aId}}","phase":"verify",
         "started_at":"2026-08-31T09:10:00.000Z",
         "ended_at":{{(aComplete ? "\"2026-08-31T09:12:00.000Z\"" : "null")}},
         "elapsed_ms":{{(aComplete ? "120000" : "null")}},
         "complete":{{(aComplete ? "true" : "false")}},
         "end_reason":"{{(aComplete ? "idle" : "eof")}}",
         "model":"dominant",
         "models":[{"model":"dominant","turns":10,"tokens":{"input":1,"output":1,"reasoning":0,"cache_read":0,"cache_write":0},"tokens_in":1,"tokens_out":700,"cost_usd":0.30,"cost_status":"complete","active_ms":100},
                   {"model":"quiet","turns":2,"tokens":{"input":1,"output":1,"reasoning":0,"cache_read":0,"cache_write":0},"tokens_in":1,"tokens_out":200,"cost_usd":0.11,"cost_status":"complete","active_ms":50}],
         "tokens":{"input":31203,"output":7900,"reasoning":1220,"cache_read":16000,"cache_write":1010},
         "tokens_in":{{aTokensIn}},"tokens_out":9120,"cost_usd":0.41,"attempt":2,"gate_verdict":"FAIL",
         "project_type":"dotnet-react","timestamp":"2026-08-31T09:12:00.000Z","session_id":"ses_123",
         "harness":"opencode","granularity":"message","turns":12,
         "observed_active_effort":{"assistant_elapsed_ms":78000,"tool_elapsed_ms":31000,"observed_active_ms":84000,"coverage":"complete"},
         "data_quality":{"valid":true,"issues":[],"token_status":"complete","cost_status":"complete"},
         "tokens_scope":"tree",
         "subagents":{"count":1,"spawned":2,"contributors":1,
                      "sessions":[{"session_id":"c2","parent_session_id":"root","tokens_out":120,
                                   "sessions":[{"session_id":"g1","tokens_out":40}]}]} }
        """.ReplaceLineEndings(" ");

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
