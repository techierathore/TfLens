using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;

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
    /// <summary>The stream tables, in the order a rebuild truncates them.</summary>
    private static readonly string[] StreamTables = ["Run", "Gate", "Session", "Commit", "PbEvent"];

    /// <summary>Resolved once — the schema script does not move while the process runs.</summary>
    private static string? SchemaPathCache;

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

        return vWritten;
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
        foreach (var vTable in StreamTables)
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

        foreach (var vArchive in EnumerateArchive(aUserId))
        {
            var vText = await File.ReadAllTextAsync(vArchive.Path, aCancellationToken).ConfigureAwait(false);
            var vParsed = objParser.Parse(vArchive.UserId, vArchive.Repo, vArchive.Sha, vArchive.Stream, vText);

            vFiles++;
            vDuplicates += vParsed.DuplicatesCollapsed;
            vInvalid += vParsed.InvalidLines;
            vRecords += await UpsertAsync(vParsed, aCancellationToken).ConfigureAwait(false);
        }

        await RecomputeSyncCountsAsync(aUserId, aCancellationToken).ConfigureAwait(false);

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
                "EventsCount"   = (SELECT COUNT(*) FROM "PbEvent" p WHERE p."UserId" = t."UserId" AND p."Repo" = t."Repo")
            WHERE @aUserId IS NULL OR t."UserId" = @aUserId
            """;

        await using var vConnection = await OpenAsync(aCancellationToken).ConfigureAwait(false);
        await vConnection.ExecuteAsync(
            new CommandDefinition(vSql, new { aUserId }, cancellationToken: aCancellationToken))
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

    /// <summary>
    /// Per-repository, per-stream row counts, backfilled counts and newest timestamp (REQ-UI-014).
    /// </summary>
    /// <remarks>
    /// <c>"Ts"</c> is stored as ISO-8601 text, whose lexical order is its chronological order, so
    /// <c>MAX</c> over the column is the newest record without a cast. Only <c>"Run"</c> and <c>"Gate"</c>
    /// carry a <c>"Backfilled"</c> column — SCHEMA.md does not put the flag on the other three streams —
    /// so those report zero rather than pretending the fact was captured.
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
        """;

    /// <summary>Idempotent insert for <c>runs</c>; conflicts on <c>UcRunIdentity</c> are no-ops.</summary>
    private const string InsertRunSql = """
        INSERT INTO "Run" (
            "UserId","Repo","SourceSha","V","Ts","App","ProjectType","ProjectTypeInferred","Backfilled",
            "Harness","Cmd","Mode","Started","Ended","DurationS","ReqsTouched","ReqsCount","Subagents",
            "FilesWritten","BuildResult","Tier","TierModel","Model","Models","Routed","TokensIn","TokensOut",
            "TokensCacheRead","TokensCacheWrite","CostUsd","TokensScope","Attempt","Overflow")
        VALUES (
            @UserId,@Repo,@SourceSha,@V,@Ts,@App,@ProjectType,@ProjectTypeInferred,@Backfilled,
            @Harness,@Cmd,@Mode,@Started,@Ended,@DurationS,@ReqsTouched,@ReqsCount,@Subagents,
            @FilesWritten,@BuildResult,@Tier,@TierModel,@Model,@Models,@Routed,@TokensIn,@TokensOut,
            @TokensCacheRead,@TokensCacheWrite,@CostUsd,@TokensScope,@Attempt,CAST(@Overflow AS jsonb))
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

    /// <summary>Writes one repository's sync bookkeeping, replacing whatever was there.</summary>
    private const string UpsertSyncStateSql = """
        INSERT INTO "SyncState" (
            "UserId","Repo","Kind","Branch","LastSha","LastSyncTs","LastError",
            "RunsCount","GatesCount","SessionsCount","CommitsCount","EventsCount")
        VALUES (
            @UserId,@Repo,@Kind,@Branch,@LastSha,@LastSyncTs,@LastError,
            @RunsCount,@GatesCount,@SessionsCount,@CommitsCount,@EventsCount)
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
            "EventsCount" = EXCLUDED."EventsCount"
        """;

    /// <summary>Writes one connected repository, replacing whatever was there.</summary>
    private const string UpsertUserRepoSql = """
        INSERT INTO "UserRepo" (
            "UserId","Repo","Owner","Name","Branch","Kind","Framework","IsPublic","ConnectedTs")
        VALUES (@UserId,@Repo,@Owner,@Name,@Branch,@Kind,@Framework,@IsPublic,@ConnectedTs)
        ON CONFLICT ON CONSTRAINT "PkUserRepo" DO UPDATE SET
            "Owner" = EXCLUDED."Owner",
            "Name" = EXCLUDED."Name",
            "Branch" = EXCLUDED."Branch",
            "Kind" = EXCLUDED."Kind",
            "Framework" = EXCLUDED."Framework",
            "IsPublic" = EXCLUDED."IsPublic",
            "ConnectedTs" = EXCLUDED."ConnectedTs"
        """;
}
