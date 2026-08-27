namespace TfLens.Core.Contracts;

/// <summary>
/// The four TechieFlow telemetry streams plus the Playbook stream, as recognised by TfLens.
/// </summary>
/// <remarks>
/// The wire names (<c>runs</c>, <c>gates</c>, <c>sessions</c>, <c>commits</c>, <c>events</c>) are the
/// JSONL file base names under a repository's telemetry path; <see cref="StreamNames"/> holds them.
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
    Events = 4
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

    /// <summary>The four TechieFlow streams in report order.</summary>
    public static readonly IReadOnlyList<string> TechieFlow = [Runs, Gates, Sessions, Commits];

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
