using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Sync;

/// <summary>
/// An in-memory stand-in for the store, deduping on the same natural keys the real one indexes.
/// </summary>
/// <remarks>
/// The real store is the storage area's; this double exists so the sync path can be exercised on its
/// own. Its <c>RebuildAsync</c> replays the raw archive through the same parser instance the sync used,
/// which is what makes the sync-then-rebuild count-identity assertion (REQ-FN-029) about the archive
/// rather than about the database.
/// </remarks>
public sealed class FakeTelemetryStore : ITelemetryStore
{
    private readonly HashSet<string> objKeys = [];

    /// <summary>The repositories this store reports as connected.</summary>
    public List<UserRepo> Repos { get; } = [];

    /// <summary>The sync state rows, keyed by user and repository.</summary>
    public Dictionary<string, SyncState> States { get; } = [];

    /// <summary>Every state row written, in order, so a test can see the sequence.</summary>
    public List<SyncState> StateWrites { get; } = [];

    /// <summary>Rows held, by stream wire name.</summary>
    public Dictionary<string, int> RowCounts { get; } = [];

    /// <summary>The raw archive root a rebuild replays from.</summary>
    public string? RawRoot { get; set; }

    /// <summary>The parser a rebuild replays through.</summary>
    public IStreamParser? Parser { get; set; }

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken aCancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken aCancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<int> UpsertAsync(ParseResult aParsed, CancellationToken aCancellationToken = default)
    {
        var vWritten = 0;

        foreach (var vKey in NaturalKeys(aParsed))
        {
            if (objKeys.Add(vKey))
            {
                vWritten++;
            }
        }

        var vStream = StreamNames.ToName(aParsed.Stream);
        RowCounts[vStream] = objKeys.Count(aK => aK.StartsWith($"{vStream}|", StringComparison.Ordinal));

        return Task.FromResult(vWritten);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunRecord>> ReadRunsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RunRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<GateRecord>> ReadGatesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GateRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> ReadSessionsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SessionRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<CommitRecord>> ReadCommitsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CommitRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<PbEventRecord>> ReadPbEventsAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PbEventRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncState>> ReadSyncStateAsync(int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SyncState>>(States.Values.Where(aS => aS.UserId == aUserId).ToList());

    /// <inheritdoc />
    public Task WriteSyncStateAsync(SyncState aState, CancellationToken aCancellationToken = default)
    {
        States[$"{aState.UserId}|{aState.Repo}"] = aState;
        StateWrites.Add(aState);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadUserReposAsync(int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserRepo>>(Repos.Where(aR => aR.UserId == aUserId).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadAllUserReposAsync(CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserRepo>>(Repos.ToList());

    /// <inheritdoc />
    public Task WriteUserRepoAsync(UserRepo aRepo, CancellationToken aCancellationToken = default)
    {
        Repos.Add(aRepo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteRepoDataAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public async Task<RebuildReport> RebuildAsync(int? aUserId = null, CancellationToken aCancellationToken = default)
    {
        var vStarted = DateTimeOffset.UtcNow.ToString("O");

        objKeys.Clear();
        RowCounts.Clear();

        if (RawRoot is null || Parser is null || !Directory.Exists(RawRoot))
        {
            return new RebuildReport(0, 0, 0, 0, vStarted, DateTimeOffset.UtcNow.ToString("O"));
        }

        var vFiles = Directory.GetFiles(RawRoot, "*.jsonl", SearchOption.AllDirectories).OrderBy(aF => aF).ToList();
        var vRecords = 0;
        var vDuplicates = 0;
        var vInvalid = 0;

        foreach (var vFile in vFiles)
        {
            var vName = Path.GetFileNameWithoutExtension(vFile);
            var vStream = vName[..vName.LastIndexOf('-')];
            var vSha = vName[(vName.LastIndexOf('-') + 1)..];
            var vRepoFolder = Path.GetFileName(Path.GetDirectoryName(vFile))!;
            var vRepo = vRepoFolder.Replace("__", "/", StringComparison.Ordinal);
            var vUser = int.Parse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(vFile))!));

            var vText = await File.ReadAllTextAsync(vFile, aCancellationToken);
            var vParsed = Parser.Parse(vUser, vRepo, vSha, StreamNames.ToKind(vStream), vText);

            vRecords += await UpsertAsync(vParsed, aCancellationToken);
            vDuplicates += vParsed.DuplicatesCollapsed;
            vInvalid += vParsed.InvalidLines;
        }

        return new RebuildReport(
            vFiles.Count, vRecords, vDuplicates, vInvalid, vStarted, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Builds the natural key of every record in a parse result.</summary>
    /// <param name="aParsed">The parse output.</param>
    /// <returns>One key per record.</returns>
    private static IEnumerable<string> NaturalKeys(ParseResult aParsed)
    {
        foreach (var vRun in aParsed.Runs)
        {
            yield return $"{StreamNames.Runs}|{vRun.UserId}|{vRun.Repo}|{vRun.Ts}";
        }

        foreach (var vGate in aParsed.Gates)
        {
            yield return $"{StreamNames.Gates}|{vGate.UserId}|{vGate.Repo}|{vGate.Ts}";
        }

        foreach (var vSession in aParsed.Sessions)
        {
            yield return $"{StreamNames.Sessions}|{vSession.UserId}|{vSession.Repo}|{vSession.SessionId}";
        }

        foreach (var vCommit in aParsed.Commits)
        {
            yield return $"{StreamNames.Commits}|{vCommit.UserId}|{vCommit.Repo}|{vCommit.Sha}";
        }

        foreach (var vEvent in aParsed.PbEvents)
        {
            yield return $"{StreamNames.Events}|{vEvent.UserId}|{vEvent.Repo}|{vEvent.Ts}";
        }
    }
}
