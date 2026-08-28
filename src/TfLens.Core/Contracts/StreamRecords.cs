namespace TfLens.Core.Contracts;

/// <summary>
/// The five TechieFlow telemetry streams plus the Playbook stream, as recognised by TfLens.
/// </summary>
/// <remarks>
/// The wire names (<c>runs</c>, <c>gates</c>, <c>sessions</c>, <c>commits</c>, <c>misses</c>,
/// <c>events</c>) are the JSONL file base names under a repository's telemetry path;
/// <see cref="StreamNames"/> holds them. <see cref="Misses"/> is the one stream whose records do not
/// all share a shape: it carries three record kinds on one file (ADR-018, SCHEMA.md §5.5), so
/// <see cref="StreamKind"/> is no longer 1:1 with a table.
/// </remarks>
public enum StreamKind
{
    /// <summary>docs/metrics/runs.jsonl — one record per phase-task run.</summary>
    Runs = 0,

    /// <summary>docs/metrics/gates.jsonl — one record per gate verdict on a REQ.</summary>
    Gates = 1,

    /// <summary>docs/metrics/sessions.jsonl — one record per harness session.</summary>
    Sessions = 2,

    /// <summary>docs/metrics/commits.jsonl — one record per commit.</summary>
    Commits = 3,

    /// <summary>verification/telemetry/events.ndjson — the AI-First-Playbook stream (Phase 3).</summary>
    Events = 4,

    /// <summary>
    /// docs/metrics/misses.jsonl — what was missed, who missed it, what the fix cost (SCHEMA.md §5.5).
    /// </summary>
    /// <remarks>
    /// The only stream carrying three record kinds on one file: <c>miss</c>, <c>miss-fix</c> and
    /// <c>miss-amend</c>. They land in three tables, dispatched on each record's own <c>kind</c>
    /// (ADR-018, REQ-FN-072).
    /// </remarks>
    Misses = 5
}

/// <summary>
/// The <c>kind</c> values <c>misses.jsonl</c> declares (SCHEMA.md §5.5).
/// </summary>
/// <remarks>
/// Every other stream file declares exactly one kind, so "matches the file" and "is declared by the
/// file" are the same rule there. <c>misses.jsonl</c> declares three, which is why the parser
/// dispatches on the record's own <c>kind</c> inside <see cref="StreamKind.Misses"/>; anything else is
/// counted as an invalid line and skipped, never thrown (REQ-FN-072).
/// </remarks>
public static class MissKinds
{
    /// <summary>A miss was opened — maps to <see cref="MissRecord"/>.</summary>
    public const string Miss = "miss";

    /// <summary>A repair run closed (or moved) a miss — maps to <see cref="MissFixRecord"/>.</summary>
    public const string MissFix = "miss-fix";

    /// <summary>An append-only completion of a field the miss left <c>null</c> — maps to <see cref="MissAmendRecord"/>.</summary>
    public const string MissAmend = "miss-amend";
}

/// <summary>
/// The wire names of the streams, and the canonical ordering TfLens reports them in.
/// </summary>
/// <remarks>
/// These strings appear in raw archive file names, in <c>SyncState</c> rows and on the Coverage page,
/// so they are fixed here rather than derived from <see cref="StreamKind"/> names.
/// </remarks>
public static class StreamNames
{
    /// <summary>Wire name of <see cref="StreamKind.Runs"/>.</summary>
    public const string Runs = "runs";

    /// <summary>Wire name of <see cref="StreamKind.Gates"/>.</summary>
    public const string Gates = "gates";

    /// <summary>Wire name of <see cref="StreamKind.Sessions"/>.</summary>
    public const string Sessions = "sessions";

    /// <summary>Wire name of <see cref="StreamKind.Commits"/>.</summary>
    public const string Commits = "commits";

    /// <summary>Wire name of <see cref="StreamKind.Events"/> (Playbook).</summary>
    public const string Events = "events";

    /// <summary>Wire name of <see cref="StreamKind.Misses"/> (added 2026-08-28, BRD-112).</summary>
    public const string Misses = "misses";

    /// <summary>
    /// The five TechieFlow streams in report order; <c>misses</c> is appended, so it reports last.
    /// </summary>
    /// <remarks>
    /// The sync loop, the raw-archive replay and the Coverage stream table all read this list, so
    /// appending the name here is what makes the fifth stream fetched, archived and reported. A
    /// repository that does not emit <c>misses.jsonl</c> simply answers 404 and stores zero rows —
    /// no coordination window is needed in either deploy order (BRD-112, REQ-FN-071).
    /// </remarks>
    public static readonly IReadOnlyList<string> TechieFlow = [Runs, Gates, Sessions, Commits, Misses];

    /// <summary>The Playbook streams in report order.</summary>
    public static readonly IReadOnlyList<string> Playbook = [Events];

    /// <summary>
    /// Maps a wire name to its <see cref="StreamKind"/>.
    /// </summary>
    /// <param name="aName">The wire name, e.g. <c>gates</c>.</param>
    /// <returns>The matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The name is not a known stream.</exception>
    public static StreamKind ToKind(string aName) => aName switch
    {
        Runs => StreamKind.Runs,
        Gates => StreamKind.Gates,
        Sessions => StreamKind.Sessions,
        Commits => StreamKind.Commits,
        Misses => StreamKind.Misses,
        Events => StreamKind.Events,
        _ => throw new ArgumentOutOfRangeException(nameof(aName), aName, "Unknown stream name.")
    };

    /// <summary>
    /// Maps a <see cref="StreamKind"/> to its wire name.
    /// </summary>
    /// <param name="aKind">The stream kind.</param>
    /// <returns>The wire name used in file names and <c>SyncState</c>.</returns>
    public static string ToName(StreamKind aKind) => aKind switch
    {
        StreamKind.Runs => Runs,
        StreamKind.Gates => Gates,
        StreamKind.Sessions => Sessions,
        StreamKind.Commits => Commits,
        StreamKind.Misses => Misses,
        StreamKind.Events => Events,
        _ => throw new ArgumentOutOfRangeException(nameof(aKind), aKind, "Unknown stream kind.")
    };
}

/// <summary>
/// One <c>runs.jsonl</c> record, stored in the <c>"Run"</c> table.
/// </summary>
/// <remarks>
/// Column names are the SCHEMA.md field names in PascalCase (<c>req_id</c> → <c>ReqId</c>). Absent
/// optional fields stay <c>null</c> and are never coerced to zero (SCHEMA.md §2.5). Any property the
/// parser does not recognise — and every record whose <see cref="V"/> is greater than 1 — lands in
/// <see cref="Overflow"/> verbatim so a rebuild loses nothing.
/// </remarks>
public sealed record RunRecord
{
    /// <summary>AppManager user who connected the repository this record came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Commit SHA the raw file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>Schema version carried by the record (<c>v</c>); 1 is the only version TfLens maps to columns.</summary>
    public int V { get; init; } = 1;

    /// <summary>ISO-8601 timestamp of the run.</summary>
    public required string Ts { get; init; }

    /// <summary>Application the run belongs to.</summary>
    public string? App { get; init; }

    /// <summary>Declared or inferred project type (<c>app</c> | <c>library</c> | <c>docs</c> | <c>framework</c>).</summary>
    public string? ProjectType { get; init; }

    /// <summary>True when <c>project_type</c> was inferred rather than declared; such records segment as <c>unclassified</c>.</summary>
    public bool? ProjectTypeInferred { get; init; }

    /// <summary>True when the record was backfilled rather than emitted live; backfilled figures never pool with live ones.</summary>
    public bool? Backfilled { get; init; }

    /// <summary>Detected harness (<c>claude-code</c> | <c>opencode</c> | <c>codex</c>); <c>null</c> means not detected.</summary>
    public string? Harness { get; init; }

    /// <summary>The phase command that ran, e.g. <c>build-phase</c>.</summary>
    public string? Cmd { get; init; }

    /// <summary>Run mode — <c>build</c> for a fresh pass, <c>fix</c> for re-entry over failing rows.</summary>
    public string? Mode { get; init; }

    /// <summary>ISO-8601 start timestamp.</summary>
    public string? Started { get; init; }

    /// <summary>ISO-8601 end timestamp.</summary>
    public string? Ended { get; init; }

    /// <summary>Wall-clock duration in seconds.</summary>
    public int? DurationS { get; init; }

    /// <summary>JSON array of REQ IDs touched, stored verbatim.</summary>
    public string? ReqsTouched { get; init; }

    /// <summary>Count of REQs touched.</summary>
    public int? ReqsCount { get; init; }

    /// <summary>JSON array of sub-agent names fanned out to.</summary>
    public string? Subagents { get; init; }

    /// <summary>Number of files written during the run.</summary>
    public int? FilesWritten { get; init; }

    /// <summary>Build outcome — <c>pass</c> | <c>fail</c> | <c>not-run</c>.</summary>
    public string? BuildResult { get; init; }

    /// <summary>Routing tier requested.</summary>
    public string? Tier { get; init; }

    /// <summary>Model the tier was expected to resolve to.</summary>
    public string? TierModel { get; init; }

    /// <summary>Model actually observed.</summary>
    public string? Model { get; init; }

    /// <summary>JSON array of every model observed in the run.</summary>
    public string? Models { get; init; }

    /// <summary>False when the request was not routed through the tier — the routing-drift signal.</summary>
    public bool? Routed { get; init; }

    /// <summary>Input tokens.</summary>
    public int? TokensIn { get; init; }

    /// <summary>Output tokens.</summary>
    public int? TokensOut { get; init; }

    /// <summary>Cache-read tokens.</summary>
    public int? TokensCacheRead { get; init; }

    /// <summary>Cache-write tokens.</summary>
    public int? TokensCacheWrite { get; init; }

    /// <summary>Measured spend in USD. Only ever non-null for <c>opencode</c>; never summed across harnesses.</summary>
    public decimal? CostUsd { get; init; }

    /// <summary>Scope the token counts cover; <c>none</c> excludes the run from repricing.</summary>
    public string? TokensScope { get; init; }

    /// <summary>Attempt number; first-pass rate counts only <c>attempt == 1</c>.</summary>
    public int? Attempt { get; init; }

    /// <summary>JSON object of properties SCHEMA.md does not document, preserved for rebuild fidelity.</summary>
    public string? Overflow { get; init; }
}

/// <summary>
/// One <c>gates.jsonl</c> record, stored in the <c>"Gate"</c> table.
/// </summary>
/// <remarks>
/// Gate records carry the verdicts the three questions are computed from. A <c>gate</c> value of
/// <c>escaped</c> means no gate caught the defect and is reported as its own row, never merged into a
/// catch bucket.
/// </remarks>
public sealed record GateRecord
{
    /// <summary>AppManager user who connected the repository this record came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Commit SHA the raw file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>Schema version carried by the record.</summary>
    public int V { get; init; } = 1;

    /// <summary>ISO-8601 timestamp of the gate verdict.</summary>
    public required string Ts { get; init; }

    /// <summary>Application the record belongs to.</summary>
    public string? App { get; init; }

    /// <summary>Declared or inferred project type.</summary>
    public string? ProjectType { get; init; }

    /// <summary>True when <c>project_type</c> was inferred rather than declared.</summary>
    public bool? ProjectTypeInferred { get; init; }

    /// <summary>True when the record was backfilled rather than emitted live.</summary>
    public bool? Backfilled { get; init; }

    /// <summary>Free-form marker for values the emitter inferred.</summary>
    public string? Inferred { get; init; }

    /// <summary>Detected harness; <c>null</c> means not detected.</summary>
    public string? Harness { get; init; }

    /// <summary>Identifier of the run this verdict belongs to.</summary>
    public string? RunId { get; init; }

    /// <summary>The requirement the verdict is about, e.g. <c>REQ-UI-001</c>.</summary>
    public string? ReqId { get; init; }

    /// <summary>Requirement class — <c>UI</c> | <c>FN</c> | <c>RAG</c> | <c>NFR</c>.</summary>
    public string? ReqClass { get; init; }

    /// <summary>Attempt number for this REQ.</summary>
    public int? Attempt { get; init; }

    /// <summary>Verdict, e.g. <c>Verified</c>, <c>FAIL</c>, <c>PARTIAL</c>.</summary>
    public string? Verdict { get; init; }

    /// <summary>Which gate produced the verdict; <c>escaped</c> when none did.</summary>
    public string? Gate { get; init; }

    /// <summary>JSON array of the gates that ran, used for late-gate coverage.</summary>
    public string? GatesRun { get; init; }

    /// <summary>Classification of the failure.</summary>
    public string? FailureClass { get; init; }

    /// <summary>The verdict this one superseded.</summary>
    public string? PriorVerdict { get; init; }

    /// <summary>Evidence reference for the verdict.</summary>
    public string? Proof { get; init; }

    /// <summary>JSON object of properties SCHEMA.md does not document.</summary>
    public string? Overflow { get; init; }
}

/// <summary>
/// One <c>sessions.jsonl</c> record, stored in the <c>"Session"</c> table.
/// </summary>
/// <remarks>
/// Sessions are deduped in the parser by <c>SessionId</c>, keeping the highest
/// <see cref="OutputTokens"/> and, on a tie, the latest <see cref="Ts"/> — a session record is
/// rewritten as the session grows, so the largest one is the complete one.
/// </remarks>
public sealed record SessionRecord
{
    /// <summary>AppManager user who connected the repository this record came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Commit SHA the raw file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>Schema version carried by the record.</summary>
    public int V { get; init; } = 1;

    /// <summary>ISO-8601 timestamp of the session record.</summary>
    public required string Ts { get; init; }

    /// <summary>Application the session belongs to.</summary>
    public string? App { get; init; }

    /// <summary>Declared or inferred project type.</summary>
    public string? ProjectType { get; init; }

    /// <summary>Detected harness; <c>null</c> means not detected.</summary>
    public string? Harness { get; init; }

    /// <summary>Harness session identifier — the dedupe key.</summary>
    public required string SessionId { get; init; }

    /// <summary>Model used for the session.</summary>
    public string? Model { get; init; }

    /// <summary>Wall-clock duration in seconds.</summary>
    public int? DurationS { get; init; }

    /// <summary>Input tokens.</summary>
    public int? InputTokens { get; init; }

    /// <summary>Output tokens — the dedupe tie-breaker.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>Cache-read tokens.</summary>
    public int? CacheReadTokens { get; init; }

    /// <summary>Cache-creation tokens.</summary>
    public int? CacheCreationTokens { get; init; }

    /// <summary>Measured spend in USD; only ever non-null for <c>opencode</c>.</summary>
    public decimal? CostUsd { get; init; }

    /// <summary>JSON object of properties SCHEMA.md does not document.</summary>
    public string? Overflow { get; init; }
}

/// <summary>
/// One <c>commits.jsonl</c> record, stored in the <c>"Commit"</c> table.
/// </summary>
/// <remarks>Deduped on <see cref="Sha"/> per user and repository — two repositories may share a short SHA.</remarks>
public sealed record CommitRecord
{
    /// <summary>AppManager user who connected the repository this record came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Commit SHA the raw file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>Schema version carried by the record.</summary>
    public int V { get; init; } = 1;

    /// <summary>ISO-8601 timestamp of the commit.</summary>
    public required string Ts { get; init; }

    /// <summary>Application the commit belongs to.</summary>
    public string? App { get; init; }

    /// <summary>Declared or inferred project type.</summary>
    public string? ProjectType { get; init; }

    /// <summary>The commit SHA — the dedupe key within a repository.</summary>
    public required string Sha { get; init; }

    /// <summary>Files changed.</summary>
    public int? Files { get; init; }

    /// <summary>Lines inserted.</summary>
    public int? Insertions { get; init; }

    /// <summary>Lines deleted.</summary>
    public int? Deletions { get; init; }

    /// <summary>Conventional-commit prefix of the subject line.</summary>
    public string? SubjectPrefix { get; init; }

    /// <summary>Branch the commit landed on.</summary>
    public string? Branch { get; init; }

    /// <summary>JSON object of properties SCHEMA.md does not document.</summary>
    public string? Overflow { get; init; }
}

/// <summary>
/// One <c>misses.jsonl</c> record of kind <c>miss</c>, stored in the <c>"Miss"</c> table (SCHEMA.md §5.5.1).
/// </summary>
/// <remarks>
/// <para>
/// A miss is opened once and closed by one or more <see cref="MissFixRecord"/>s linked on
/// <see cref="MissId"/>; a <see cref="MissAmendRecord"/> may later complete a field this record left
/// <c>null</c>. Deduped on <c>(UserId, Repo, MissId)</c> keeping the <b>earliest</b> <see cref="Ts"/> —
/// a duplicate is a re-parse of the same archived file, not new information (REQ-FN-073).
/// </para>
/// <para>
/// <b>Every nullable here means "not captured", never zero and never a bucket.</b>
/// <see cref="WhyMissed"/> in particular is <c>null</c> for <i>not assessed</i>, so the failed-practice
/// distribution's denominator is the records that carry it — never the miss count (SCHEMA.md §5.5.6).
/// <see cref="ReqId"/> is <c>null</c> when no REQ existed to miss, which is itself the finding.
/// </para>
/// <para>
/// <see cref="OriginConfidence"/>, <see cref="OriginModel"/> and <see cref="OriginHarness"/> are
/// derived by <c>tf-emit.sh</c> and never written by an agent; a record whose lookup failed carries
/// <c>null</c> model and harness, so a non-<c>linked</c> record cannot carry a model name at all
/// (SCHEMA.md §5.5.1, §6).
/// </para>
/// </remarks>
public sealed record MissRecord
{
    /// <summary>AppManager user who connected the repository this record came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Commit SHA the raw file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>Schema version carried by the record.</summary>
    public int V { get; init; } = 1;

    /// <summary>ISO-8601 timestamp the miss was recorded.</summary>
    public required string Ts { get; init; }

    /// <summary>Application the miss belongs to.</summary>
    public string? App { get; init; }

    /// <summary>Declared or inferred project type; figures never pool across it (SCHEMA.md §6).</summary>
    public string? ProjectType { get; init; }

    /// <summary>True when <c>project_type</c> was inferred rather than declared.</summary>
    public bool? ProjectTypeInferred { get; init; }

    /// <summary>True when the record was backfilled rather than emitted live.</summary>
    public bool? Backfilled { get; init; }

    /// <summary>Detected harness; <c>null</c> means not detected.</summary>
    public string? Harness { get; init; }

    /// <summary><c>MISS-&lt;app&gt;-&lt;YYYYMMDD&gt;-&lt;NN&gt;</c> — the link key and the dedupe key.</summary>
    public required string MissId { get; init; }

    /// <summary>The owning REQ; <c>null</c> is meaningful — no REQ existed to miss.</summary>
    public string? ReqId { get; init; }

    /// <summary>Requirement class — <c>UI</c> | <c>FN</c> | <c>RAG</c> | <c>NFR</c>.</summary>
    public string? ReqClass { get; init; }

    /// <summary>What was missed — the closed vocabulary of SCHEMA.md §5.5.1.</summary>
    public string? MissClass { get; init; }

    /// <summary>Which artifact was deficient — <c>brd</c> · <c>src</c> · <c>tests</c> · … .</summary>
    public string? Artifact { get; init; }

    /// <summary>Owner-visible impact — <c>blocker</c> | <c>major</c> | <c>minor</c>; never an effort estimate.</summary>
    public string? Severity { get; init; }

    /// <summary>
    /// Which <i>practice</i> failed (SCHEMA.md §5.5.6); <c>null</c> means <b>not assessed</b>.
    /// </summary>
    /// <remarks>
    /// Never coerced to a bucket: a distribution rendered over all misses understates every category.
    /// The field is also subject to an eligibility floor — a miss written before 2026-08-28 had no
    /// field to fill and leaves the denominator entirely (REQ-FN-076). It is the one field a
    /// <see cref="MissAmendRecord"/> may complete.
    /// </remarks>
    public string? WhyMissed { get; init; }

    /// <summary>The <c>cmd</c> that should have produced the artifact correctly.</summary>
    public string? OriginPhase { get; init; }

    /// <summary>The agent persona that was running.</summary>
    public string? OriginAgent { get; init; }

    /// <summary>The <c>started</c> timestamp of the originating run, found in <c>runs.jsonl</c>; never guessed.</summary>
    public string? OriginRunId { get; init; }

    /// <summary>
    /// <c>linked</c> | <c>inferred</c> | <c>unknown</c> — derived by the emitter, never written by an agent.
    /// </summary>
    /// <remarks>A provenance boundary: only <c>linked</c> records reach a per-phase, per-model or per-agent figure.</remarks>
    public string? OriginConfidence { get; init; }

    /// <summary>Model of the originating run; forced to <c>null</c> whenever the emitter's lookup failed.</summary>
    public string? OriginModel { get; init; }

    /// <summary>Harness of the originating run; forced to <c>null</c> whenever the emitter's lookup failed.</summary>
    public string? OriginHarness { get; init; }

    /// <summary>Who found it — <c>gate</c> · <c>self-smoke</c> · <c>owner</c> · <c>production</c> · … .</summary>
    public string? FoundBy { get; init; }

    /// <summary>The <c>cmd</c> that was running when it surfaced.</summary>
    public string? FoundPhase { get; init; }

    /// <summary>Which gate caught it when <see cref="FoundBy"/> is <c>gate</c>; <c>null</c> otherwise.</summary>
    public string? FoundGate { get; init; }

    /// <summary><c>started</c> of the finding run.</summary>
    public string? FoundRunId { get; init; }

    /// <summary>The §3.3 failure-class vocabulary, reused verbatim; <c>null</c> where none applies.</summary>
    public string? FailureClass { get; init; }

    /// <summary>JSON object of properties SCHEMA.md does not document, preserved for rebuild fidelity.</summary>
    public string? Overflow { get; init; }
}

/// <summary>
/// One <c>misses.jsonl</c> record of kind <c>miss-fix</c>, stored in the <c>"MissFix"</c> table (SCHEMA.md §5.5.2).
/// </summary>
/// <remarks>
/// <para>
/// Deduped on <c>(UserId, Repo, MissId, FixRunId)</c> keeping the <b>latest</b> <see cref="Ts"/>
/// (REQ-FN-073). A record whose <see cref="MissId"/> matches no <see cref="MissRecord"/> is an
/// <b>orphan</b>: counted and surfaced on Coverage, never silently dropped.
/// </para>
/// <para>
/// <b><see cref="CostAttribution"/> is what the money number stands on.</b> A fix run that repaired
/// three misses has one token window, so <c>shared:&lt;n&gt;</c> is an apportionment and never enters a
/// headline cost figure; <c>none</c> — which the deliberate <c>log-miss --fixed</c> path produces by
/// omitting <see cref="FixRunId"/> — is a count, never a divisor, and is correct data rather than
/// missing data (SCHEMA.md §5.5.3, §0.4).
/// </para>
/// </remarks>
public sealed record MissFixRecord
{
    /// <summary>AppManager user who connected the repository this record came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Commit SHA the raw file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>Schema version carried by the record.</summary>
    public int V { get; init; } = 1;

    /// <summary>ISO-8601 timestamp the fix record was written.</summary>
    public required string Ts { get; init; }

    /// <summary>Application the fix belongs to.</summary>
    public string? App { get; init; }

    /// <summary>Declared or inferred project type.</summary>
    public string? ProjectType { get; init; }

    /// <summary>True when <c>project_type</c> was inferred rather than declared.</summary>
    public bool? ProjectTypeInferred { get; init; }

    /// <summary>True when the record was backfilled rather than emitted live.</summary>
    public bool? Backfilled { get; init; }

    /// <summary>Detected harness; <c>null</c> means not detected.</summary>
    public string? Harness { get; init; }

    /// <summary>The <see cref="MissRecord.MissId"/> this fix belongs to — the link.</summary>
    public required string MissId { get; init; }

    /// <summary>The REQ, copied from the miss for readability.</summary>
    public string? ReqId { get; init; }

    /// <summary>
    /// <c>started</c> of the repair run — where the cost comes from; part of the dedupe key.
    /// </summary>
    /// <remarks>
    /// <c>null</c> is a deliberate emission, not a gap: <c>log-miss --fixed</c> omits it when the
    /// repairing run cannot be identified, which is exactly what makes the record cost <c>none</c>.
    /// </remarks>
    public string? FixRunId { get; init; }

    /// <summary>The command that ran the fix — <c>fix-issues</c> | <c>build-phase</c> | <c>triage-issues</c> | <c>amend-docs</c> | <c>log-miss</c>.</summary>
    public string? FixCmd { get; init; }

    /// <summary>One more than the count of prior fixes for this miss.</summary>
    public int? FixAttempt { get; init; }

    /// <summary><c>Verified</c> | <c>Needs re-verify</c> | <c>FAIL</c> | <c>deferred</c> | <c>wont-fix</c>.</summary>
    public string? VerdictAfter { get; init; }

    /// <summary>True when a closed miss was re-opened by a later escape.</summary>
    public bool? Reopened { get; init; }

    /// <summary><c>sole</c> | <c>shared:&lt;n&gt;</c> | <c>none</c> — derived by the emitter (SCHEMA.md §5.5.3).</summary>
    public string? CostAttribution { get; init; }

    /// <summary>Input tokens of the fix run's window.</summary>
    public int? TokensIn { get; init; }

    /// <summary>Output tokens of the fix run's window.</summary>
    public int? TokensOut { get; init; }

    /// <summary>Cache-read tokens of the fix run's window.</summary>
    public int? TokensCacheRead { get; init; }

    /// <summary>Cache-write tokens of the fix run's window.</summary>
    public int? TokensCacheWrite { get; init; }

    /// <summary>Measured spend in USD; only ever non-null for <c>opencode</c>, never summed across harnesses.</summary>
    public decimal? CostUsd { get; init; }

    /// <summary>Scope the token counts cover; <c>none</c> excludes the record from cost figures.</summary>
    public string? TokensScope { get; init; }

    /// <summary>Model that ran the fix.</summary>
    public string? Model { get; init; }

    /// <summary>JSON object of properties SCHEMA.md does not document.</summary>
    public string? Overflow { get; init; }
}

/// <summary>
/// One <c>misses.jsonl</c> record of kind <c>miss-amend</c>, stored in the <c>"MissAmend"</c> table
/// (SCHEMA.md §5.5.7, ADR-020).
/// </summary>
/// <remarks>
/// <para>
/// An amend <b>completes</b> a record; it never alters a fact. It may set a field that is currently
/// <c>null</c> and may never overwrite a non-<c>null</c> value — including one an earlier amend set.
/// That is what keeps the correction inside the append-only rule rather than an edit wearing a
/// record's clothes.
/// </para>
/// <para>
/// <b>Stored, never collapsed at ingest.</b> Folding is a read-time operation over these rows
/// (<c>MissAmendFolder</c>), so <c>RebuildAsync</c> re-derives identical values and the null-check is
/// re-applied by TfLens rather than trusted to the producer — a stream merged from several machines can
/// carry an amend and a later-written value in either order (ADR-020, REQ-FN-075). Deduped on
/// <c>(UserId, Repo, MissId, Field, Ts)</c> keeping the earliest.
/// </para>
/// </remarks>
public sealed record MissAmendRecord
{
    /// <summary>AppManager user who connected the repository this record came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Commit SHA the raw file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>Schema version carried by the record.</summary>
    public int V { get; init; } = 1;

    /// <summary>ISO-8601 timestamp the amendment was written; amendments fold oldest first.</summary>
    public required string Ts { get; init; }

    /// <summary>Application the amendment belongs to.</summary>
    public string? App { get; init; }

    /// <summary>Declared or inferred project type.</summary>
    public string? ProjectType { get; init; }

    /// <summary>True when <c>project_type</c> was inferred rather than declared.</summary>
    public bool? ProjectTypeInferred { get; init; }

    /// <summary>True when the record was backfilled rather than emitted live.</summary>
    public bool? Backfilled { get; init; }

    /// <summary>Detected harness; <c>null</c> means not detected.</summary>
    public string? Harness { get; init; }

    /// <summary>The miss this completes; an amend naming no known miss is an orphan, never applied.</summary>
    public required string MissId { get; init; }

    /// <summary>The wire field name being completed; must be on the allowlist or the amend is an orphan.</summary>
    public required string Field { get; init; }

    /// <summary>The value to set; must be inside that field's closed vocabulary or the amend is an orphan.</summary>
    public string? Value { get; init; }

    /// <summary>JSON object of properties SCHEMA.md does not document.</summary>
    public string? Overflow { get; init; }
}

/// <summary>
/// One AI-First-Playbook <c>events.ndjson</c> record, stored in the <c>"PbEvent"</c> table (Phase 3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Amended 2026-08-26 from the emitter source (REQ-FN-068, ADR-010).</b> The day-1 column set was a
/// guess made before any shape was known. It has been replaced with the shape the Playbook's own writer
/// emits — <c>harness/opencode/plugin/telemetry.ts</c> in <c>techierathore/AI-First-Playbook</c> — and
/// the field names, wire spellings and types are recorded in <c>DECISIONS.md</c> §Playbook. No captured
/// <c>events.ndjson</c> has been parsed, so value ranges remain unverified; see
/// <c>PlaybookSchemaStatus.EmitterSourceDerived</c>.
/// </para>
/// <para>
/// The wire spelling is camelCase with capitalised acronyms (<c>sessionID</c>, <c>parentID</c>,
/// <c>messageID</c>) — <b>not</b> the snake_case of the four TechieFlow streams — and
/// <c>tokens</c> is a nested object, which is why it lands in five columns rather than one.
/// </para>
/// <para>
/// Playbook process-gates (<see cref="PhaseGate"/>) and TechieFlow assertion-gates never share a table,
/// column or chart (SCHEMA.md §11).
/// </para>
/// </remarks>
public sealed record PbEventRecord
{
    /// <summary>AppManager user who connected the repository this record came from.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Commit SHA the raw file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>ISO-8601 timestamp of the event (wire <c>ts</c>); the emitter stamps every record.</summary>
    public required string Ts { get; init; }

    /// <summary>
    /// The record kind (wire <c>kind</c>) — <c>phase-start</c>, <c>turn</c> or <c>phase-end</c>.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Playbook process-gate — a different axis from a TechieFlow assertion gate (SCHEMA.md §11).
    /// </summary>
    /// <remarks>
    /// <b>Derived, not emitted.</b> <c>events.ndjson</c> carries the phase only as <c>command</c> on the
    /// <c>phase-start</c> record; the <c>turn</c> and <c>phase-end</c> records that follow belong to that
    /// phase by sequence. The parser therefore carries the enclosing <c>phase-start.command</c> onto
    /// every record, which is exactly how <c>scripts/playbook-telemetry.mjs</c> joins them.
    /// </remarks>
    public string? PhaseGate { get; init; }

    /// <summary>Arguments the phase command was invoked with (wire <c>arguments</c>, <c>phase-start</c> only).</summary>
    public string? Arguments { get; init; }

    /// <summary>Harness session identifier (wire <c>sessionID</c>).</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Parent session identifier (wire <c>parentID</c>): <c>null</c> on a main session, the parent's
    /// <c>sessionID</c> on a sub-agent session. The emitter leaves it <c>null</c> when it could not be
    /// learned, and the Playbook's own joiner treats a missing parent as main.
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// Assistant message identifier (wire <c>messageID</c>, <c>turn</c> only) — the dedupe key.
    /// </summary>
    /// <remarks>
    /// The emitter appends a fresh <c>turn</c> record on every <c>message.updated</c> event, so one
    /// message produces many rows as it streams and only the last carries its final token and cost
    /// counts. The parser collapses them on this id keeping the highest <see cref="TokensOutput"/> and,
    /// on a tie, the latest <see cref="Ts"/> — the same rule as <see cref="SessionRecord"/>. Summing the
    /// uncollapsed rows would multiply the token totals.
    /// </remarks>
    public string? MessageId { get; init; }

    /// <summary>Model that answered the turn (wire <c>model</c>), formatted <c>providerID/modelID</c>.</summary>
    public string? Model { get; init; }

    /// <summary>Input tokens (wire <c>tokens.input</c>).</summary>
    public int? TokensInput { get; init; }

    /// <summary>Output tokens (wire <c>tokens.output</c>).</summary>
    public int? TokensOutput { get; init; }

    /// <summary>Reasoning tokens (wire <c>tokens.reasoning</c>); the joiner counts these as output.</summary>
    public int? TokensReasoning { get; init; }

    /// <summary>Cache-read tokens (wire <c>tokens.cache.read</c>); the joiner counts these as input.</summary>
    public int? TokensCacheRead { get; init; }

    /// <summary>Cache-write tokens (wire <c>tokens.cache.write</c>); the joiner counts these as input.</summary>
    public int? TokensCacheWrite { get; init; }

    /// <summary>
    /// Measured spend in USD for the turn (wire <c>cost</c>).
    /// </summary>
    /// <remarks>
    /// The Playbook's telemetry plugin is OpenCode-only, so — as with the TechieFlow streams — these are
    /// the one place measured dollars exist, and they are never summed across harnesses.
    /// </remarks>
    public decimal? CostUsd { get; init; }

    /// <summary>JSON object of properties this column set does not cover, preserved for rebuild fidelity.</summary>
    public string? Overflow { get; init; }

    /// <summary>Input-side tokens as the Playbook's joiner counts them — input plus both cache legs.</summary>
    public long TokensInTotal => (TokensInput ?? 0) + (TokensCacheRead ?? 0) + (TokensCacheWrite ?? 0);

    /// <summary>Output-side tokens as the Playbook's joiner counts them — output plus reasoning.</summary>
    public long TokensOutTotal => (TokensOutput ?? 0) + (TokensReasoning ?? 0);
}
