using System.Text.Json;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Provenance;

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
    private readonly List<MissRecord> objMisses = [];
    private readonly List<MissFixRecord> objMissFixes = [];
    private readonly List<MissAmendRecord> objMissAmends = [];
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
    /// The provenance ledger this store's own ingest doors wrote as they took rows in.
    /// </summary>
    /// <remarks>
    /// The fixture equivalent of <c>"SourceProvenance"</c>. <see cref="Load"/>, <see cref="Seed"/>,
    /// <see cref="SeedPbEvents"/> and <see cref="SeedMisses"/> are this store's ingest paths, and each
    /// records the identity it obtained <b>as it writes</b>, exactly as the sync and the import do
    /// (REQ-NFR-019 clause 1). That is what makes <see cref="AuditProvenanceAsync"/> a real comparison
    /// rather than a rubber stamp: it reads the SHAs off the rows and asks this list about them, so a row
    /// that reached the tables without passing a door — the production failure of 2026-08-29 — has
    /// nothing behind it and is reported.
    /// </remarks>
    private readonly List<SourceProvenanceRecord> objObtained = [];

    private ProvenanceAuditReport? objProvenanceOverride;

    /// <summary>
    /// What this store answers when the export asks whether its rows have real provenance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading it runs the store's own audit over every user it holds; setting it <b>overrides</b> that
    /// audit, which is how a test stands in for the one thing an in-memory store has no way to do to
    /// itself — a write that bypassed the ingest door (REQ-NFR-019 clause 4).
    /// </para>
    /// <para>
    /// It used to default to <see cref="ProvenanceAuditReport.Unsupported"/> on the grounds that a
    /// fixture "is not in a position to declare itself clean". That was true of the first cut, which had
    /// no ledger — but the conclusion drawn from it was a fail-open, because an unauditable store then
    /// reached <c>QUOTABLE</c> unimpeded. Both halves are fixed together (2026-08-30): the refusal is now
    /// unconditional in <c>ParityRecord.EvaluateWithProvenance</c>, and this store earns a real answer
    /// instead of being exempted from the question. An <see cref="ProvenanceAuditReport.Unsupported"/>
    /// answer is now reached only by a store that genuinely has no audit — the
    /// <c>ITelemetryStore</c> default — which is exactly the case that must not publish a figure.
    /// </para>
    /// </remarks>
    public ProvenanceAuditReport Provenance
    {
        get => objProvenanceOverride ?? AuditOwnRows(null);
        set => objProvenanceOverride = value;
    }

    /// <inheritdoc />
    public Task<ProvenanceAuditReport> AuditProvenanceAsync(
        int? aUserId = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult(objProvenanceOverride ?? AuditOwnRows(aUserId));

    /// <inheritdoc />
    public Task RecordSourceProvenanceAsync(
        SourceProvenanceRecord aRecord, CancellationToken aCancellationToken = default)
    {
        RecordObtained(aRecord);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Compares the SHAs this store's rows carry against the ledger its ingest doors wrote.
    /// </summary>
    /// <param name="aUserId">One user, or <c>null</c> for every user the store holds.</param>
    /// <returns>The findings — supported, because this store really can answer the question.</returns>
    private ProvenanceAuditReport AuditOwnRows(int? aUserId)
    {
        var vStored = new List<StoredProvenance>();

        Collect(vStored, aUserId, "Gate", objGates.Select(aR => (aR.UserId, aR.Repo, aR.SourceSha)));
        Collect(vStored, aUserId, "Run", objRuns.Select(aR => (aR.UserId, aR.Repo, aR.SourceSha)));
        Collect(vStored, aUserId, "Session", objSessions.Select(aR => (aR.UserId, aR.Repo, aR.SourceSha)));
        Collect(vStored, aUserId, "Commit", objCommits.Select(aR => (aR.UserId, aR.Repo, aR.SourceSha)));
        Collect(vStored, aUserId, "PbEvent", objPbEvents.Select(aR => (aR.UserId, aR.Repo, aR.SourceSha)));
        Collect(vStored, aUserId, "Miss", objMisses.Select(aR => (aR.UserId, aR.Repo, aR.SourceSha)));
        Collect(vStored, aUserId, "MissFix", objMissFixes.Select(aR => (aR.UserId, aR.Repo, aR.SourceSha)));
        Collect(vStored, aUserId, "MissAmend", objMissAmends.Select(aR => (aR.UserId, aR.Repo, aR.SourceSha)));

        var vObtained = aUserId is null
            ? objObtained
            : objObtained.Where(aEntry => aEntry.UserId == aUserId).ToList();

        return ProvenanceAudit.Compare(vStored, vObtained);
    }

    /// <summary>Groups one stream's rows into the <c>(user, repo, SHA, table)</c> shape the audit reads.</summary>
    /// <param name="aInto">The list being built.</param>
    /// <param name="aUserId">One user, or <c>null</c> for every user.</param>
    /// <param name="aTable">The stream table's name.</param>
    /// <param name="aRows">Each row's user, repository and stored SHA.</param>
    private static void Collect(
        List<StoredProvenance> aInto,
        int? aUserId,
        string aTable,
        IEnumerable<(int UserId, string Repo, string SourceSha)> aRows)
    {
        var vGroups = aRows
            .Where(aRow => aUserId is null || aRow.UserId == aUserId)
            .GroupBy(aRow => (aRow.UserId, aRow.Repo, aRow.SourceSha));

        foreach (var vGroup in vGroups)
        {
            aInto.Add(new StoredProvenance(
                vGroup.Key.UserId, vGroup.Key.Repo, vGroup.Key.SourceSha, aTable, vGroup.Count()));
        }
    }

    /// <summary>Records that an ingest door obtained one identity, ignoring a repeat.</summary>
    /// <param name="aRecord">What was obtained.</param>
    private void RecordObtained(SourceProvenanceRecord aRecord)
    {
        if (string.IsNullOrWhiteSpace(aRecord.SourceSha))
        {
            return;
        }

        var vAlready = objObtained.Any(aEntry =>
            aEntry.UserId == aRecord.UserId
            && string.Equals(aEntry.Repo, aRecord.Repo, StringComparison.Ordinal)
            && string.Equals(aEntry.SourceSha, aRecord.SourceSha, StringComparison.OrdinalIgnoreCase));

        if (!vAlready)
        {
            objObtained.Add(aRecord);
        }
    }

    /// <summary>
    /// Takes records in through an ingest door, recording the provenance each one arrived with.
    /// </summary>
    /// <typeparam name="T">The stream record type.</typeparam>
    /// <param name="aRecords">The records handed to the door, or <c>null</c>.</param>
    /// <param name="aEntry">Reads the ledger entry a record's own fields state.</param>
    /// <returns>The materialised records, for the caller to store.</returns>
    private List<T> Ingest<T>(IEnumerable<T>? aRecords, Func<T, SourceProvenanceRecord> aEntry)
    {
        var vRecords = aRecords?.ToList() ?? [];

        foreach (var vRecord in vRecords)
        {
            RecordObtained(aEntry(vRecord));
        }

        return vRecords;
    }

    /// <summary>
    /// Puts one gate row into the tables <b>without</b> passing an ingest door, so no ledger entry
    /// stands behind it.
    /// </summary>
    /// <remarks>
    /// The in-memory stand-in for what actually happened on 2026-08-29: rows written straight into the
    /// store by raw SQL, bypassing the sync path entirely. Nothing else in the fixture can do this, and
    /// it exists only so <see cref="AuditProvenanceAsync"/> can be shown reporting a real finding rather
    /// than returning a report a test handed it.
    /// </remarks>
    /// <param name="aGate">The row to smuggle in.</param>
    /// <returns>The same store, for chaining.</returns>
    public FixtureTelemetryStore SmuggleGate(GateRecord aGate)
    {
        objGates.Add(aGate);
        return this;
    }

    /// <summary>Builds a ledger entry for one obtained identity.</summary>
    /// <param name="aUserId">The user the rows belong to.</param>
    /// <param name="aRepo">The <c>owner/name</c>.</param>
    /// <param name="aSourceSha">The identity the door obtained.</param>
    /// <returns>The entry.</returns>
    private static SourceProvenanceRecord Entry(int aUserId, string aRepo, string aSourceSha) =>
        new(aUserId, aRepo, aSourceSha, ProvenanceKinds.Import, "2026-08-01T00:00:00Z");

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
    /// Sets how a repository's data arrived — <c>api</c> or <c>import</c> (BRD-132, REQ-FN-087).
    /// </summary>
    /// <remarks>
    /// Origin is a property of <b>delivery</b>. It is stored on <c>"UserRepo"</c>, no stream table
    /// carries it, and nothing on the figure path reads it, so setting it here must change the export's
    /// <c>source_kind</c> key and <b>no figure at all</b> — which is what
    /// <c>SourceKindIsShownAndNeverSegmentedTests</c> uses this for.
    /// </remarks>
    /// <param name="aUserId">The user the repository belongs to.</param>
    /// <param name="aRepo">The <c>owner/name</c>.</param>
    /// <param name="aSourceKind">The stored source kind.</param>
    /// <returns>The same store, for chaining.</returns>
    public FixtureTelemetryStore WithSourceKind(int aUserId, string aRepo, string aSourceKind)
    {
        for (var vIndex = 0; vIndex < objRepos.Count; vIndex++)
        {
            if (objRepos[vIndex].UserId == aUserId && objRepos[vIndex].Repo == aRepo)
            {
                objRepos[vIndex] = objRepos[vIndex] with { SourceKind = aSourceKind };
            }
        }

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

        // This door stamps FixtureSha onto every row it maps, so this is the identity it obtained —
        // recorded here, before the rows exist, exactly as the sync records the SHA it fetched at.
        RecordObtained(Entry(aUserId, aRepo, FixtureSha));

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
        objGates.AddRange(Ingest(aGates, aR => Entry(aR.UserId, aR.Repo, aR.SourceSha)));
        objRuns.AddRange(Ingest(aRuns, aR => Entry(aR.UserId, aR.Repo, aR.SourceSha)));
        objSessions.AddRange(Ingest(aSessions, aR => Entry(aR.UserId, aR.Repo, aR.SourceSha)));
        objCommits.AddRange(Ingest(aCommits, aR => Entry(aR.UserId, aR.Repo, aR.SourceSha)));
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
        objPbEvents.AddRange(Ingest(aEvents, aR => Entry(aR.UserId, aR.Repo, aR.SourceSha)));
        return this;
    }

    /// <summary>
    /// Seeds the three <c>misses.jsonl</c> record kinds, for the miss figure tests (REQ-FN-077).
    /// </summary>
    /// <remarks>
    /// The rows are served exactly as stored — <b>amendments are not folded here</b>, because folding is
    /// the engine's read-time job and a fixture that pre-folded them would prove nothing (ADR-020).
    /// </remarks>
    /// <param name="aUserId">The user id the records are attributed to.</param>
    /// <param name="aRepo">The <c>owner/name</c> the records belong to.</param>
    /// <param name="aFramework">The provenance axis the repository sits on.</param>
    /// <param name="aMisses">The <c>miss</c> records to serve.</param>
    /// <param name="aFixes">The <c>miss-fix</c> records to serve.</param>
    /// <param name="aAmends">The <c>miss-amend</c> records to serve.</param>
    /// <returns>The same store, for chaining.</returns>
    public FixtureTelemetryStore SeedMisses(
        int aUserId,
        string aRepo,
        string aFramework,
        IEnumerable<MissRecord>? aMisses = null,
        IEnumerable<MissFixRecord>? aFixes = null,
        IEnumerable<MissAmendRecord>? aAmends = null)
    {
        RegisterRepo(aUserId, aRepo, aFramework);
        objMisses.AddRange(Ingest(aMisses, aR => Entry(aR.UserId, aR.Repo, aR.SourceSha)));
        objMissFixes.AddRange(Ingest(aFixes, aR => Entry(aR.UserId, aR.Repo, aR.SourceSha)));
        objMissAmends.AddRange(Ingest(aAmends, aR => Entry(aR.UserId, aR.Repo, aR.SourceSha)));
        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MissRecord>> ReadMissesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MissRecord>>(
            objMisses.Where(aRecord => aRecord.UserId == aUserId && Matches(aRecord.Repo, aFramework, aRepo)).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<MissFixRecord>> ReadMissFixesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MissFixRecord>>(
            objMissFixes.Where(aRecord => aRecord.UserId == aUserId && Matches(aRecord.Repo, aFramework, aRepo)).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<MissAmendRecord>> ReadMissAmendsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MissAmendRecord>>(
            objMissAmends.Where(aRecord => aRecord.UserId == aUserId && Matches(aRecord.Repo, aFramework, aRepo)).ToList());

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
