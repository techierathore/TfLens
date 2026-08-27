using System.Text.Json;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// An <see cref="ITelemetryStore"/> that serves the checked-in fixture streams straight from disk.
/// </summary>
/// <remarks>
/// The engine reads every record through <see cref="ITelemetryStore"/>, so the parity test can feed it
/// exactly the bytes the oracle was run over without touching PostgreSQL or the real parser (which is
/// another area's code). Only the read members are implemented; anything else throws, so a test that
/// accidentally reached for a write would fail loudly rather than pass quietly.
/// </remarks>
public sealed class FixtureTelemetryStore : ITelemetryStore
{
    private const string FixtureSha = "fixture";

    private readonly List<GateRecord> objGates = [];
    private readonly List<RunRecord> objRuns = [];
    private readonly List<SessionRecord> objSessions = [];
    private readonly List<CommitRecord> objCommits = [];
    private readonly List<PbEventRecord> objPbEvents = [];
    private readonly List<UserRepo> objRepos = [];

    /// <summary>
    /// Session collapses ingest recorded per repository, keyed <c>(userId, repo)</c>.
    /// </summary>
    /// <remarks>
    /// Sessions are deduped on the way into the real store, so this figure cannot be derived from the
    /// records the fixture serves — it is bookkeeping ingest left behind, and the engine reads it from
    /// <c>"SyncState"</c> (REQ-FN-063). A repository nobody has recorded a collapse for reads zero, which
    /// is what a repository with no duplicates would genuinely report.
    /// </remarks>
    private readonly Dictionary<(int UserId, string Repo), int> objSessionCollapses = [];

    /// <summary>
    /// Records how many session records ingest collapsed for one repository.
    /// </summary>
    /// <param name="aUserId">The user the repository belongs to.</param>
    /// <param name="aRepo">The <c>owner/name</c> the collapses were counted for.</param>
    /// <param name="aCollapsed">The number of session records ingest discarded as duplicates.</param>
    /// <returns>The same store, for chaining.</returns>
    public FixtureTelemetryStore WithSessionCollapses(int aUserId, string aRepo, int aCollapsed)
    {
        objSessionCollapses[(aUserId, aRepo)] = aCollapsed;
        return this;
    }

    /// <summary>
    /// Loads one fixture repository's streams.
    /// </summary>
    /// <param name="aUserId">The user id every record is attributed to.</param>
    /// <param name="aRepo">The <c>owner/name</c> the records belong to.</param>
    /// <param name="aFramework">The provenance axis the repository sits on.</param>
    /// <param name="aDirectory">Directory holding the fixture <c>*.jsonl</c> files.</param>
    /// <returns>The same store, for chaining.</returns>
    /// <exception cref="DirectoryNotFoundException">The fixture directory does not exist.</exception>
    public FixtureTelemetryStore Load(int aUserId, string aRepo, string aFramework, string aDirectory)
    {
        if (!Directory.Exists(aDirectory))
        {
            throw new DirectoryNotFoundException($"No fixture directory at {aDirectory}.");
        }

        RegisterRepo(aUserId, aRepo, aFramework);

        foreach (var vLine in Lines(aDirectory, StreamNames.Gates))
        {
            objGates.Add(ToGate(aUserId, aRepo, vLine));
        }

        foreach (var vLine in Lines(aDirectory, StreamNames.Runs))
        {
            objRuns.Add(ToRun(aUserId, aRepo, vLine));
        }

        foreach (var vLine in Lines(aDirectory, StreamNames.Sessions))
        {
            objSessions.Add(ToSession(aUserId, aRepo, vLine));
        }

        foreach (var vLine in Lines(aDirectory, StreamNames.Commits))
        {
            objCommits.Add(ToCommit(aUserId, aRepo, vLine));
        }

        return this;
    }

    /// <summary>
    /// Seeds records built in memory, for the rule tests that state only the fields they are about.
    /// </summary>
    /// <param name="aUserId">The user id the records are attributed to.</param>
    /// <param name="aRepo">The <c>owner/name</c> the records belong to.</param>
    /// <param name="aFramework">The provenance axis the repository sits on.</param>
    /// <param name="aGates">Gate records to serve.</param>
    /// <param name="aRuns">Run records to serve.</param>
    /// <param name="aSessions">Session records to serve.</param>
    /// <param name="aCommits">Commit records to serve.</param>
    /// <returns>The same store, for chaining.</returns>
    public FixtureTelemetryStore Seed(
        int aUserId,
        string aRepo,
        string aFramework,
        IEnumerable<GateRecord>? aGates = null,
        IEnumerable<RunRecord>? aRuns = null,
        IEnumerable<SessionRecord>? aSessions = null,
        IEnumerable<CommitRecord>? aCommits = null)
    {
        RegisterRepo(aUserId, aRepo, aFramework);
        objGates.AddRange(aGates ?? []);
        objRuns.AddRange(aRuns ?? []);
        objSessions.AddRange(aSessions ?? []);
        objCommits.AddRange(aCommits ?? []);
        return this;
    }

    /// <summary>
    /// Seeds Playbook <c>"PbEvent"</c> records, for the tests on that axis.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate method from <see cref="Seed"/>: a Playbook record and a TechieFlow record
    /// never arrive through the same door, so no test can feed one where the other is expected
    /// (SCHEMA.md §11, REQ-FN-066).
    /// </remarks>
    /// <param name="aUserId">The user id the records are attributed to.</param>
    /// <param name="aRepo">The <c>owner/name</c> the records belong to.</param>
    /// <param name="aEvents">The event records to serve.</param>
    /// <returns>The same store, for chaining.</returns>
    public FixtureTelemetryStore SeedPbEvents(int aUserId, string aRepo, IEnumerable<PbEventRecord> aEvents)
    {
        RegisterRepo(aUserId, aRepo, FrameworkNames.Playbook);
        objPbEvents.AddRange(aEvents);
        return this;
    }

    /// <summary>Records how many times the engine has read the gate stream, so memoisation is observable.</summary>
    public int GateReads { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<GateRecord>> ReadGatesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default)
    {
        GateReads++;
        return Task.FromResult<IReadOnlyList<GateRecord>>(
            objGates.Where(aRecord => aRecord.UserId == aUserId && Matches(aRecord.Repo, aFramework, aRepo)).ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunRecord>> ReadRunsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RunRecord>>(
            objRuns.Where(aRecord => aRecord.UserId == aUserId && Matches(aRecord.Repo, aFramework, aRepo)).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> ReadSessionsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SessionRecord>>(
            objSessions.Where(aRecord => aRecord.UserId == aUserId && Matches(aRecord.Repo, aFramework, aRepo)).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<CommitRecord>> ReadCommitsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CommitRecord>>(
            objCommits.Where(aRecord => aRecord.UserId == aUserId && Matches(aRecord.Repo, aFramework, aRepo)).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<PbEventRecord>> ReadPbEventsAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PbEventRecord>>(
            objPbEvents
                .Where(aRecord => aRecord.UserId == aUserId
                    && (aRepo is null || string.Equals(aRecord.Repo, aRepo, StringComparison.Ordinal)))
                .ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadUserReposAsync(int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserRepo>>(objRepos.Where(aRepo => aRepo.UserId == aUserId).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncState>> ReadSyncStateAsync(int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SyncState>>(
            objRepos.Where(aRepo => aRepo.UserId == aUserId)
                .Select(aRepo => new SyncState
                {
                    UserId = aUserId,
                    Repo = aRepo.Repo,
                    LastSha = FixtureSha,
                    SessionDuplicatesCollapsed =
                        objSessionCollapses.GetValueOrDefault((aUserId, aRepo.Repo))
                })
                .ToList());

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken aCancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken aCancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<int> UpsertAsync(ParseResult aParsed, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task WriteSyncStateAsync(SyncState aState, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadAllUserReposAsync(CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task WriteUserRepoAsync(UserRepo aRepo, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task DeleteRepoDataAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<RebuildReport> RebuildAsync(int? aUserId = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>Registers a repository the records belong to, unless it is already known.</summary>
    /// <param name="aUserId">The user id.</param>
    /// <param name="aRepo">The <c>owner/name</c>.</param>
    /// <param name="aFramework">The provenance axis.</param>
    private void RegisterRepo(int aUserId, string aRepo, string aFramework)
    {
        if (objRepos.Any(aExisting => aExisting.UserId == aUserId && aExisting.Repo == aRepo))
        {
            return;
        }

        objRepos.Add(new UserRepo
        {
            UserId = aUserId,
            Repo = aRepo,
            Owner = aRepo.Split('/')[0],
            Name = aRepo.Split('/')[^1],
            Branch = "main",
            Kind = aFramework,
            Framework = aFramework,
            ConnectedTs = "2026-08-01T00:00:00Z"
        });
    }

    /// <summary>Tests whether a record's repository belongs to the requested framework and filter.</summary>
    /// <param name="aRecordRepo">The record's repository.</param>
    /// <param name="aFramework">The framework being read.</param>
    /// <param name="aRepoFilter">A single repository, or <c>null</c> for all.</param>
    /// <returns><c>true</c> when the record should be returned.</returns>
    private bool Matches(string aRecordRepo, string aFramework, string? aRepoFilter)
    {
        if (aRepoFilter is not null && aRepoFilter != aRecordRepo)
        {
            return false;
        }

        return objRepos.Any(aRepo => aRepo.Repo == aRecordRepo && aRepo.Framework == aFramework);
    }

    /// <summary>Reads one stream file's JSON lines, skipping blanks as the reference does.</summary>
    /// <param name="aDirectory">The fixture directory.</param>
    /// <param name="aStream">The stream's wire name.</param>
    /// <returns>The parsed root elements, or nothing when the stream file is absent.</returns>
    private static IEnumerable<JsonElement> Lines(string aDirectory, string aStream)
    {
        var vPath = Path.Combine(aDirectory, aStream + ".jsonl");
        if (!File.Exists(vPath))
        {
            yield break;
        }

        foreach (var vLine in File.ReadAllLines(vPath))
        {
            if (!string.IsNullOrWhiteSpace(vLine))
            {
                yield return JsonDocument.Parse(vLine).RootElement.Clone();
            }
        }
    }

    /// <summary>Maps a gates.jsonl line onto the stored record shape.</summary>
    /// <param name="aUserId">The user id.</param>
    /// <param name="aRepo">The repository.</param>
    /// <param name="aLine">The parsed line.</param>
    /// <returns>The gate record.</returns>
    private static GateRecord ToGate(int aUserId, string aRepo, JsonElement aLine) => new()
    {
        UserId = aUserId,
        Repo = aRepo,
        SourceSha = FixtureSha,
        Ts = Text(aLine, "ts") ?? string.Empty,
        App = Text(aLine, "app"),
        ProjectType = Text(aLine, "project_type"),
        ProjectTypeInferred = Flag(aLine, "project_type_inferred"),
        Backfilled = Flag(aLine, "backfilled"),
        Inferred = Raw(aLine, "inferred"),
        RunId = Text(aLine, "run_id"),
        ReqId = Text(aLine, "req_id"),
        ReqClass = Text(aLine, "req_class"),
        Attempt = Number(aLine, "attempt"),
        Verdict = Text(aLine, "verdict"),
        Gate = Text(aLine, "gate"),
        GatesRun = Raw(aLine, "gates_run"),
        FailureClass = Text(aLine, "failure_class"),
        PriorVerdict = Text(aLine, "prior_verdict")
    };

    /// <summary>Maps a runs.jsonl line onto the stored record shape.</summary>
    /// <param name="aUserId">The user id.</param>
    /// <param name="aRepo">The repository.</param>
    /// <param name="aLine">The parsed line.</param>
    /// <returns>The run record.</returns>
    private static RunRecord ToRun(int aUserId, string aRepo, JsonElement aLine) => new()
    {
        UserId = aUserId,
        Repo = aRepo,
        SourceSha = FixtureSha,
        Ts = Text(aLine, "ts") ?? string.Empty,
        App = Text(aLine, "app"),
        ProjectType = Text(aLine, "project_type"),
        ProjectTypeInferred = Flag(aLine, "project_type_inferred"),
        Backfilled = Flag(aLine, "backfilled"),
        Harness = Text(aLine, "harness"),
        Cmd = Text(aLine, "cmd"),
        Mode = Text(aLine, "mode"),
        Started = Text(aLine, "started"),
        Ended = Text(aLine, "ended"),
        DurationS = Number(aLine, "duration_s"),
        ReqsCount = Number(aLine, "reqs_count"),
        FilesWritten = Number(aLine, "files_written"),
        BuildResult = Text(aLine, "build_result")
    };

    /// <summary>Maps a sessions.jsonl line onto the stored record shape.</summary>
    /// <param name="aUserId">The user id.</param>
    /// <param name="aRepo">The repository.</param>
    /// <param name="aLine">The parsed line.</param>
    /// <returns>The session record.</returns>
    private static SessionRecord ToSession(int aUserId, string aRepo, JsonElement aLine) => new()
    {
        UserId = aUserId,
        Repo = aRepo,
        SourceSha = FixtureSha,
        Ts = Text(aLine, "ts") ?? string.Empty,
        App = Text(aLine, "app"),
        ProjectType = Text(aLine, "project_type"),
        Harness = Text(aLine, "harness"),
        SessionId = Text(aLine, "session_id") ?? string.Empty,
        Model = Text(aLine, "model"),
        DurationS = Number(aLine, "duration_s"),
        InputTokens = Number(aLine, "input_tokens"),
        OutputTokens = Number(aLine, "output_tokens"),
        CacheReadTokens = Number(aLine, "cache_read_tokens"),
        CacheCreationTokens = Number(aLine, "cache_creation_tokens")
    };

    /// <summary>Maps a commits.jsonl line onto the stored record shape.</summary>
    /// <param name="aUserId">The user id.</param>
    /// <param name="aRepo">The repository.</param>
    /// <param name="aLine">The parsed line.</param>
    /// <returns>The commit record.</returns>
    private static CommitRecord ToCommit(int aUserId, string aRepo, JsonElement aLine) => new()
    {
        UserId = aUserId,
        Repo = aRepo,
        SourceSha = FixtureSha,
        Ts = Text(aLine, "ts") ?? string.Empty,
        App = Text(aLine, "app"),
        ProjectType = Text(aLine, "project_type"),
        Sha = Text(aLine, "sha") ?? string.Empty,
        Files = Number(aLine, "files"),
        Insertions = Number(aLine, "insertions"),
        Deletions = Number(aLine, "deletions"),
        SubjectPrefix = Text(aLine, "subject_prefix"),
        Branch = Text(aLine, "branch")
    };

    /// <summary>Reads a string property, treating JSON null as absent.</summary>
    /// <param name="aLine">The parsed line.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The string, or <c>null</c>.</returns>
    private static string? Text(JsonElement aLine, string aName) =>
        aLine.TryGetProperty(aName, out var vValue) && vValue.ValueKind == JsonValueKind.String
            ? vValue.GetString()
            : null;

    /// <summary>Reads an integer property, treating JSON null as absent.</summary>
    /// <param name="aLine">The parsed line.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The integer, or <c>null</c>.</returns>
    private static int? Number(JsonElement aLine, string aName) =>
        aLine.TryGetProperty(aName, out var vValue) && vValue.ValueKind == JsonValueKind.Number
            ? vValue.GetInt32()
            : null;

    /// <summary>Reads a boolean property, treating JSON null as absent.</summary>
    /// <param name="aLine">The parsed line.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The boolean, or <c>null</c>.</returns>
    private static bool? Flag(JsonElement aLine, string aName) =>
        aLine.TryGetProperty(aName, out var vValue) && vValue.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? vValue.GetBoolean()
            : null;

    /// <summary>Reads a property's raw JSON text, as the store keeps arrays and objects verbatim.</summary>
    /// <param name="aLine">The parsed line.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The raw JSON, or <c>null</c>.</returns>
    private static string? Raw(JsonElement aLine, string aName) =>
        aLine.TryGetProperty(aName, out var vValue) && vValue.ValueKind is JsonValueKind.Array or JsonValueKind.Object
            ? vValue.GetRawText()
            : null;
}
