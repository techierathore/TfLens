using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Provenance;

namespace TfLens.Core.Storage;

/// <summary>
/// The PostgreSQL store — Dapper over Npgsql, one connection per unit of work (REQ-FN-030).
/// </summary>
/// <remarks>
/// <para>
/// <b>Isolation is a parameter, not a filter (ADR-013).</b> Every method here takes <c>aUserId</c>, and
/// it reaches the SQL as a <c>WHERE "UserId" = @aUserId</c> that no caller can forget, because there is
/// no overload without it. Framework scoping (ADR-016) is a join to <c>"UserRepo"."Framework"</c> for
/// the same reason: a figure cannot pool across frameworks any more than across users.
/// </para>
/// <para>
/// <b>Every identifier is double-quoted.</b> PostgreSQL folds unquoted identifiers to lower case, which
/// would silently destroy the PascalCase column names the Coding Standards fix — <c>"Gate"."ReqId"</c>
/// stays <c>ReqId</c> only because it is quoted here and in the DDL.
/// </para>
/// <para>
/// <b>Writes are idempotent.</b> Every insert is <c>INSERT … ON CONFLICT DO NOTHING</c> against the
/// unique indexes that encode the dedupe keys, so re-parsing the same archived file writes nothing the
/// second time (REQ-FN-033..035) and a rebuild reproduces the live counts exactly (REQ-FN-029).
/// </para>
/// </remarks>
public sealed class PostgresStore : ITelemetryStore
{
    /// <summary>
    /// The stream tables, in the order a rebuild truncates them and a repo removal purges them.
    /// </summary>
    /// <remarks>
    /// <b>One list, both jobs.</b> <see cref="DeleteRepoDataAsync"/> and the rebuild's
    /// <c>ClearStreamTablesAsync</c> both walk this array, so a new stream table cannot be added to one
    /// and forgotten by the other — which is exactly how a removed repository would keep contributing
    /// rows to every figure (REQ-FN-074, BRD-115). The three miss tables (2026-08-28) are here for that
    /// reason and a guardrail test asserts it.
    /// </remarks>
    private static readonly string[] StreamTables =
        ["Run", "Gate", "Session", "Commit", "Miss", "MissFix", "MissAmend", "PbEvent"];

    /// <summary>
    /// The three schema-2 Playbook phase tables, purged with the stream tables when a repository is
    /// removed (REQ-FN-095, ADR-025).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purged, but deliberately not truncated by the rebuild</b> — which is why they are a second
    /// list rather than three more entries in <see cref="StreamTables"/>. A rebuild empties a table and
    /// then replays the raw JSONL archive into it, and that is safe precisely because the archive is the
    /// source of truth. Phase-metric rows do not arrive that way: they come through the import path
    /// (ADR-023), so truncating them on a rebuild would delete data with nothing to replay it from.
    /// </para>
    /// <para>
    /// <see cref="DeleteRepoDataAsync"/> walks both lists, because there the rule is the opposite and
    /// absolute: missing one table leaves orphaned rows that reappear in every figure for a repository
    /// the owner believes they removed — the same failure the miss tables were added here to avoid
    /// (BRD-115).
    /// </para>
    /// </remarks>
    private static readonly string[] PhaseTables =
        ["PbPhaseExecution", "PbPhaseModelUsage", "PbPhaseSubagent"];

    /// <summary>Resolved once — the schema script does not move while the process runs.</summary>
    private static string? SchemaPathCache;

    /// <summary>
    /// Registers the Dapper type handlers the column set needs, once per process.
    /// </summary>
    /// <remarks>
    /// <c>"Run"."ModelTokensOut"</c> is a <c>jsonb</c> map rather than a scalar, and Dapper cannot read
    /// one without being told how (<see cref="JsonMapTypeHandler"/>). Registering from the store's type
    /// initializer puts it on the one path every database call already passes through.
    /// </remarks>
    static PostgresStore() => JsonMapTypeHandler.Register();

    private readonly TfLensOptions objOptions;
    private readonly IStreamParser objParser;
    private readonly ILogger<PostgresStore> objLogger;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="aOptions">Configuration, supplying <c>TfLensDbConnection</c> and <c>DataRoot</c>.</param>
    /// <param name="aParser">The stream parser a rebuild replays the raw archive through.</param>
    /// <param name="aLogger">Logger; IDs, counts and status only — never a record body (Coding Standards §Logging).</param>
    public PostgresStore(
        IOptions<TfLensOptions> aOptions,
        IStreamParser aParser,
        ILogger<PostgresStore> aLogger)
    {
        ArgumentNullException.ThrowIfNull(aOptions);
        objOptions = aOptions.Value;
        objParser = aParser;
        objLogger = aLogger;
    }

    /// <inheritdoc />
    public async Task EnsureSchemaAsync(CancellationToken aCancellationToken = default)
    {
        var vScript = await File.ReadAllTextAsync(ResolveSchemaPath(), aCancellationToken).ConfigureAwait(false);
        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        await vConnection.ExecuteAsync(new CommandDefinition(vScript, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> PingAsync(CancellationToken aCancellationToken = default)
    {
        try
        {
            await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
            var vAnswer = await vConnection
                .ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: aCancellationToken))
                .ConfigureAwait(false);
            return vAnswer == 1;
        }
        catch (NpgsqlException vEx)
        {
            objLogger.LogWarning(vEx, "Database ping failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> UpsertAsync(ParseResult aParsed, CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aParsed);

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);

        var vWritten = 0;
        vWritten += await ExecuteBatchAsync(vConnection, InsertRunSql, aParsed.Runs, aCancellationToken)
            .ConfigureAwait(false);
        vWritten += await ExecuteBatchAsync(vConnection, InsertGateSql, aParsed.Gates, aCancellationToken)
            .ConfigureAwait(false);
        vWritten += await ExecuteBatchAsync(vConnection, InsertSessionSql, aParsed.Sessions, aCancellationToken)
            .ConfigureAwait(false);
        vWritten += await ExecuteBatchAsync(vConnection, InsertCommitSql, aParsed.Commits, aCancellationToken)
            .ConfigureAwait(false);
        // The misses stream is one file and three tables (ADR-018): the parser has already split the
        // records by their own `kind`, so this is three ordinary idempotent batches, not a discriminator.
        vWritten += await ExecuteBatchAsync(vConnection, InsertMissSql, aParsed.Misses, aCancellationToken)
            .ConfigureAwait(false);
        vWritten += await ExecuteBatchAsync(vConnection, InsertMissFixSql, aParsed.MissFixes, aCancellationToken)
            .ConfigureAwait(false);
        vWritten += await ExecuteBatchAsync(vConnection, InsertMissAmendSql, aParsed.MissAmends, aCancellationToken)
            .ConfigureAwait(false);
        // Playbook events split by record kind: a turn is a cumulative snapshot keyed on its messageID
        // and must overwrite a smaller one, a marker is keyed on kind+ts+session and never changes.
        // The two land on different partial unique indexes, so they cannot share one ON CONFLICT clause
        // (DECISIONS.md D-011).
        vWritten += await ExecuteBatchAsync(
            vConnection,
            InsertPbEventTurnSql,
            aParsed.PbEvents.Where(aE => !string.IsNullOrEmpty(aE.MessageId)).ToList(),
            aCancellationToken).ConfigureAwait(false);
        vWritten += await ExecuteBatchAsync(
            vConnection,
            InsertPbEventMarkerSql,
            aParsed.PbEvents.Where(aE => string.IsNullOrEmpty(aE.MessageId)).ToList(),
            aCancellationToken).ConfigureAwait(false);
        // One phase-metric line, three tables (ADR-025). The parser has already split it, so these are
        // three ordinary upserts keyed on (UserId, Repo, PhaseExecutionId) — re-import is the NORMAL
        // case here, because the exporter re-emits every currently readable window (REQ-FN-094).
        vWritten += await ExecuteBatchAsync(
            vConnection, InsertPhaseExecutionSql, aParsed.PhaseExecutions, aCancellationToken)
            .ConfigureAwait(false);
        vWritten += await ExecuteBatchAsync(
            vConnection, InsertPhaseModelUsageSql, aParsed.PhaseModelUsages, aCancellationToken)
            .ConfigureAwait(false);
        vWritten += await ExecuteBatchAsync(
            vConnection, InsertPhaseSubagentSql, aParsed.PhaseSubagents, aCancellationToken)
            .ConfigureAwait(false);

        return vWritten;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PbPhaseExecutionRecord>> ReadPhaseExecutionsAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default)
    {
        const string vSql = """
            SELECT s.* FROM "PbPhaseExecution" s
            WHERE s."UserId" = @aUserId AND (@aRepo IS NULL OR s."Repo" = @aRepo)
            ORDER BY s."StartedAt", s."PhaseExecutionId"
            """;

        return await ReadPhaseAsync<PbPhaseExecutionRecord>(vSql, aUserId, aRepo, aCancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PbPhaseModelUsageRecord>> ReadPhaseModelUsagesAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default)
    {
        const string vSql = """
            SELECT s.* FROM "PbPhaseModelUsage" s
            WHERE s."UserId" = @aUserId AND (@aRepo IS NULL OR s."Repo" = @aRepo)
            ORDER BY s."PhaseExecutionId", s."Model"
            """;

        return await ReadPhaseAsync<PbPhaseModelUsageRecord>(vSql, aUserId, aRepo, aCancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PbPhaseSubagentRecord>> ReadPhaseSubagentsAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default)
    {
        const string vSql = """
            SELECT s.* FROM "PbPhaseSubagent" s
            WHERE s."UserId" = @aUserId AND (@aRepo IS NULL OR s."Repo" = @aRepo)
            ORDER BY s."PhaseExecutionId", s."SessionId"
            """;

        return await ReadPhaseAsync<PbPhaseSubagentRecord>(vSql, aUserId, aRepo, aCancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one phase-table read for a user, optionally narrowed to one repository.
    /// </summary>
    /// <remarks>
    /// No framework join, unlike <see cref="ReadStreamAsync{T}"/>: these three tables exist only on the
    /// Playbook axis, so a join to <c>"UserRepo"."Framework"</c> could only ever remove rows the caller
    /// already asked for by repository.
    /// </remarks>
    /// <typeparam name="T">The record type the table maps to.</typeparam>
    /// <param name="aSql">The query.</param>
    /// <param name="aUserId">The AppManager user id — mandatory (ADR-013).</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The matching rows.</returns>
    private async Task<IReadOnlyList<T>> ReadPhaseAsync<T>(
        string aSql, int aUserId, string? aRepo, CancellationToken aCancellationToken)
    {
        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);

        var vRows = await vConnection.QueryAsync<T>(
            new CommandDefinition(aSql, new { aUserId, aRepo }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);

        return vRows.ToList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunRecord>> ReadRunsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        ReadStreamAsync<RunRecord>("Run", aUserId, aFramework, aRepo, aCancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<GateRecord>> ReadGatesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        ReadStreamAsync<GateRecord>("Gate", aUserId, aFramework, aRepo, aCancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> ReadSessionsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        ReadStreamAsync<SessionRecord>("Session", aUserId, aFramework, aRepo, aCancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<CommitRecord>> ReadCommitsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        ReadStreamAsync<CommitRecord>("Commit", aUserId, aFramework, aRepo, aCancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MissRecord>> ReadMissesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        ReadStreamAsync<MissRecord>("Miss", aUserId, aFramework, aRepo, aCancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MissFixRecord>> ReadMissFixesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        ReadStreamAsync<MissFixRecord>("MissFix", aUserId, aFramework, aRepo, aCancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MissAmendRecord>> ReadMissAmendsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        ReadStreamAsync<MissAmendRecord>("MissAmend", aUserId, aFramework, aRepo, aCancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PbEventRecord>> ReadPbEventsAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default)
    {
        const string vSql = """
            SELECT s.* FROM "PbEvent" s
            WHERE s."UserId" = @aUserId AND (@aRepo IS NULL OR s."Repo" = @aRepo)
            ORDER BY s."Ts"
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        var vRows = await vConnection.QueryAsync<PbEventRecord>(
            new CommandDefinition(vSql, new { aUserId, aRepo }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
        return vRows.ToList();
    }

    /// <inheritdoc />
    public async Task RecordSourceProvenanceAsync(
        SourceProvenanceRecord aRecord, CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aRecord);
        ProvenanceRules.RequireObtained(aRecord.UserId, aRecord.Repo, aRecord.SourceSha);

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        await vConnection.ExecuteAsync(new CommandDefinition(
            InsertProvenanceSql,
            new
            {
                aRecord.UserId,
                aRecord.Repo,
                aRecord.SourceSha,
                aRecord.Kind,
                aRecord.ObtainedTs
            },
            cancellationToken: aCancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProvenanceAuditReport> AuditProvenanceAsync(
        int? aUserId = null, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);

        var vStored = (await vConnection.QueryAsync<StoredProvenance>(new CommandDefinition(
            StoredProvenanceSql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false)).ToList();

        var vObtained = (await vConnection.QueryAsync<SourceProvenanceRecord>(new CommandDefinition(
            ObtainedProvenanceSql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false)).ToList();

        // BRD-19 — the raw archive is the app's own record of what a sync fetched, written by the sync
        // path before the parse. It is a weaker attestation than the ledger (a file-system write can
        // forge a name, which is exactly what happened on 2026-08-29 alongside the seeded rows), but it
        // is the only oracle that covers a SHA an EARLIER sync obtained: "SyncState" keeps just the
        // newest, and user 2's four repositories legitimately hold rows on eight different SHAs.
        // Dropping it would report six real datasets as fabricated, and a check that cries wolf is a
        // check nobody runs.
        foreach (var vArchive in EnumerateArchive(aUserId))
        {
            vObtained.Add(new SourceProvenanceRecord(
                vArchive.UserId, vArchive.Repo, vArchive.Sha, ProvenanceKinds.Archive, string.Empty));
        }

        var vReport = ProvenanceAudit.Compare(vStored, vObtained);

        if (vReport.HasOrphans)
        {
            objLogger.LogWarning(
                "Provenance audit found {Sources} unaccounted source SHA(s) over {Rows} row(s)",
                vReport.Orphans.Count,
                vReport.OrphanRows);
        }

        return vReport;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncState>> ReadSyncStateAsync(
        int aUserId, CancellationToken aCancellationToken = default)
    {
        const string vSql = """
            SELECT * FROM "SyncState" WHERE "UserId" = @aUserId ORDER BY "Repo"
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        var vRows = await vConnection.QueryAsync<SyncState>(
            new CommandDefinition(vSql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
        return vRows.ToList();
    }

    /// <inheritdoc />
    public async Task WriteSyncStateAsync(SyncState aState, CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aState);

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        await vConnection.ExecuteAsync(
            new CommandDefinition(UpsertSyncStateSql, aState, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRepo>> ReadUserReposAsync(
        int aUserId, CancellationToken aCancellationToken = default)
    {
        const string vSql = """
            SELECT * FROM "UserRepo" WHERE "UserId" = @aUserId ORDER BY "Repo"
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        var vRows = await vConnection.QueryAsync<UserRepo>(
            new CommandDefinition(vSql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
        return vRows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRepo>> ReadAllUserReposAsync(CancellationToken aCancellationToken = default)
    {
        const string vSql = """
            SELECT * FROM "UserRepo" ORDER BY "UserId", "Repo"
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        var vRows = await vConnection.QueryAsync<UserRepo>(
            new CommandDefinition(vSql, cancellationToken: aCancellationToken)).ConfigureAwait(false);
        return vRows.ToList();
    }

    /// <inheritdoc />
    public async Task WriteUserRepoAsync(UserRepo aRepo, CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aRepo);

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        await vConnection.ExecuteAsync(
            new CommandDefinition(UpsertUserRepoSql, aRepo, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteRepoDataAsync(
        int aUserId, string aRepo, CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aRepo);

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);

        // Both lists, and all three phase tables among them (REQ-FN-095): a table this loop forgets is a
        // table whose rows survive the removal and go on contributing to every figure.
        foreach (var vTable in StreamTables.Concat(PhaseTables))
        {
            var vSql = $"""DELETE FROM "{vTable}" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""";
            await vConnection.ExecuteAsync(
                new CommandDefinition(vSql, new { aUserId, aRepo }, cancellationToken: aCancellationToken))
                .ConfigureAwait(false);
        }

        await vConnection.ExecuteAsync(new CommandDefinition(
            """DELETE FROM "SyncState" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""",
            new { aUserId, aRepo }, cancellationToken: aCancellationToken)).ConfigureAwait(false);

        await vConnection.ExecuteAsync(new CommandDefinition(
            """DELETE FROM "UserRepo" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""",
            new { aUserId, aRepo }, cancellationToken: aCancellationToken)).ConfigureAwait(false);

        objLogger.LogInformation("Purged stored data for user {UserId} repo {Repo}", aUserId, aRepo);
    }

    /// <inheritdoc />
    public async Task<CoverageFacts> ReadCoverageFactsAsync(
        int aUserId, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);

        var vStreams = await vConnection.QueryAsync<StreamCoverage>(
            new CommandDefinition(StreamCoverageSql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);

        var vOverflowKeys = await vConnection.QueryAsync<UnknownFieldFact>(
            new CommandDefinition(OverflowKeysSql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);

        var vVersions = await vConnection.QueryAsync<SchemaVersionFact>(
            new CommandDefinition(AboveSchemaV1Sql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);

        // REQ-UI-016: the Overflow column holds every property the table had no column for, which
        // includes documented ones such as a run's `inferred`. Only the names SCHEMA.md does not document
        // are undocumented, and only names — never values — leave this method.
        var vUnknown = vOverflowKeys
            .Where(aFact => !StreamParser.IsDocumented(StreamNames.ToKind(aFact.Stream), aFact.Field))
            .OrderBy(aFact => aFact.Repo, StringComparer.Ordinal)
            .ThenBy(aFact => aFact.Stream, StringComparer.Ordinal)
            .ThenBy(aFact => aFact.Field, StringComparer.Ordinal)
            .ToList();

        return new CoverageFacts(vStreams.ToList(), vUnknown, vVersions.ToList());
    }

    /// <inheritdoc />
    public async Task<RebuildReport> RebuildAsync(
        int? aUserId = null, CancellationToken aCancellationToken = default)
    {
        var vStarted = DateTimeOffset.UtcNow;

        await ClearStreamTablesAsync(aUserId, aCancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(aCancellationToken).ConfigureAwait(false);

        var vFiles = 0;
        var vRecords = 0;
        var vDuplicates = 0;
        var vInvalid = 0;

        // REQ-FN-063: session records the replay presented to the store, per repository, counted before
        // any dedupe. What the store actually kept is COUNT(*) afterwards, and the difference is exactly
        // what ingest collapsed — including the cross-file duplicates that no single parse can see.
        var vSessionsPresented = new Dictionary<RepoKey, int>();

        foreach (var vArchive in EnumerateArchive(aUserId))
        {
            var vText = await File.ReadAllTextAsync(vArchive.Path, aCancellationToken).ConfigureAwait(false);
            var vParsed = objParser.Parse(vArchive.UserId, vArchive.Repo, vArchive.Sha, vArchive.Stream, vText);

            vFiles++;
            vDuplicates += vParsed.DuplicatesCollapsed;
            vInvalid += vParsed.InvalidLines;

            var vKey = new RepoKey(vArchive.UserId, vArchive.Repo);
            vSessionsPresented[vKey] = vSessionsPresented.GetValueOrDefault(vKey) + vParsed.SessionsPresented;

            vRecords += await UpsertAsync(vParsed, aCancellationToken).ConfigureAwait(false);
        }

        await RecomputeSyncCountsAsync(aUserId, aCancellationToken).ConfigureAwait(false);
        await SetSessionCollapsesAsync(aUserId, vSessionsPresented, aCancellationToken).ConfigureAwait(false);

        objLogger.LogInformation(
            "Rebuild replayed {Files} raw files for user {UserId}, writing {Records} rows",
            vFiles, aUserId, vRecords);

        return new RebuildReport(
            vFiles,
            vRecords,
            vDuplicates,
            vInvalid,
            vStarted.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    /// <summary>
    /// Empties the stream tables ahead of a replay, scoped to one user when one was given.
    /// </summary>
    /// <param name="aUserId">One user, or <c>null</c> for every user.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the tables are empty.</returns>
    private async Task ClearStreamTablesAsync(int? aUserId, CancellationToken aCancellationToken)
    {
        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        foreach (var vTable in StreamTables)
        {
            if (aUserId is null)
            {
                await vConnection.ExecuteAsync(new CommandDefinition(
                    $"""TRUNCATE TABLE "{vTable}" """, cancellationToken: aCancellationToken)).ConfigureAwait(false);
                continue;
            }

            await vConnection.ExecuteAsync(new CommandDefinition(
                $"""DELETE FROM "{vTable}" WHERE "UserId" = @aUserId""",
                new { aUserId = aUserId.Value }, cancellationToken: aCancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Recomputes every <c>"SyncState"</c> per-stream count from the rebuilt tables (REQ-FN-028).
    /// </summary>
    /// <param name="aUserId">One user, or <c>null</c> for every user.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the counts match the rebuilt rows.</returns>
    private async Task RecomputeSyncCountsAsync(int? aUserId, CancellationToken aCancellationToken)
    {
        const string vSql = """
            UPDATE "SyncState" AS t SET
                "RunsCount"     = (SELECT COUNT(*) FROM "Run"     r WHERE r."UserId" = t."UserId" AND r."Repo" = t."Repo"),
                "GatesCount"    = (SELECT COUNT(*) FROM "Gate"    g WHERE g."UserId" = t."UserId" AND g."Repo" = t."Repo"),
                "SessionsCount" = (SELECT COUNT(*) FROM "Session" s WHERE s."UserId" = t."UserId" AND s."Repo" = t."Repo"),
                "CommitsCount"  = (SELECT COUNT(*) FROM "Commit"  c WHERE c."UserId" = t."UserId" AND c."Repo" = t."Repo"),
                "EventsCount"   = (SELECT COUNT(*) FROM "PbEvent" p WHERE p."UserId" = t."UserId" AND p."Repo" = t."Repo"),
                -- One stream, three tables: the misses count is their sum, so Coverage reports five
                -- stream rows per repository rather than seven (REQ-FN-071).
                "MissesCount"   = (SELECT COUNT(*) FROM "Miss"      m WHERE m."UserId" = t."UserId" AND m."Repo" = t."Repo")
                                + (SELECT COUNT(*) FROM "MissFix"   f WHERE f."UserId" = t."UserId" AND f."Repo" = t."Repo")
                                + (SELECT COUNT(*) FROM "MissAmend" a WHERE a."UserId" = t."UserId" AND a."Repo" = t."Repo")
            WHERE @aUserId IS NULL OR t."UserId" = @aUserId
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        await vConnection.ExecuteAsync(
            new CommandDefinition(vSql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sets <c>"SessionDuplicatesCollapsed"</c> from a completed replay (REQ-FN-063).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Set, never add.</b> A rebuild replays the entire raw archive for the scope it was given, so
    /// what it measured is the whole truth for that scope and adding would double the figure on the
    /// second run. Only an incremental sync, which sees one pass' worth of new files, adds — and it does
    /// that by reading the stored row and writing the sum, not here.
    /// </para>
    /// <para>
    /// The figure is <c>presented - stored</c>: how many session records the replay handed the store
    /// minus how many rows survived. That is the only formulation that catches a session id repeated
    /// across two archived snapshots, which no single parse can see and which
    /// <c>UcSessionUserRepoId</c> collapses silently. Every row in scope is zeroed first, so a
    /// repository whose sessions have gone from the archive does not keep a stale count, and the result
    /// is floored at zero so a hand-edited archive can never produce a negative one.
    /// </para>
    /// </remarks>
    /// <param name="aUserId">One user, or <c>null</c> for every user — the scope the replay covered.</param>
    /// <param name="aPresented">Session records presented per repository during the replay.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the collapse counts describe the replay.</returns>
    private async Task SetSessionCollapsesAsync(
        int? aUserId,
        IReadOnlyDictionary<RepoKey, int> aPresented,
        CancellationToken aCancellationToken)
    {
        const string vResetSql = """
            UPDATE "SyncState" SET "SessionDuplicatesCollapsed" = 0
            WHERE @aUserId IS NULL OR "UserId" = @aUserId
            """;

        const string vSetSql = """
            UPDATE "SyncState" AS t SET "SessionDuplicatesCollapsed" = GREATEST(
                0,
                @Presented - (SELECT COUNT(*) FROM "Session" s
                              WHERE s."UserId" = t."UserId" AND s."Repo" = t."Repo"))
            WHERE t."UserId" = @UserId AND t."Repo" = @Repo
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);

        await vConnection.ExecuteAsync(
            new CommandDefinition(vResetSql, new { aUserId }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);

        var vTallies = aPresented
            .Where(aEntry => aEntry.Value > 0)
            .Select(aEntry => new SessionTally(aEntry.Key.UserId, aEntry.Key.Repo, aEntry.Value))
            .ToList();

        if (vTallies.Count == 0)
        {
            return;
        }

        await vConnection.ExecuteAsync(
            new CommandDefinition(vSetSql, vTallies, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Enumerates the raw archive in <c>(user, repo, SHA fetch order)</c> — the rebuild's only source.
    /// </summary>
    /// <param name="aUserId">One user, or <c>null</c> for every user.</param>
    /// <returns>One entry per replayable archive file, in replay order.</returns>
    /// <remarks>
    /// The fetcher names each file <c>{stream}-{sha}.jsonl</c> (REQ-FN-027), so the stream and the SHA
    /// come out of the name. Fetch order within a repository is the file's last-write time — the archive
    /// carries no other ordering, and a later fetch is always written later.
    /// </remarks>
    private IEnumerable<ArchiveFile> EnumerateArchive(int? aUserId)
    {
        var vRawRoot = Path.Combine(objOptions.DataRoot, "raw");
        if (!Directory.Exists(vRawRoot))
        {
            yield break;
        }

        IEnumerable<string> vUserDirs = aUserId is null
            ? Directory.EnumerateDirectories(vRawRoot).OrderBy(aD => aD, StringComparer.Ordinal)
            : new[] { Path.Combine(vRawRoot, aUserId.Value.ToString()) };

        foreach (var vUserDir in vUserDirs)
        {
            if (!Directory.Exists(vUserDir) || !int.TryParse(Path.GetFileName(vUserDir), out var vUserId))
            {
                continue;
            }

            var vRepoDirs = Directory.EnumerateDirectories(vUserDir).OrderBy(aD => aD, StringComparer.Ordinal);
            foreach (var vRepoDir in vRepoDirs)
            {
                var vRepo = Path.GetFileName(vRepoDir).Replace("__", "/", StringComparison.Ordinal);
                var vFiles = Directory.EnumerateFiles(vRepoDir, "*.jsonl")
                    .Select(aF => new FileInfo(aF))
                    .OrderBy(aF => aF.LastWriteTimeUtc)
                    .ThenBy(aF => aF.Name, StringComparer.Ordinal);

                foreach (var vFile in vFiles)
                {
                    if (TryDescribe(vFile, vUserId, vRepo, out var vArchive))
                    {
                        yield return vArchive;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reads the stream and SHA out of an archive file name.
    /// </summary>
    /// <param name="aFile">The archive file.</param>
    /// <param name="aUserId">The user the archive belongs to.</param>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <param name="aArchive">The described file when the name is recognised.</param>
    /// <returns><c>true</c> when the name is <c>{stream}-{sha}.jsonl</c> for a known stream.</returns>
    private static bool TryDescribe(FileInfo aFile, int aUserId, string aRepo, out ArchiveFile aArchive)
    {
        aArchive = default;
        var vName = Path.GetFileNameWithoutExtension(aFile.Name);
        var vDash = vName.IndexOf('-', StringComparison.Ordinal);
        if (vDash <= 0)
        {
            return false;
        }

        var vStreamName = vName[..vDash];
        if (!StreamNames.TechieFlow.Contains(vStreamName) && !StreamNames.Playbook.Contains(vStreamName))
        {
            return false;
        }

        aArchive = new ArchiveFile(
            aUserId, aRepo, vName[(vDash + 1)..], StreamNames.ToKind(vStreamName), aFile.FullName);
        return true;
    }

    /// <inheritdoc />
    public async Task<DailySeries> ReadDailySeriesAsync(
        int aUserId,
        string aFramework,
        StreamKind aStream,
        int aDays = 14,
        bool aFailuresOnly = false,
        CancellationToken aCancellationToken = default)
    {
        var vTable = aStream switch
        {
            StreamKind.Runs => "Run",
            StreamKind.Gates => "Gate",
            StreamKind.Sessions => "Session",
            StreamKind.Commits => "Commit",
            // The daily series counts misses OPENED per day; fixes and amendments are lifecycle events
            // on an existing miss and would double-count a defect if they joined the same line.
            StreamKind.Misses => "Miss",
            StreamKind.Events => "PbEvent",
            _ => throw new ArgumentOutOfRangeException(nameof(aStream), aStream, "Unknown stream.")
        };

        if (aFailuresOnly && aStream != StreamKind.Gates)
        {
            throw new ArgumentException("Only the gates stream carries a verdict.", nameof(aFailuresOnly));
        }

        // The window is generated rather than derived from the rows, so a day with no records appears
        // as a zero instead of being closed up — a quiet week should look quiet, not like a straight
        // line between the days either side of it. "Ts" is the record's own timestamp, so the series
        // describes when the work happened rather than when TfLens happened to sync it.
        var vFailureFilter = aFailuresOnly
            ? """AND COALESCE(s."Verdict", '') NOT IN ('Verified', 'Done (pre-existing)')"""
            : string.Empty;

        var vSql = $"""
            WITH days AS (
                SELECT generate_series(
                    (CURRENT_DATE - MAKE_INTERVAL(days => @aDays - 1)),
                    CURRENT_DATE,
                    INTERVAL '1 day')::date AS "Day"
            ),
            hits AS (
                SELECT LEFT(s."Ts", 10)::date AS "Day", COUNT(*)::int AS "Count"
                FROM "{vTable}" s
                INNER JOIN "UserRepo" u ON u."UserId" = s."UserId" AND u."Repo" = s."Repo"
                WHERE s."UserId" = @aUserId
                  AND u."Framework" = @aFramework
                  AND s."Ts" LIKE '____-__-__%'
                  AND LEFT(s."Ts", 10)::date > (CURRENT_DATE - MAKE_INTERVAL(days => @aDays))
                  {vFailureFilter}
                GROUP BY 1
            )
            SELECT d."Day", COALESCE(h."Count", 0) AS "Count"
            FROM days d LEFT JOIN hits h ON h."Day" = d."Day"
            ORDER BY d."Day"
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);

        var vRows = await vConnection.QueryAsync<DailyCount>(
            new CommandDefinition(
                vSql,
                new { aUserId, aFramework, aDays },
                cancellationToken: aCancellationToken)).ConfigureAwait(false);

        var vPoints = vRows.ToList();

        // A window of pure zeros is not history, it is absence — say nothing rather than draw a flat line.
        if (vPoints.Count == 0 || vPoints.All(aP => aP.Count == 0))
        {
            return DailySeries.Empty;
        }

        // The wire names are plural ("gates", "runs"), which reads badly as a noun phrase — the label is
        // shown to a person, so it says "gate records", not "gates records".
        var vNoun = aStream switch
        {
            StreamKind.Runs => "run",
            StreamKind.Gates => "gate",
            StreamKind.Sessions => "session",
            StreamKind.Commits => "commit",
            StreamKind.Misses => "miss",
            StreamKind.Events => "event",
            _ => StreamNames.ToName(aStream)
        };

        var vWhat = aFailuresOnly ? "failure" : vNoun;

        return new DailySeries(vPoints, $"{vWhat} records per day, last {aDays} days");
    }

    /// <summary>
    /// Reads one stream table for a user, narrowed to a framework and optionally to one repository.
    /// </summary>
    /// <typeparam name="T">The record type the table maps to.</typeparam>
    /// <param name="aTable">The quoted table name.</param>
    /// <param name="aUserId">The AppManager user id — mandatory (ADR-013).</param>
    /// <param name="aFramework">The provenance axis, matched against <c>"UserRepo"."Framework"</c> (ADR-016).</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The matching records, ordered by timestamp.</returns>
    private async Task<IReadOnlyList<T>> ReadStreamAsync<T>(
        string aTable, int aUserId, string aFramework, string? aRepo, CancellationToken aCancellationToken)
    {
        var vSql = $"""
            SELECT s.* FROM "{aTable}" s
            INNER JOIN "UserRepo" u ON u."UserId" = s."UserId" AND u."Repo" = s."Repo"
            WHERE s."UserId" = @aUserId
              AND u."Framework" = @aFramework
              AND (@aRepo IS NULL OR s."Repo" = @aRepo)
            ORDER BY s."Ts"
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        var vRows = await vConnection.QueryAsync<T>(
            new CommandDefinition(vSql, new { aUserId, aFramework, aRepo }, cancellationToken: aCancellationToken))
            .ConfigureAwait(false);
        return vRows.ToList();
    }

    /// <summary>
    /// Runs one insert statement over a batch, returning how many rows the database actually wrote.
    /// </summary>
    /// <typeparam name="T">The record type being written.</typeparam>
    /// <param name="aConnection">The open connection.</param>
    /// <param name="aSql">The <c>INSERT … ON CONFLICT DO NOTHING</c> statement.</param>
    /// <param name="aRecords">The records to write.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>Rows written; a record already present contributes zero.</returns>
    private static async Task<int> ExecuteBatchAsync<T>(
        NpgsqlConnection aConnection, string aSql, IReadOnlyList<T> aRecords, CancellationToken aCancellationToken)
    {
        if (aRecords.Count == 0)
        {
            return 0;
        }

        return await aConnection.ExecuteAsync(
            new CommandDefinition(aSql, aRecords, cancellationToken: aCancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a pooled connection to the configured database.
    /// </summary>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>An open connection the caller disposes.</returns>
    /// <exception cref="InvalidOperationException"><c>TfLensDbConnection</c> is not configured.</exception>
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken aCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objOptions.DbConnection))
        {
            throw new InvalidOperationException(
                "TfLens cannot reach the store — TfLensDbConnection is not configured.");
        }

        var vConnection = new NpgsqlConnection(objOptions.DbConnection);
        await vConnection.OpenAsync(aCancellationToken).ConfigureAwait(false);
        return vConnection;
    }

    /// <summary>
    /// Finds <c>database/001-schema.sql</c> beside the binary (as the Dockerfile places it) or above it.
    /// </summary>
    /// <returns>The absolute path of the schema script.</returns>
    /// <exception cref="FileNotFoundException">The script is not beside the binary nor at any ancestor.</exception>
    private static string ResolveSchemaPath()
    {
        if (SchemaPathCache is not null)
        {
            return SchemaPathCache;
        }

        foreach (var vRoot in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var vDirectory = new DirectoryInfo(vRoot);
            while (vDirectory is not null)
            {
                var vCandidate = Path.Combine(vDirectory.FullName, "database", "001-schema.sql");
                if (File.Exists(vCandidate))
                {
                    SchemaPathCache = vCandidate;
                    return vCandidate;
                }

                vDirectory = vDirectory.Parent;
            }
        }

        throw new FileNotFoundException(
            "database/001-schema.sql was not found beside the binary or at any ancestor directory.");
    }

    /// <summary>One replayable file in the raw archive.</summary>
    /// <param name="UserId">The user the archive belongs to.</param>
    /// <param name="Repo"><c>owner/name</c> of the repository.</param>
    /// <param name="Sha">The SHA the file was fetched at, read from the file name.</param>
    /// <param name="Stream">Which stream the file holds.</param>
    /// <param name="Path">Absolute path of the file.</param>
    private readonly record struct ArchiveFile(int UserId, string Repo, string Sha, StreamKind Stream, string Path);

    /// <summary>Identifies one user's one repository while a replay tallies it.</summary>
    /// <param name="UserId">The user the archive belongs to.</param>
    /// <param name="Repo"><c>owner/name</c> of the repository.</param>
    private readonly record struct RepoKey(int UserId, string Repo);

    /// <summary>One repository's replayed session tally, as the collapse update reads it.</summary>
    /// <param name="UserId">The user the archive belongs to.</param>
    /// <param name="Repo"><c>owner/name</c> of the repository.</param>
    /// <param name="Presented">Session records the replay handed the store, before any dedupe.</param>
    private sealed record SessionTally(int UserId, string Repo, int Presented);

    /// <summary>
    /// Per-repository, per-stream row counts, backfilled counts and newest timestamp (REQ-UI-014).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>"Ts"</c> is stored as ISO-8601 text, whose lexical order is its chronological order, so
    /// <c>MAX</c> over the column is the newest record without a cast. <c>"Session"</c>, <c>"Commit"</c>
    /// and <c>"PbEvent"</c> carry no <c>"Backfilled"</c> column — SCHEMA.md does not put the flag on
    /// those streams — so they report zero rather than pretending the fact was captured.
    /// </para>
    /// <para>
    /// <c>misses</c> reports as <b>one</b> row per repository over its three tables: the stream is one
    /// file, so the Coverage stream table gains one row rather than three (BRD-127, REQ-FN-071).
    /// </para>
    /// </remarks>
    private const string StreamCoverageSql = """
        SELECT "Repo", 'runs' AS "Stream", COUNT(*)::int AS "Records",
               COUNT(*) FILTER (WHERE "Backfilled")::int AS "Backfilled", MAX("Ts") AS "NewestTs"
        FROM "Run" WHERE "UserId" = @aUserId GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'gates', COUNT(*)::int,
               COUNT(*) FILTER (WHERE "Backfilled")::int, MAX("Ts")
        FROM "Gate" WHERE "UserId" = @aUserId GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'sessions', COUNT(*)::int, 0, MAX("Ts")
        FROM "Session" WHERE "UserId" = @aUserId GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'commits', COUNT(*)::int, 0, MAX("Ts")
        FROM "Commit" WHERE "UserId" = @aUserId GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'misses', COUNT(*)::int,
               COUNT(*) FILTER (WHERE "Backfilled")::int, MAX("Ts")
        FROM (
            SELECT "Repo", "Ts", "Backfilled" FROM "Miss"      WHERE "UserId" = @aUserId
            UNION ALL
            SELECT "Repo", "Ts", "Backfilled" FROM "MissFix"   WHERE "UserId" = @aUserId
            UNION ALL
            SELECT "Repo", "Ts", "Backfilled" FROM "MissAmend" WHERE "UserId" = @aUserId
        ) misses GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'events', COUNT(*)::int, 0, MAX("Ts")
        FROM "PbEvent" WHERE "UserId" = @aUserId GROUP BY "Repo"
        """;

    /// <summary>
    /// The distinct keys of every stored <c>"Overflow"</c> payload, per repository and stream (REQ-UI-016).
    /// </summary>
    /// <remarks>
    /// <c>jsonb_object_keys</c> returns the key names and nothing else, so no field value can leave the
    /// database through this query even before the documented names are filtered out in C#.
    /// </remarks>
    private const string OverflowKeysSql = """
        SELECT "Repo", 'runs' AS "Stream", k AS "Field", COUNT(*)::int AS "Records"
        FROM "Run", LATERAL jsonb_object_keys("Overflow") AS k
        WHERE "UserId" = @aUserId AND "Overflow" IS NOT NULL GROUP BY "Repo", k
        UNION ALL
        SELECT "Repo", 'gates', k, COUNT(*)::int
        FROM "Gate", LATERAL jsonb_object_keys("Overflow") AS k
        WHERE "UserId" = @aUserId AND "Overflow" IS NOT NULL GROUP BY "Repo", k
        UNION ALL
        SELECT "Repo", 'sessions', k, COUNT(*)::int
        FROM "Session", LATERAL jsonb_object_keys("Overflow") AS k
        WHERE "UserId" = @aUserId AND "Overflow" IS NOT NULL GROUP BY "Repo", k
        UNION ALL
        SELECT "Repo", 'commits', k, COUNT(*)::int
        FROM "Commit", LATERAL jsonb_object_keys("Overflow") AS k
        WHERE "UserId" = @aUserId AND "Overflow" IS NOT NULL GROUP BY "Repo", k
        UNION ALL
        -- The three miss tables report as one stream, so their keys are grouped together: a field
        -- observed on both a miss and a miss-fix is one undocumented field, not two (REQ-FN-072).
        SELECT "Repo", 'misses', k, COUNT(*)::int
        FROM (
            SELECT "Repo", "Overflow" FROM "Miss"      WHERE "UserId" = @aUserId AND "Overflow" IS NOT NULL
            UNION ALL
            SELECT "Repo", "Overflow" FROM "MissFix"   WHERE "UserId" = @aUserId AND "Overflow" IS NOT NULL
            UNION ALL
            SELECT "Repo", "Overflow" FROM "MissAmend" WHERE "UserId" = @aUserId AND "Overflow" IS NOT NULL
        ) misses, LATERAL jsonb_object_keys("Overflow") AS k
        GROUP BY "Repo", k
        UNION ALL
        SELECT "Repo", 'events', k, COUNT(*)::int
        FROM "PbEvent", LATERAL jsonb_object_keys("Overflow") AS k
        WHERE "UserId" = @aUserId AND "Overflow" IS NOT NULL GROUP BY "Repo", k
        """;

    /// <summary>
    /// Repositories and streams holding a record from a newer schema version (REQ-UI-016).
    /// </summary>
    /// <remarks>
    /// <c>"PbEvent"</c> has no <c>"V"</c> column: the Playbook stream carries no schema version on the
    /// wire, so it cannot contribute a <c>v &gt; 1</c> record and is deliberately absent here.
    /// </remarks>
    private const string AboveSchemaV1Sql = """
        SELECT "Repo", 'runs' AS "Stream", MAX("V")::int AS "MaxVersion", COUNT(*)::int AS "Records"
        FROM "Run" WHERE "UserId" = @aUserId AND "V" > 1 GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'gates', MAX("V")::int, COUNT(*)::int
        FROM "Gate" WHERE "UserId" = @aUserId AND "V" > 1 GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'sessions', MAX("V")::int, COUNT(*)::int
        FROM "Session" WHERE "UserId" = @aUserId AND "V" > 1 GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'commits', MAX("V")::int, COUNT(*)::int
        FROM "Commit" WHERE "UserId" = @aUserId AND "V" > 1 GROUP BY "Repo"
        UNION ALL
        SELECT "Repo", 'misses', MAX("V")::int, COUNT(*)::int
        FROM (
            SELECT "Repo", "V" FROM "Miss"      WHERE "UserId" = @aUserId AND "V" > 1
            UNION ALL
            SELECT "Repo", "V" FROM "MissFix"   WHERE "UserId" = @aUserId AND "V" > 1
            UNION ALL
            SELECT "Repo", "V" FROM "MissAmend" WHERE "UserId" = @aUserId AND "V" > 1
        ) misses GROUP BY "Repo"
        """;

    /// <summary>Idempotent insert for <c>runs</c>; conflicts on <c>UcRunIdentity</c> are no-ops.</summary>
    /// <remarks>
    /// The three SCHEMA §2.6 columns are written straight through as the parser read them, <c>null</c>
    /// included: nothing here coalesces an absent <c>"SubagentRuns"</c> to zero, so <c>RebuildAsync</c>
    /// re-derives the same nulls from the stream alone (REQ-FN-088, ADR-026).
    /// </remarks>
    private const string InsertRunSql = """
        INSERT INTO "Run" (
            "UserId","Repo","SourceSha","V","Ts","App","ProjectType","ProjectTypeInferred","Backfilled",
            "Harness","Cmd","Mode","Started","Ended","DurationS","ReqsTouched","ReqsCount","Subagents",
            "FilesWritten","BuildResult","Tier","TierModel","Model","Models","Routed","TokensIn","TokensOut",
            "TokensCacheRead","TokensCacheWrite","CostUsd","TokensScope","Attempt",
            "SubagentRuns","TokensOutSubagents","ModelTokensOut","Overflow")
        VALUES (
            @UserId,@Repo,@SourceSha,@V,@Ts,@App,@ProjectType,@ProjectTypeInferred,@Backfilled,
            @Harness,@Cmd,@Mode,@Started,@Ended,@DurationS,@ReqsTouched,@ReqsCount,@Subagents,
            @FilesWritten,@BuildResult,@Tier,@TierModel,@Model,@Models,@Routed,@TokensIn,@TokensOut,
            @TokensCacheRead,@TokensCacheWrite,@CostUsd,@TokensScope,@Attempt,
            @SubagentRuns,@TokensOutSubagents,CAST(@ModelTokensOut AS jsonb),CAST(@Overflow AS jsonb))
        ON CONFLICT DO NOTHING
        """;

    /// <summary>Idempotent insert for <c>gates</c>; conflicts on <c>UcGateIdentity</c> are no-ops.</summary>
    private const string InsertGateSql = """
        INSERT INTO "Gate" (
            "UserId","Repo","SourceSha","V","Ts","App","ProjectType","ProjectTypeInferred","Backfilled",
            "Inferred","Harness","RunId","ReqId","ReqClass","Attempt","Verdict","Gate","GatesRun",
            "FailureClass","PriorVerdict","Proof","Overflow")
        VALUES (
            @UserId,@Repo,@SourceSha,@V,@Ts,@App,@ProjectType,@ProjectTypeInferred,@Backfilled,
            @Inferred,@Harness,@RunId,@ReqId,@ReqClass,@Attempt,@Verdict,@Gate,@GatesRun,
            @FailureClass,@PriorVerdict,@Proof,CAST(@Overflow AS jsonb))
        ON CONFLICT DO NOTHING
        """;

    /// <summary>Idempotent insert for <c>sessions</c>; conflicts on <c>UcSessionUserRepoId</c> are no-ops.</summary>
    private const string InsertSessionSql = """
        INSERT INTO "Session" (
            "UserId","Repo","SourceSha","V","Ts","App","ProjectType","Harness","SessionId","Model",
            "DurationS","InputTokens","OutputTokens","CacheReadTokens","CacheCreationTokens","CostUsd","Overflow")
        VALUES (
            @UserId,@Repo,@SourceSha,@V,@Ts,@App,@ProjectType,@Harness,@SessionId,@Model,
            @DurationS,@InputTokens,@OutputTokens,@CacheReadTokens,@CacheCreationTokens,@CostUsd,
            CAST(@Overflow AS jsonb))
        ON CONFLICT DO NOTHING
        """;

    /// <summary>Idempotent insert for <c>commits</c>; conflicts on <c>UcCommitUserRepoSha</c> are no-ops.</summary>
    private const string InsertCommitSql = """
        INSERT INTO "Commit" (
            "UserId","Repo","SourceSha","V","Ts","App","ProjectType","Sha","Files","Insertions","Deletions",
            "SubjectPrefix","Branch","Overflow")
        VALUES (
            @UserId,@Repo,@SourceSha,@V,@Ts,@App,@ProjectType,@Sha,@Files,@Insertions,@Deletions,
            @SubjectPrefix,@Branch,CAST(@Overflow AS jsonb))
        ON CONFLICT DO NOTHING
        """;

    /// <summary>Idempotent insert for a <c>miss</c>; conflicts on <c>UcMissUserRepoMissId</c> are no-ops.</summary>
    /// <remarks>
    /// One table, two editions (ADR-024). <c>"ItemId"</c> and <c>"FoundPhaseGate"</c> are written beside
    /// <c>"ReqId"</c> and <c>"FoundGate"</c> rather than into them, and a TechieFlow row simply leaves
    /// all three of the new columns <c>null</c> (REQ-FN-104, REQ-FN-103). A Playbook row additionally
    /// conflicts on <c>UcMissUserRepoSourceLine</c>, which the <c>ON CONFLICT DO NOTHING</c> covers
    /// without naming — the clause is unqualified precisely so a second natural key on the same table
    /// does not need a second statement.
    /// </remarks>
    private const string InsertMissSql = """
        INSERT INTO "Miss" (
            "UserId","Repo","SourceSha","V","Ts","App","ProjectType","ProjectTypeInferred","Backfilled",
            "Harness","MissId","ReqId","ItemId","ReqClass","MissClass","Artifact","Severity","WhyMissed",
            "OriginPhase","OriginAgent","OriginRunId","OriginConfidence","OriginModel","OriginHarness",
            "FoundBy","FoundPhase","FoundGate","FoundPhaseGate","FoundRunId","FailureClass",
            "SourceLineHash","Overflow")
        VALUES (
            @UserId,@Repo,@SourceSha,@V,@Ts,@App,@ProjectType,@ProjectTypeInferred,@Backfilled,
            @Harness,@MissId,@ReqId,@ItemId,@ReqClass,@MissClass,@Artifact,@Severity,@WhyMissed,
            @OriginPhase,@OriginAgent,@OriginRunId,@OriginConfidence,@OriginModel,@OriginHarness,
            @FoundBy,@FoundPhase,@FoundGate,@FoundPhaseGate,@FoundRunId,@FailureClass,
            @SourceLineHash,CAST(@Overflow AS jsonb))
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// Idempotent insert for a <c>miss-fix</c>; conflicts on <c>UcMissFixUserRepoMissIdFixRunId</c> are no-ops.
    /// </summary>
    /// <remarks>
    /// <c>DO NOTHING</c> rather than keep-the-latest: within a file the parser already applied the
    /// latest-wins rule, and across files a fix record's token window is written once by the emitter when
    /// the run closes. Re-fetching an archived file therefore has nothing newer to offer.
    /// </remarks>
    private const string InsertMissFixSql = """
        INSERT INTO "MissFix" (
            "UserId","Repo","SourceSha","V","Ts","App","ProjectType","ProjectTypeInferred","Backfilled",
            "Harness","MissId","ReqId","FixRunId","FixCmd","FixAttempt","VerdictAfter","Reopened",
            "CostAttribution","TokensIn","TokensOut","TokensCacheRead","TokensCacheWrite","CostUsd",
            "TokensScope","Model","SourceLineHash","Overflow")
        VALUES (
            @UserId,@Repo,@SourceSha,@V,@Ts,@App,@ProjectType,@ProjectTypeInferred,@Backfilled,
            @Harness,@MissId,@ReqId,@FixRunId,@FixCmd,@FixAttempt,@VerdictAfter,@Reopened,
            @CostAttribution,@TokensIn,@TokensOut,@TokensCacheRead,@TokensCacheWrite,@CostUsd,
            @TokensScope,@Model,@SourceLineHash,CAST(@Overflow AS jsonb))
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// Idempotent insert for a <c>miss-amend</c>; conflicts on
    /// <c>UcMissAmendUserRepoMissIdFieldTs</c> are no-ops.
    /// </summary>
    /// <remarks>
    /// The row is stored exactly as written and nothing is folded here: the allowlist, the closed
    /// vocabulary and the never-overwrite rule are applied at read time, so a rebuild re-derives
    /// identical values whatever order the archived files arrived in (ADR-020, REQ-FN-075).
    /// </remarks>
    private const string InsertMissAmendSql = """
        INSERT INTO "MissAmend" (
            "UserId","Repo","SourceSha","V","Ts","App","ProjectType","ProjectTypeInferred","Backfilled",
            "Harness","MissId","Field","Value","SourceLineHash","Overflow")
        VALUES (
            @UserId,@Repo,@SourceSha,@V,@Ts,@App,@ProjectType,@ProjectTypeInferred,@Backfilled,
            @Harness,@MissId,@Field,@Value,@SourceLineHash,CAST(@Overflow AS jsonb))
        ON CONFLICT DO NOTHING
        """;

    /// <summary>The column list shared by both Playbook <c>events</c> inserts.</summary>
    private const string PbEventColumns = """
        "UserId","Repo","SourceSha","Ts","Kind","PhaseGate","Arguments","SessionId","ParentId",
        "MessageId","Model","TokensInput","TokensOutput","TokensReasoning","TokensCacheRead",
        "TokensCacheWrite","CostUsd","Overflow"
        """;

    /// <summary>The value list shared by both Playbook <c>events</c> inserts.</summary>
    private const string PbEventValues = """
        @UserId,@Repo,@SourceSha,@Ts,@Kind,@PhaseGate,@Arguments,@SessionId,@ParentId,
        @MessageId,@Model,@TokensInput,@TokensOutput,@TokensReasoning,@TokensCacheRead,
        @TokensCacheWrite,@CostUsd,CAST(@Overflow AS jsonb)
        """;

    /// <summary>
    /// Insert for a Playbook <c>turn</c> record, keyed on <c>UcPbEventTurn</c>.
    /// </summary>
    /// <remarks>
    /// The one stream insert that is <b>not</b> <c>DO NOTHING</c>. The Playbook emitter appends a fresh
    /// turn record on every <c>message.updated</c>, so a message fetched mid-stream is stored partial and
    /// a later sync must replace it with the larger snapshot — the same keep-the-largest rule as
    /// <c>sessions</c> (DECISIONS.md D-011). <c>DO NOTHING</c> here would freeze the partial counts.
    /// </remarks>
    private static readonly string InsertPbEventTurnSql = $"""
        INSERT INTO "PbEvent" ({PbEventColumns})
        VALUES ({PbEventValues})
        ON CONFLICT ("UserId","Repo","MessageId") WHERE "MessageId" IS NOT NULL DO UPDATE SET
            "Ts"               = EXCLUDED."Ts",
            "PhaseGate"        = EXCLUDED."PhaseGate",
            "Model"            = EXCLUDED."Model",
            "ParentId"         = COALESCE(EXCLUDED."ParentId", "PbEvent"."ParentId"),
            "TokensInput"      = EXCLUDED."TokensInput",
            "TokensOutput"     = EXCLUDED."TokensOutput",
            "TokensReasoning"  = EXCLUDED."TokensReasoning",
            "TokensCacheRead"  = EXCLUDED."TokensCacheRead",
            "TokensCacheWrite" = EXCLUDED."TokensCacheWrite",
            "CostUsd"          = EXCLUDED."CostUsd",
            "SourceSha"        = EXCLUDED."SourceSha",
            "Overflow"         = EXCLUDED."Overflow"
        WHERE COALESCE(EXCLUDED."TokensOutput", 0) + COALESCE(EXCLUDED."TokensReasoning", 0)
            > COALESCE("PbEvent"."TokensOutput", 0) + COALESCE("PbEvent"."TokensReasoning", 0)
           OR (COALESCE(EXCLUDED."TokensOutput", 0) + COALESCE(EXCLUDED."TokensReasoning", 0)
            = COALESCE("PbEvent"."TokensOutput", 0) + COALESCE("PbEvent"."TokensReasoning", 0)
            AND EXCLUDED."Ts" > "PbEvent"."Ts")
        """;

    /// <summary>
    /// Idempotent insert for a Playbook <c>phase-start</c> or <c>phase-end</c> record.
    /// </summary>
    /// <remarks>These carry no <c>messageID</c>, are not snapshots, and key on <c>UcPbEventMarker</c>.</remarks>
    private static readonly string InsertPbEventMarkerSql = $"""
        INSERT INTO "PbEvent" ({PbEventColumns})
        VALUES ({PbEventValues})
        ON CONFLICT DO NOTHING
        """;

    /// <summary>Every <c>"PbPhaseExecution"</c> column; the first three are its natural key.</summary>
    private static readonly string[] PhaseExecutionColumns =
    [
        "UserId", "Repo", "PhaseExecutionId", "SourceSchema", "SourceHarness", "Phase", "SessionId",
        "Granularity", "StartedAt", "EndedAt", "ElapsedMs", "Complete", "EndReason", "DominantModel",
        "Tier", "TokensInput", "TokensOutput", "TokensReasoning", "TokensCacheRead", "TokensCacheWrite",
        "TokensIn", "TokensOut", "CostUsd", "Turns", "AssistantElapsedMs", "ToolElapsedMs",
        "ObservedActiveMs", "ActiveCoverage", "DataQualityValid", "DataQualityIssues", "TokenStatus",
        "CostStatus", "TokensScope", "SubagentsSpawned", "SubagentsContributors", "AttemptSnapshot",
        "GateVerdictSnapshot", "ProjectType", "ImportedAt", "Overflow"
    ];

    /// <summary>Every <c>"PbPhaseModelUsage"</c> column; the first four are its natural key.</summary>
    private static readonly string[] PhaseModelUsageColumns =
    [
        "UserId", "Repo", "PhaseExecutionId", "Model", "Turns", "TokensInput", "TokensOutput",
        "TokensReasoning", "TokensCacheRead", "TokensCacheWrite", "TokensIn", "TokensOut", "CostUsd",
        "CostStatus", "ActiveMs"
    ];

    /// <summary>Every <c>"PbPhaseSubagent"</c> column; the first four are its natural key.</summary>
    private static readonly string[] PhaseSubagentColumns =
    [
        "UserId", "Repo", "PhaseExecutionId", "SessionId", "ParentSessionId", "Agent", "StartedAt",
        "EndedAt", "ElapsedMs", "Complete", "Turns", "TokensIn", "TokensOut", "CostUsd", "CostStatus"
    ];

    /// <summary>
    /// Upsert for one phase execution, keyed on <c>UcPbPhaseExecUserRepoId</c> (REQ-FN-094).
    /// </summary>
    /// <remarks>
    /// <b>Upsert, not <c>DO NOTHING</c>.</b> The exporter re-emits every currently readable window, so a
    /// later import legitimately carries a <i>more complete</i> reading of a window already stored — an
    /// EOF window that has since closed, for instance — and refusing it would freeze the partial row.
    /// The <c>WHERE</c> is what keeps re-import honest in the other direction: an identical bundle
    /// changes nothing and reports zero rows written rather than counting an untouched row as new.
    /// <c>"ImportedAt"</c> is excluded from that comparison and included in the <c>SET</c>, because it
    /// changes on every import by definition and would otherwise make every re-import look like a change.
    /// </remarks>
    private static readonly string InsertPhaseExecutionSql =
        PhaseUpsertSql("PbPhaseExecution", PhaseExecutionColumns, 3, "ImportedAt");

    /// <summary>Upsert for one model's usage, keyed on <c>UcPbPhaseModelUserRepoIdModel</c>.</summary>
    private static readonly string InsertPhaseModelUsageSql =
        PhaseUpsertSql("PbPhaseModelUsage", PhaseModelUsageColumns, 4);

    /// <summary>Upsert for one sub-agent session, keyed on <c>UcPbPhaseSubUserRepoIdSession</c>.</summary>
    private static readonly string InsertPhaseSubagentSql =
        PhaseUpsertSql("PbPhaseSubagent", PhaseSubagentColumns, 4);

    /// <summary>
    /// Builds the idempotent upsert for one phase table.
    /// </summary>
    /// <remarks>
    /// Generated rather than written out three times because the same forty column names would otherwise
    /// appear in an insert list, a value list, a <c>SET</c> clause and a <c>WHERE</c> comparison — four
    /// places for one of them to be forgotten, and a forgotten column in the <c>SET</c> is a value that
    /// silently never updates. The shape is exactly the one the hand-written statements above use.
    /// </remarks>
    /// <param name="aTable">The table name.</param>
    /// <param name="aColumns">Every column, in insert order.</param>
    /// <param name="aKeyColumns">How many leading columns form the natural key.</param>
    /// <param name="aExcludedFromCompare">Columns that change on every import and so cannot signal one.</param>
    /// <returns>The statement.</returns>
    private static string PhaseUpsertSql(
        string aTable, string[] aColumns, int aKeyColumns, params string[] aExcludedFromCompare)
    {
        var vKey = aColumns.Take(aKeyColumns).ToArray();
        var vPayload = aColumns.Skip(aKeyColumns).ToArray();
        var vCompared = vPayload.Except(aExcludedFromCompare, StringComparer.Ordinal).ToArray();

        var vInsert = string.Join(",", aColumns.Select(aC => $"\"{aC}\""));
        var vValues = string.Join(",", aColumns.Select(Parameter));
        var vSet = string.Join(",\n            ", vPayload.Select(aC => $"\"{aC}\" = EXCLUDED.\"{aC}\""));
        var vMine = string.Join(",", vCompared.Select(aC => $"\"{aTable}\".\"{aC}\""));
        var vTheirs = string.Join(",", vCompared.Select(aC => $"EXCLUDED.\"{aC}\""));

        return $"""
            INSERT INTO "{aTable}" ({vInsert})
            VALUES ({vValues})
            ON CONFLICT ({string.Join(",", vKey.Select(aC => $"\"{aC}\""))}) DO UPDATE SET
            {vSet}
            WHERE ({vMine}) IS DISTINCT FROM ({vTheirs})
            """;
    }

    /// <summary>Renders one column's insert parameter, casting the one <c>jsonb</c> column.</summary>
    /// <param name="aColumn">The column name.</param>
    /// <returns>The parameter expression.</returns>
    private static string Parameter(string aColumn) =>
        string.Equals(aColumn, "Overflow", StringComparison.Ordinal)
            ? "CAST(@Overflow AS jsonb)"
            : "@" + aColumn;

    /// <summary>
    /// Records one obtained dataset identity; re-recording the same triple is a no-op (REQ-NFR-019).
    /// </summary>
    private const string InsertProvenanceSql = """
        INSERT INTO "SourceProvenance" ("UserId","Repo","SourceSha","Kind","ObtainedTs")
        VALUES (@UserId,@Repo,@SourceSha,@Kind,@ObtainedTs)
        ON CONFLICT ON CONSTRAINT "PkSourceProvenance" DO NOTHING
        """;

    /// <summary>
    /// Every distinct <c>(user, repo, source SHA)</c> the eight stream tables hold rows under.
    /// </summary>
    /// <remarks>
    /// One <c>UNION ALL</c> arm per table so a finding names the table it sits in — the fact the
    /// 2026-08-29 cleanup had to reconstruct by hand. The list is the same eight tables
    /// <see cref="StreamTables"/> names, and a guardrail test asserts the two do not drift: a stream
    /// table this query forgot is a table pollution could hide in.
    /// </remarks>
    private const string StoredProvenanceSql = """
        SELECT "UserId", "Repo", "SourceSha", 'Run' AS "Table", COUNT(*)::int AS "Rows"
        FROM "Run" WHERE @aUserId IS NULL OR "UserId" = @aUserId GROUP BY 1,2,3
        UNION ALL
        SELECT "UserId", "Repo", "SourceSha", 'Gate', COUNT(*)::int
        FROM "Gate" WHERE @aUserId IS NULL OR "UserId" = @aUserId GROUP BY 1,2,3
        UNION ALL
        SELECT "UserId", "Repo", "SourceSha", 'Session', COUNT(*)::int
        FROM "Session" WHERE @aUserId IS NULL OR "UserId" = @aUserId GROUP BY 1,2,3
        UNION ALL
        SELECT "UserId", "Repo", "SourceSha", 'Commit', COUNT(*)::int
        FROM "Commit" WHERE @aUserId IS NULL OR "UserId" = @aUserId GROUP BY 1,2,3
        UNION ALL
        SELECT "UserId", "Repo", "SourceSha", 'Miss', COUNT(*)::int
        FROM "Miss" WHERE @aUserId IS NULL OR "UserId" = @aUserId GROUP BY 1,2,3
        UNION ALL
        SELECT "UserId", "Repo", "SourceSha", 'MissFix', COUNT(*)::int
        FROM "MissFix" WHERE @aUserId IS NULL OR "UserId" = @aUserId GROUP BY 1,2,3
        UNION ALL
        SELECT "UserId", "Repo", "SourceSha", 'MissAmend', COUNT(*)::int
        FROM "MissAmend" WHERE @aUserId IS NULL OR "UserId" = @aUserId GROUP BY 1,2,3
        UNION ALL
        SELECT "UserId", "Repo", "SourceSha", 'PbEvent', COUNT(*)::int
        FROM "PbEvent" WHERE @aUserId IS NULL OR "UserId" = @aUserId GROUP BY 1,2,3
        """;

    /// <summary>
    /// Every dataset identity an ingest path recorded obtaining — the ledger and the two stamps.
    /// </summary>
    /// <remarks>
    /// <c>"SyncState"."LastSha"</c> and <c>"UserRepo"."BundleSha"</c> are read directly rather than
    /// relying on the schema script having adopted them, so the audit is correct on the first run
    /// against a database whose schema has not been re-applied since.
    /// </remarks>
    private const string ObtainedProvenanceSql = """
        SELECT "UserId", "Repo", "SourceSha", "Kind", "ObtainedTs"
        FROM "SourceProvenance" WHERE @aUserId IS NULL OR "UserId" = @aUserId
        UNION ALL
        SELECT "UserId", "Repo", "LastSha", 'api', COALESCE("LastSyncTs", '')
        FROM "SyncState"
        WHERE (@aUserId IS NULL OR "UserId" = @aUserId)
          AND "LastSha" IS NOT NULL AND btrim("LastSha") <> ''
        UNION ALL
        SELECT "UserId", "Repo", "BundleSha", 'import', "ConnectedTs"
        FROM "UserRepo"
        WHERE (@aUserId IS NULL OR "UserId" = @aUserId)
          AND "BundleSha" IS NOT NULL AND btrim("BundleSha") <> ''
        """;

    /// <summary>Writes one repository's sync bookkeeping, replacing whatever was there.</summary>
    private const string UpsertSyncStateSql = """
        INSERT INTO "SyncState" (
            "UserId","Repo","Kind","Branch","LastSha","LastSyncTs","LastError",
            "RunsCount","GatesCount","SessionsCount","CommitsCount","EventsCount","MissesCount",
            "SessionDuplicatesCollapsed")
        VALUES (
            @UserId,@Repo,@Kind,@Branch,@LastSha,@LastSyncTs,@LastError,
            @RunsCount,@GatesCount,@SessionsCount,@CommitsCount,@EventsCount,@MissesCount,
            @SessionDuplicatesCollapsed)
        ON CONFLICT ON CONSTRAINT "PkSyncState" DO UPDATE SET
            "Kind" = EXCLUDED."Kind",
            "Branch" = EXCLUDED."Branch",
            "LastSha" = EXCLUDED."LastSha",
            "LastSyncTs" = EXCLUDED."LastSyncTs",
            "LastError" = EXCLUDED."LastError",
            "RunsCount" = EXCLUDED."RunsCount",
            "GatesCount" = EXCLUDED."GatesCount",
            "SessionsCount" = EXCLUDED."SessionsCount",
            "CommitsCount" = EXCLUDED."CommitsCount",
            "EventsCount" = EXCLUDED."EventsCount",
            "MissesCount" = EXCLUDED."MissesCount",
            "SessionDuplicatesCollapsed" = EXCLUDED."SessionDuplicatesCollapsed"
        """;

    /// <summary>
    /// Writes one connected repository, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// <c>"SourceKind"</c>, <c>"BundleSha"</c> and <c>"LastImportTs"</c> round-trip here so the import
    /// cluster has a write path (REQ-FN-084, REQ-FN-085); <b>no import behaviour lives in this class</b>.
    /// A fetched source writes <c>'Synced'</c> with both other columns <c>null</c>, which is what keeps
    /// the "<c>LastSha</c> or <c>BundleSha</c>, never both" invariant true for everything TfLens writes
    /// today.
    /// </remarks>
    private const string UpsertUserRepoSql = """
        INSERT INTO "UserRepo" (
            "UserId","Repo","Owner","Name","Branch","Kind","Framework","IsPublic","ConnectedTs",
            "SourceKind","BundleSha","LastImportTs")
        VALUES (@UserId,@Repo,@Owner,@Name,@Branch,@Kind,@Framework,@IsPublic,@ConnectedTs,
            @SourceKind,@BundleSha,@LastImportTs)
        ON CONFLICT ON CONSTRAINT "PkUserRepo" DO UPDATE SET
            "Owner" = EXCLUDED."Owner",
            "Name" = EXCLUDED."Name",
            "Branch" = EXCLUDED."Branch",
            "Kind" = EXCLUDED."Kind",
            "Framework" = EXCLUDED."Framework",
            "IsPublic" = EXCLUDED."IsPublic",
            "ConnectedTs" = EXCLUDED."ConnectedTs",
            "SourceKind" = EXCLUDED."SourceKind",
            "BundleSha" = EXCLUDED."BundleSha",
            "LastImportTs" = EXCLUDED."LastImportTs"
        """;
}
