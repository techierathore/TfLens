using Dapper;
using Npgsql;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Repos;

/// <summary>
/// The slice of <see cref="ITelemetryStore"/> the repository registry uses, against the real
/// PostgreSQL 16 database.
/// </summary>
/// <remarks>
/// <para>
/// The registry's isolation claim is a claim about SQL predicates, so testing it against an in-memory
/// dictionary would prove nothing. This type therefore runs the same double-quoted, user-scoped SQL
/// the production store will run, against the schema in <c>database/001-schema.sql</c>.
/// </para>
/// <para>
/// It exists only until the Storage cluster's <c>PostgresStore</c> lands; the members outside the
/// registry's reach throw rather than pretending to work. The seed and count helpers are test-only.
/// </para>
/// </remarks>
public sealed class PostgresRepoTestStore : ITelemetryStore
{
    private readonly string objConnectionString;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="aConnectionString">The PostgreSQL connection string.</param>
    public PostgresRepoTestStore(string aConnectionString) => objConnectionString = aConnectionString;

    /// <summary>The stream tables a repository's rows can live in, all keyed by user and repository.</summary>
    private static readonly string[] StreamTables = ["Run", "Gate", "Session", "Commit", "PbEvent"];

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRepo>> ReadUserReposAsync(
        int aUserId,
        CancellationToken aCancellationToken = default)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        var vRows = await vConnection.QueryAsync<UserRepo>(
            """
            SELECT "UserId", "Repo", "Owner", "Name", "Branch", "Kind", "Framework", "IsPublic", "ConnectedTs"
            FROM "UserRepo"
            WHERE "UserId" = @aUserId
            ORDER BY "ConnectedTs"
            """,
            new { aUserId }).ConfigureAwait(false);

        return vRows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRepo>> ReadAllUserReposAsync(CancellationToken aCancellationToken = default)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        var vRows = await vConnection.QueryAsync<UserRepo>(
            """
            SELECT "UserId", "Repo", "Owner", "Name", "Branch", "Kind", "Framework", "IsPublic", "ConnectedTs"
            FROM "UserRepo"
            ORDER BY "UserId", "Repo"
            """).ConfigureAwait(false);

        return vRows.ToList();
    }

    /// <inheritdoc />
    public async Task WriteUserRepoAsync(UserRepo aRepo, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        await vConnection.ExecuteAsync(
            """
            INSERT INTO "UserRepo" ("UserId", "Repo", "Owner", "Name", "Branch", "Kind", "Framework", "IsPublic", "ConnectedTs")
            VALUES (@UserId, @Repo, @Owner, @Name, @Branch, @Kind, @Framework, @IsPublic, @ConnectedTs)
            ON CONFLICT ("UserId", "Repo") DO UPDATE SET
                "Branch" = EXCLUDED."Branch",
                "Kind" = EXCLUDED."Kind",
                "Framework" = EXCLUDED."Framework"
            """,
            aRepo).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncState>> ReadSyncStateAsync(
        int aUserId,
        CancellationToken aCancellationToken = default)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        var vRows = await vConnection.QueryAsync<SyncState>(
            """
            SELECT "UserId", "Repo", "Kind", "Branch", "LastSha", "LastSyncTs", "LastError",
                   "RunsCount", "GatesCount", "SessionsCount", "CommitsCount", "EventsCount"
            FROM "SyncState"
            WHERE "UserId" = @aUserId
            """,
            new { aUserId }).ConfigureAwait(false);

        return vRows.ToList();
    }

    /// <inheritdoc />
    public async Task WriteSyncStateAsync(SyncState aState, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        await vConnection.ExecuteAsync(
            """
            INSERT INTO "SyncState" ("UserId", "Repo", "Kind", "Branch", "LastSha", "LastSyncTs", "LastError",
                                     "RunsCount", "GatesCount", "SessionsCount", "CommitsCount", "EventsCount")
            VALUES (@UserId, @Repo, @Kind, @Branch, @LastSha, @LastSyncTs, @LastError,
                    @RunsCount, @GatesCount, @SessionsCount, @CommitsCount, @EventsCount)
            ON CONFLICT ("UserId", "Repo") DO UPDATE SET
                "LastSha" = EXCLUDED."LastSha",
                "LastSyncTs" = EXCLUDED."LastSyncTs",
                "LastError" = EXCLUDED."LastError",
                "RunsCount" = EXCLUDED."RunsCount",
                "GatesCount" = EXCLUDED."GatesCount",
                "SessionsCount" = EXCLUDED."SessionsCount",
                "CommitsCount" = EXCLUDED."CommitsCount",
                "EventsCount" = EXCLUDED."EventsCount"
            """,
            aState).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteRepoDataAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        foreach (var vTable in StreamTables)
        {
            await vConnection.ExecuteAsync(
                $"""DELETE FROM "{vTable}" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""",
                new { aUserId, aRepo }).ConfigureAwait(false);
        }

        await vConnection.ExecuteAsync(
            """DELETE FROM "SyncState" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""",
            new { aUserId, aRepo }).ConfigureAwait(false);

        await vConnection.ExecuteAsync(
            """DELETE FROM "UserRepo" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""",
            new { aUserId, aRepo }).ConfigureAwait(false);
    }

    /// <summary>
    /// Test helper: writes one <c>"Run"</c> row so a purge has something to remove.
    /// </summary>
    /// <param name="aUserId">The owning user.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aTs">The record timestamp, which is part of the dedupe key.</param>
    /// <returns>A task that completes when the row is written.</returns>
    public async Task SeedRunAsync(int aUserId, string aRepo, string aTs)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        await vConnection.ExecuteAsync(
            """
            INSERT INTO "Run" ("UserId", "Repo", "SourceSha", "V", "Ts", "App", "Cmd")
            VALUES (@aUserId, @aRepo, 'testsha', 1, @aTs, 'tflens', 'build')
            ON CONFLICT DO NOTHING
            """,
            new { aUserId, aRepo, aTs }).ConfigureAwait(false);
    }

    /// <summary>
    /// Test helper: counts the <c>"Run"</c> rows stored for one user and repository.
    /// </summary>
    /// <param name="aUserId">The owning user.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <returns>How many rows remain.</returns>
    public async Task<int> CountRunsAsync(int aUserId, string aRepo)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        return await vConnection.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "Run" WHERE "UserId" = @aUserId AND "Repo" = @aRepo""",
            new { aUserId, aRepo }).ConfigureAwait(false);
    }

    /// <summary>
    /// Test helper: deletes every row this test user owns, whatever repository it belongs to.
    /// </summary>
    /// <param name="aUserId">The user to wipe.</param>
    /// <returns>A task that completes when the user has no rows left.</returns>
    public async Task PurgeUserAsync(int aUserId)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        foreach (var vTable in StreamTables.Concat(["SyncState", "UserRepo"]))
        {
            await vConnection.ExecuteAsync(
                $"""DELETE FROM "{vTable}" WHERE "UserId" = @aUserId""",
                new { aUserId }).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken aCancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task<bool> PingAsync(CancellationToken aCancellationToken = default)
    {
        await using var vConnection = new NpgsqlConnection(objConnectionString);
        return await vConnection.ExecuteScalarAsync<int>("SELECT 1").ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public Task<int> UpsertAsync(ParseResult aParsed, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("Parsing belongs to the sync cluster, not the registry.");

    /// <inheritdoc />
    public Task<IReadOnlyList<RunRecord>> ReadRunsAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The registry does not read stream records.");

    /// <inheritdoc />
    public Task<IReadOnlyList<GateRecord>> ReadGatesAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The registry does not read stream records.");

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> ReadSessionsAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The registry does not read stream records.");

    /// <inheritdoc />
    public Task<IReadOnlyList<CommitRecord>> ReadCommitsAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The registry does not read stream records.");

    /// <inheritdoc />
    public Task<IReadOnlyList<PbEventRecord>> ReadPbEventsAsync(
        int aUserId,
        string? aRepo = null,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The registry does not read stream records.");

    /// <inheritdoc />
    public Task<RebuildReport> RebuildAsync(int? aUserId = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("Rebuild belongs to the storage cluster.");
}
