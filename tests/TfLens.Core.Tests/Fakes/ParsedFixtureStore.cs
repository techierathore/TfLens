using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;

namespace TfLens.Core.Tests.Fakes;

/// <summary>
/// An in-memory <see cref="ITelemetryStore"/> over a checked-in fixture repository's JSONL streams.
/// </summary>
/// <remarks>
/// The point of this fake is that it is <b>not</b> a hand-built object graph: it runs the real
/// <see cref="StreamParser"/> over the real fixture files, so the records the extras are computed from
/// are the records the production parser produces from the same bytes. Only the storage round-trip is
/// faked. Reads are user-scoped exactly as the interface requires (ADR-013) — a read for another user
/// returns nothing, so a test that forgot to scope fails rather than passing by accident.
/// </remarks>
public sealed class ParsedFixtureStore : ITelemetryStore
{
    private readonly int objUserId;
    private readonly string objRepo;
    private readonly string objFramework;
    private readonly string objSourceSha;
    private readonly List<RunRecord> objRuns = [];
    private readonly List<GateRecord> objGates = [];
    private readonly List<SessionRecord> objSessions = [];
    private readonly List<CommitRecord> objCommits = [];

    /// <summary>
    /// Loads a fixture repository's four TechieFlow streams.
    /// </summary>
    /// <param name="aUserId">The user the records are attributed to.</param>
    /// <param name="aRepo">The repository identifier the records carry.</param>
    /// <param name="aFramework">The provenance axis the records belong to.</param>
    /// <param name="aMetricsFolder">The fixture's <c>docs/metrics</c> folder.</param>
    /// <param name="aSourceSha">The SHA to report as the dataset SHA.</param>
    /// <exception cref="DirectoryNotFoundException">The fixture folder does not exist.</exception>
    public ParsedFixtureStore(
        int aUserId,
        string aRepo,
        string aFramework,
        string aMetricsFolder,
        string aSourceSha)
    {
        if (!Directory.Exists(aMetricsFolder))
        {
            throw new DirectoryNotFoundException($"Fixture stream folder not found: {aMetricsFolder}");
        }

        objUserId = aUserId;
        objRepo = aRepo;
        objFramework = aFramework;
        objSourceSha = aSourceSha;

        var vParser = new StreamParser();
        foreach (var vStream in StreamNames.TechieFlow)
        {
            var vPath = Path.Combine(aMetricsFolder, vStream + ".jsonl");
            if (!File.Exists(vPath))
            {
                continue;
            }

            var vParsed = vParser.Parse(
                aUserId, aRepo, aSourceSha, StreamNames.ToKind(vStream), File.ReadAllText(vPath));

            objRuns.AddRange(vParsed.Runs);
            objGates.AddRange(vParsed.Gates);
            objSessions.AddRange(vParsed.Sessions);
            objCommits.AddRange(vParsed.Commits);
        }
    }

    /// <summary>Run records the parser produced from the fixture.</summary>
    public IReadOnlyList<RunRecord> Runs => objRuns;

    /// <summary>Gate records the parser produced from the fixture.</summary>
    public IReadOnlyList<GateRecord> Gates => objGates;

    /// <summary>Session records the parser produced from the fixture.</summary>
    public IReadOnlyList<SessionRecord> Sessions => objSessions;

    /// <summary>Commit records the parser produced from the fixture.</summary>
    public IReadOnlyList<CommitRecord> Commits => objCommits;

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken aCancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken aCancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<int> UpsertAsync(ParseResult aParsed, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The fixture store is read-only.");

    /// <inheritdoc />
    public Task<IReadOnlyList<RunRecord>> ReadRunsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult(Scoped(objRuns, aUserId, aFramework, aRepo));

    /// <inheritdoc />
    public Task<IReadOnlyList<GateRecord>> ReadGatesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult(Scoped(objGates, aUserId, aFramework, aRepo));

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> ReadSessionsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult(Scoped(objSessions, aUserId, aFramework, aRepo));

    /// <inheritdoc />
    public Task<IReadOnlyList<CommitRecord>> ReadCommitsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult(Scoped(objCommits, aUserId, aFramework, aRepo));

    /// <inheritdoc />
    public Task<IReadOnlyList<PbEventRecord>> ReadPbEventsAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PbEventRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncState>> ReadSyncStateAsync(
        int aUserId, CancellationToken aCancellationToken = default)
    {
        IReadOnlyList<SyncState> vStates = aUserId == objUserId
            ?
            [
                new SyncState
                {
                    UserId = objUserId,
                    Repo = objRepo,
                    Kind = objFramework,
                    Branch = "main",
                    LastSha = objSourceSha,
                    LastSyncTs = "2026-08-23T13:05:00Z",
                    RunsCount = objRuns.Count,
                    GatesCount = objGates.Count,
                    SessionsCount = objSessions.Count,
                    CommitsCount = objCommits.Count
                }
            ]
            : [];

        return Task.FromResult(vStates);
    }

    /// <inheritdoc />
    public Task WriteSyncStateAsync(SyncState aState, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The fixture store is read-only.");

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadUserReposAsync(
        int aUserId, CancellationToken aCancellationToken = default)
    {
        IReadOnlyList<UserRepo> vRepos = aUserId == objUserId
            ?
            [
                new UserRepo
                {
                    UserId = objUserId,
                    Repo = objRepo,
                    Owner = objRepo.Split('/')[0],
                    Name = objRepo.Split('/')[^1],
                    Branch = "main",
                    Kind = objFramework,
                    Framework = objFramework,
                    ConnectedTs = "2026-08-19T07:00:00Z"
                }
            ]
            : [];

        return Task.FromResult(vRepos);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadAllUserReposAsync(CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserRepo>>([]);

    /// <inheritdoc />
    public Task WriteUserRepoAsync(UserRepo aRepo, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The fixture store is read-only.");

    /// <inheritdoc />
    public Task DeleteRepoDataAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The fixture store is read-only.");

    /// <inheritdoc />
    public Task<RebuildReport> RebuildAsync(int? aUserId = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The fixture store is read-only.");

    /// <summary>
    /// Applies the isolation the real store applies — user, framework and optional repository.
    /// </summary>
    /// <typeparam name="TRecord">The record type.</typeparam>
    /// <param name="aRecords">Every record the fixture holds.</param>
    /// <param name="aUserId">The user the caller asked for.</param>
    /// <param name="aFramework">The framework the caller asked for.</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all.</param>
    /// <returns>The records visible to that caller.</returns>
    private IReadOnlyList<TRecord> Scoped<TRecord>(
        List<TRecord> aRecords, int aUserId, string aFramework, string? aRepo)
    {
        var vVisible = aUserId == objUserId
            && string.Equals(aFramework, objFramework, StringComparison.Ordinal)
            && (aRepo is null || string.Equals(aRepo, objRepo, StringComparison.Ordinal));

        return vVisible ? aRecords : [];
    }
}
