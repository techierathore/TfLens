using System.Text.Json;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Parsing;

/// <summary>
/// Turns one raw JSONL stream file into typed records, preserving verbatim everything it does not
/// recognise (REQ-FN-030, REQ-FN-031, REQ-FN-032, REQ-FN-036).
/// </summary>
/// <remarks>
/// <para>
/// <b>This class is the single place the two spellings meet.</b> The wire format is SCHEMA.md's
/// snake_case (<c>req_id</c>, <c>gates_run</c>, <c>cost_usd</c>, <c>project_type_inferred</c>); the
/// store and every record type use the Coding Standards' PascalCase (<c>ReqId</c>, <c>GatesRun</c>,
/// <c>CostUsd</c>, <c>ProjectTypeInferred</c>). The mapping tables below are the whole translation —
/// nothing else in TfLens knows a snake_case name.
/// </para>
/// <para>
/// <b>SCHEMA.md → column mapping (REQ-FN-030).</b>
/// </para>
/// <para>
/// Every record (SCHEMA.md §1): <c>v</c>→<c>V</c>, <c>ts</c>→<c>Ts</c>, <c>app</c>→<c>App</c>,
/// <c>project_type</c>→<c>ProjectType</c>, <c>project_type_inferred</c>→<c>ProjectTypeInferred</c>,
/// <c>backfilled</c>→<c>Backfilled</c>, <c>inferred</c>→<c>Inferred</c>, <c>harness</c>→<c>Harness</c>.
/// <c>kind</c> is documented but carries no column: the stream file already says which stream it is.
/// </para>
/// <para>
/// <c>runs</c> (§2, §2.5): <c>cmd</c>→<c>Cmd</c>, <c>mode</c>→<c>Mode</c>, <c>started</c>→<c>Started</c>,
/// <c>ended</c>→<c>Ended</c>, <c>duration_s</c>→<c>DurationS</c>, <c>reqs_touched</c>→<c>ReqsTouched</c>,
/// <c>reqs_count</c>→<c>ReqsCount</c>, <c>subagents</c>→<c>Subagents</c>,
/// <c>files_written</c>→<c>FilesWritten</c>, <c>build_result</c>→<c>BuildResult</c>,
/// <c>tier</c>→<c>Tier</c>, <c>tier_model</c>→<c>TierModel</c>, <c>model</c>→<c>Model</c>,
/// <c>models</c>→<c>Models</c>, <c>routed</c>→<c>Routed</c>, <c>tokens_in</c>→<c>TokensIn</c>,
/// <c>tokens_out</c>→<c>TokensOut</c>, <c>tokens_cache_read</c>→<c>TokensCacheRead</c>,
/// <c>tokens_cache_write</c>→<c>TokensCacheWrite</c>, <c>cost_usd</c>→<c>CostUsd</c>,
/// <c>tokens_scope</c>→<c>TokensScope</c>, <c>attempt</c>→<c>Attempt</c>.
/// </para>
/// <para>
/// <c>gates</c> (§3): <c>run_id</c>→<c>RunId</c>, <c>req_id</c>→<c>ReqId</c>,
/// <c>req_class</c>→<c>ReqClass</c>, <c>attempt</c>→<c>Attempt</c>, <c>verdict</c>→<c>Verdict</c>,
/// <c>gate</c>→<c>Gate</c>, <c>gates_run</c>→<c>GatesRun</c>, <c>failure_class</c>→<c>FailureClass</c>,
/// <c>prior_verdict</c>→<c>PriorVerdict</c>, <c>proof</c>→<c>Proof</c>.
/// </para>
/// <para>
/// <c>sessions</c> (§4): <c>session_id</c>→<c>SessionId</c>, <c>model</c>→<c>Model</c>,
/// <c>duration_s</c>→<c>DurationS</c>, <c>input_tokens</c>→<c>InputTokens</c>,
/// <c>output_tokens</c>→<c>OutputTokens</c>, <c>cache_read_tokens</c>→<c>CacheReadTokens</c>,
/// <c>cache_creation_tokens</c>→<c>CacheCreationTokens</c>, <c>cost_usd</c>→<c>CostUsd</c>.
/// </para>
/// <para>
/// <c>commits</c> (§5): <c>sha</c>→<c>Sha</c>, <c>files</c>→<c>Files</c>,
/// <c>insertions</c>→<c>Insertions</c>, <c>deletions</c>→<c>Deletions</c>,
/// <c>subject_prefix</c>→<c>SubjectPrefix</c>, <c>branch</c>→<c>Branch</c>.
/// </para>
/// <para>
/// <c>misses</c> (§5.5, added 2026-08-28 — REQ-FN-071, REQ-FN-072, ADR-018): the one stream whose
/// records do <b>not</b> all share a shape. Three kinds land in three tables, dispatched on the
/// record's own <c>kind</c>. Common to all three: the §1 set above.
/// </para>
/// <para>
/// <c>kind: "miss"</c> (§5.5.1) → <c>"Miss"</c>: <c>miss_id</c>→<c>MissId</c>,
/// <c>req_id</c>→<c>ReqId</c>, <c>req_class</c>→<c>ReqClass</c>, <c>miss_class</c>→<c>MissClass</c>,
/// <c>artifact</c>→<c>Artifact</c>, <c>severity</c>→<c>Severity</c>,
/// <c>why_missed</c>→<c>WhyMissed</c>, <c>origin_phase</c>→<c>OriginPhase</c>,
/// <c>origin_agent</c>→<c>OriginAgent</c>, <c>origin_run_id</c>→<c>OriginRunId</c>,
/// <c>origin_confidence</c>→<c>OriginConfidence</c>, <c>origin_model</c>→<c>OriginModel</c>,
/// <c>origin_harness</c>→<c>OriginHarness</c>, <c>found_by</c>→<c>FoundBy</c>,
/// <c>found_phase</c>→<c>FoundPhase</c>, <c>found_gate</c>→<c>FoundGate</c>,
/// <c>found_run_id</c>→<c>FoundRunId</c>, <c>failure_class</c>→<c>FailureClass</c>.
/// </para>
/// <para>
/// <c>kind: "miss-fix"</c> (§5.5.2) → <c>"MissFix"</c>: <c>miss_id</c>→<c>MissId</c>,
/// <c>req_id</c>→<c>ReqId</c>, <c>fix_run_id</c>→<c>FixRunId</c>, <c>fix_cmd</c>→<c>FixCmd</c>,
/// <c>fix_attempt</c>→<c>FixAttempt</c>, <c>verdict_after</c>→<c>VerdictAfter</c>,
/// <c>reopened</c>→<c>Reopened</c>, <c>cost_attribution</c>→<c>CostAttribution</c>,
/// <c>tokens_in</c>→<c>TokensIn</c>, <c>tokens_out</c>→<c>TokensOut</c>,
/// <c>tokens_cache_read</c>→<c>TokensCacheRead</c>, <c>tokens_cache_write</c>→<c>TokensCacheWrite</c>,
/// <c>cost_usd</c>→<c>CostUsd</c>, <c>tokens_scope</c>→<c>TokensScope</c>, <c>model</c>→<c>Model</c>.
/// </para>
/// <para>
/// <c>kind: "miss-amend"</c> (§5.5.7) → <c>"MissAmend"</c>: <c>miss_id</c>→<c>MissId</c>,
/// <c>field</c>→<c>Field</c>, <c>value</c>→<c>Value</c>. Any other <c>kind</c> increments
/// <see cref="ParseResult.InvalidLines"/> and is skipped — never thrown, because an unknown kind in a
/// stream TfLens <i>does</i> know is the same class of event as a malformed line (REQ-FN-032).
/// </para>
/// <para>
/// <c>events</c> (Playbook, amended 2026-08-26 from the emitter source — REQ-FN-068, ADR-010): this is
/// the one stream whose wire spelling is <b>not</b> snake_case. <c>kind</c>→<c>Kind</c>,
/// <c>command</c>→<c>PhaseGate</c> (latched on <c>phase-start</c> and carried across the phase),
/// <c>arguments</c>→<c>Arguments</c>, <c>sessionID</c>→<c>SessionId</c>, <c>parentID</c>→<c>ParentId</c>,
/// <c>messageID</c>→<c>MessageId</c>, <c>model</c>→<c>Model</c>, <c>cost</c>→<c>CostUsd</c>, and the
/// nested <c>tokens</c> object fans out to five columns: <c>tokens.input</c>→<c>TokensInput</c>,
/// <c>tokens.output</c>→<c>TokensOutput</c>, <c>tokens.reasoning</c>→<c>TokensReasoning</c>,
/// <c>tokens.cache.read</c>→<c>TokensCacheRead</c>, <c>tokens.cache.write</c>→<c>TokensCacheWrite</c>.
/// </para>
/// <para>
/// <b>Three rules the mapping enforces.</b> (1) A malformed line is counted in
/// <see cref="ParseResult.InvalidLines"/> and skipped — never fatal, exactly as <c>read_stream</c> in
/// <c>tf-metrics.sh</c> does (REQ-FN-032). (2) A property with no column, and <b>every</b> property of a
/// record whose <c>v</c> is greater than 1, is written verbatim to that record's <c>Overflow</c> column
/// (REQ-FN-031). (3) An absent optional is <c>null</c>; a present <c>0</c> is <c>0</c>. "Not captured"
/// and "zero" are different facts and stay different at the column level (SCHEMA.md §2.5, REQ-FN-036).
/// </para>
/// </remarks>
public sealed class StreamParser : IStreamParser
{
    /// <summary>Fields SCHEMA.md §1 documents on every record of every stream.</summary>
    private static readonly string[] CommonDocumented =
        ["v", "ts", "kind", "app", "project_type", "project_type_inferred", "backfilled", "inferred", "harness"];

    /// <summary>Fields SCHEMA.md §2/§2.5 documents for <c>runs</c>, on top of <see cref="CommonDocumented"/>.</summary>
    private static readonly string[] RunDocumented =
    [
        "cmd", "mode", "started", "ended", "duration_s", "reqs_touched", "reqs_count", "subagents",
        "files_written", "build_result", "tier", "tier_model", "model", "models", "routed",
        "tokens_in", "tokens_out", "tokens_cache_read", "tokens_cache_write", "cost_usd",
        "tokens_scope", "attempt"
    ];

    /// <summary>Fields SCHEMA.md §3 documents for <c>gates</c>.</summary>
    private static readonly string[] GateDocumented =
    [
        "run_id", "req_id", "req_class", "attempt", "verdict", "gate", "gates_run", "failure_class",
        "prior_verdict", "proof"
    ];

    /// <summary>Fields SCHEMA.md §4 documents for <c>sessions</c>.</summary>
    private static readonly string[] SessionDocumented =
    [
        "session_id", "model", "duration_s", "input_tokens", "output_tokens", "cache_read_tokens",
        "cache_creation_tokens", "cost_usd"
    ];

    /// <summary>Fields SCHEMA.md §5 documents for <c>commits</c>.</summary>
    private static readonly string[] CommitDocumented =
        ["sha", "files", "insertions", "deletions", "subject_prefix", "branch"];

    /// <summary>Fields SCHEMA.md §5.5.1 documents for a <c>miss</c> record.</summary>
    private static readonly string[] MissDocumented =
    [
        "miss_id", "req_id", "req_class", "miss_class", "artifact", "severity", "why_missed",
        "origin_phase", "origin_agent", "origin_run_id", "origin_confidence", "origin_model",
        "origin_harness", "found_by", "found_phase", "found_gate", "found_run_id", "failure_class"
    ];

    /// <summary>Fields SCHEMA.md §5.5.2 documents for a <c>miss-fix</c> record.</summary>
    private static readonly string[] MissFixDocumented =
    [
        "miss_id", "req_id", "fix_run_id", "fix_cmd", "fix_attempt", "verdict_after", "reopened",
        "cost_attribution", "tokens_in", "tokens_out", "tokens_cache_read", "tokens_cache_write",
        "cost_usd", "tokens_scope", "model"
    ];

    /// <summary>Fields SCHEMA.md §5.5.7 documents for a <c>miss-amend</c> record.</summary>
    private static readonly string[] MissAmendDocumented = ["miss_id", "field", "value"];

    /// <summary>
    /// Playbook <c>events.ndjson</c> wire fields, read off the emitter source (REQ-FN-068, ADR-010).
    /// </summary>
    /// <remarks>
    /// Amended 2026-08-26 from <c>harness/opencode/plugin/telemetry.ts</c> in
    /// <c>techierathore/AI-First-Playbook</c>; recorded in <c>DECISIONS.md</c> §Playbook. The day-1
    /// snake_case guesses (<c>event_type</c>, <c>session_id</c>, …) matched nothing the Playbook emits:
    /// the wire spelling is camelCase with capitalised acronyms, and <c>tokens</c> is a nested object.
    /// </remarks>
    private static readonly string[] EventDocumented =
    [
        PlaybookWireFields.Kind,
        PlaybookWireFields.Command,
        PlaybookWireFields.Arguments,
        PlaybookWireFields.SessionId,
        PlaybookWireFields.ParentId,
        PlaybookWireFields.MessageId,
        PlaybookWireFields.Model,
        PlaybookWireFields.Tokens,
        PlaybookWireFields.Cost
    ];

    /// <summary>Wire names that <c>"Run"</c> has a column for; anything else overflows.</summary>
    private static readonly HashSet<string> RunMapped = BuildMapped(RunDocumented, "inferred");

    /// <summary>Wire names that <c>"Gate"</c> has a column for; anything else overflows.</summary>
    private static readonly HashSet<string> GateMapped = BuildMapped(GateDocumented);

    /// <summary>Wire names that <c>"Session"</c> has a column for; anything else overflows.</summary>
    private static readonly HashSet<string> SessionMapped =
        BuildMapped(SessionDocumented, "project_type_inferred", "backfilled", "inferred");

    /// <summary>Wire names that <c>"Commit"</c> has a column for; anything else overflows.</summary>
    private static readonly HashSet<string> CommitMapped =
        BuildMapped(CommitDocumented, "project_type_inferred", "backfilled", "inferred", "harness");

    /// <summary>Wire names that <c>"Miss"</c> has a column for; anything else overflows.</summary>
    private static readonly HashSet<string> MissMapped = BuildMapped(MissDocumented, "inferred");

    /// <summary>Wire names that <c>"MissFix"</c> has a column for; anything else overflows.</summary>
    private static readonly HashSet<string> MissFixMapped = BuildMapped(MissFixDocumented, "inferred");

    /// <summary>Wire names that <c>"MissAmend"</c> has a column for; anything else overflows.</summary>
    private static readonly HashSet<string> MissAmendMapped = BuildMapped(MissAmendDocumented, "inferred");

    /// <summary>Wire names that <c>"PbEvent"</c> has a column for; anything else overflows.</summary>
    private static readonly HashSet<string> EventMapped = new(EventDocumented, StringComparer.Ordinal)
    {
        "ts"
    };

    /// <summary>Every wire name SCHEMA.md documents for <c>runs</c>.</summary>
    private static readonly HashSet<string> RunKnown = BuildKnown(RunDocumented);

    /// <summary>Every wire name SCHEMA.md documents for <c>gates</c>.</summary>
    private static readonly HashSet<string> GateKnown = BuildKnown(GateDocumented);

    /// <summary>Every wire name SCHEMA.md documents for <c>sessions</c>.</summary>
    private static readonly HashSet<string> SessionKnown = BuildKnown(SessionDocumented);

    /// <summary>Every wire name SCHEMA.md documents for <c>commits</c>.</summary>
    private static readonly HashSet<string> CommitKnown = BuildKnown(CommitDocumented);

    /// <summary>Every wire name the provisional Playbook column set covers.</summary>
    private static readonly HashSet<string> EventKnown = BuildKnown(EventDocumented);

    /// <summary>
    /// Every wire name SCHEMA.md documents for <c>misses</c> — the <b>union</b> of the three kinds.
    /// </summary>
    /// <remarks>
    /// <see cref="IsDocumented"/> is keyed on the stream, and <c>misses</c> has three field
    /// vocabularies. Coverage's "fields observed that SCHEMA.md does not document" report takes their
    /// union: a <c>miss-fix</c>-only field seen on a <c>miss</c> record is not worth a separate report
    /// and would only produce noise (REQ-FN-072, ADR-018).
    /// </remarks>
    private static readonly HashSet<string> MissesKnown =
        BuildKnown([.. MissDocumented, .. MissFixDocumented, .. MissAmendDocumented]);

    /// <summary>
    /// Says whether SCHEMA.md documents a wire field name for a stream (REQ-UI-016).
    /// </summary>
    /// <remarks>
    /// The Coverage page reports the fields observed that SCHEMA.md does not document. For a row already
    /// in the store the observed names are the keys of its <c>Overflow</c> column — but that column also
    /// holds documented fields the table happens to have no column for (<c>inferred</c> on a run, for
    /// instance), so the keys have to be measured against the documented set before any of them is called
    /// undocumented. This is that set, exposed from the one class that owns it rather than copied.
    /// </remarks>
    /// <remarks>
    /// <b><see cref="StreamKind.Misses"/> answers over the union of its three field vocabularies</b>
    /// (<c>miss</c>, <c>miss-fix</c>, <c>miss-amend</c>). The stream is one file with three record
    /// shapes, and reporting a <c>fix_run_id</c> seen on a <c>miss</c> record as undocumented would be
    /// noise rather than a finding (REQ-FN-072).
    /// </remarks>
    /// <param name="aStream">The stream the field was observed in.</param>
    /// <param name="aField">The wire field name.</param>
    /// <returns><c>true</c> when SCHEMA.md documents the field for that stream.</returns>
    public static bool IsDocumented(StreamKind aStream, string aField)
    {
        ArgumentNullException.ThrowIfNull(aField);

        return aStream switch
        {
            StreamKind.Runs => RunKnown.Contains(aField),
            StreamKind.Gates => GateKnown.Contains(aField),
            StreamKind.Sessions => SessionKnown.Contains(aField),
            StreamKind.Commits => CommitKnown.Contains(aField),
            StreamKind.Misses => MissesKnown.Contains(aField),
            StreamKind.Events => EventKnown.Contains(aField),
            _ => throw new ArgumentOutOfRangeException(nameof(aStream), aStream, "Unknown stream kind.")
        };
    }

    /// <inheritdoc />
    public ParseResult Parse(int aUserId, string aRepo, string aSourceSha, StreamKind aStream, string aText)
    {
        ArgumentNullException.ThrowIfNull(aRepo);
        ArgumentNullException.ThrowIfNull(aSourceSha);

        var vState = new ParseState(aUserId, aRepo, aSourceSha, aStream);

        foreach (var vRawLine in (aText ?? string.Empty).Split('\n'))
        {
            var vLine = vRawLine.Trim();
            if (vLine.Length == 0)
            {
                continue;
            }

            ReadLine(vLine, vState);
        }

        return vState.ToResult();
    }

    /// <summary>
    /// Reads one JSONL line into the state, counting it as invalid rather than throwing.
    /// </summary>
    /// <param name="aLine">The trimmed line text.</param>
    /// <param name="aState">Accumulating parse state.</param>
    private static void ReadLine(string aLine, ParseState aState)
    {
        JsonDocument vDoc;
        try
        {
            vDoc = JsonDocument.Parse(aLine);
        }
        catch (JsonException)
        {
            // REQ-FN-032: counted and skipped, exactly as read_stream does. Never fatal.
            aState.InvalidLines++;
            return;
        }

        using (vDoc)
        {
            var vRoot = vDoc.RootElement;
            if (vRoot.ValueKind != JsonValueKind.Object)
            {
                aState.InvalidLines++;
                return;
            }

            AddRecord(vRoot, aState);
        }
    }

    /// <summary>
    /// Maps one JSON object onto the typed record for the stream being parsed.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    private static void AddRecord(JsonElement aObj, ParseState aState)
    {
        var vVersion = ReadInt(aObj, "v") ?? 1;
        var vIsAboveV1 = vVersion > 1;
        if (vIsAboveV1)
        {
            // REQ-FN-031: a record from a newer schema is stored whole in Overflow rather than
            // squeezed through a column set that was never written for it.
            aState.RecordsAboveSchemaV1++;
        }

        var vTs = ReadString(aObj, "ts") ?? string.Empty;

        switch (aState.Stream)
        {
            case StreamKind.Runs:
                CollectUnknown(aObj, RunKnown, aState);
                aState.Runs.Add(BuildRun(aObj, aState, vVersion, vTs, vIsAboveV1));
                break;
            case StreamKind.Gates:
                CollectUnknown(aObj, GateKnown, aState);
                aState.Gates.Add(BuildGate(aObj, aState, vVersion, vTs, vIsAboveV1));
                break;
            case StreamKind.Sessions:
                CollectUnknown(aObj, SessionKnown, aState);
                aState.Sessions.Add(BuildSession(aObj, aState, vVersion, vTs, vIsAboveV1));
                break;
            case StreamKind.Commits:
                CollectUnknown(aObj, CommitKnown, aState);
                aState.Commits.Add(BuildCommit(aObj, aState, vVersion, vTs, vIsAboveV1));
                break;
            case StreamKind.Misses:
                AddMissRecord(aObj, aState, vVersion, vTs, vIsAboveV1);
                break;
            case StreamKind.Events:
                CollectUnknown(aObj, EventKnown, aState);
                aState.PbEvents.Add(BuildPbEvent(aObj, aState, vTs, vIsAboveV1));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(aState), aState.Stream, "Unknown stream kind.");
        }
    }

    /// <summary>
    /// Dispatches one <c>misses.jsonl</c> record on its own <c>kind</c> (REQ-FN-072, ADR-018).
    /// </summary>
    /// <remarks>
    /// This is the one place <see cref="StreamKind"/> stops being 1:1 with a table. All three kinds
    /// parse from one file in a single pass, and an unrecognised <c>kind</c> increments
    /// <see cref="ParseResult.InvalidLines"/> and is skipped rather than thrown — the same contract a
    /// malformed line gets (REQ-FN-032), because an unknown kind in a stream TfLens does know is the
    /// same class of event. The undocumented-field report is collected against the union of the three
    /// vocabularies, matching <see cref="IsDocumented"/>.
    /// </remarks>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    /// <param name="aVersion">The record's schema version.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    private static void AddMissRecord(
        JsonElement aObj, ParseState aState, int aVersion, string aTs, bool aIsAboveV1)
    {
        var vKind = ReadString(aObj, "kind");

        switch (vKind)
        {
            case MissKinds.Miss:
                CollectUnknown(aObj, MissesKnown, aState);
                aState.Misses.Add(BuildMiss(aObj, aState, aVersion, aTs, aIsAboveV1));
                return;
            case MissKinds.MissFix:
                CollectUnknown(aObj, MissesKnown, aState);
                aState.MissFixes.Add(BuildMissFix(aObj, aState, aVersion, aTs, aIsAboveV1));
                return;
            case MissKinds.MissAmend:
                CollectUnknown(aObj, MissesKnown, aState);
                aState.MissAmends.Add(BuildMissAmend(aObj, aState, aVersion, aTs, aIsAboveV1));
                return;
            default:
                aState.InvalidLines++;
                return;
        }
    }

    /// <summary>
    /// Builds one <c>miss</c> record; a <c>v &gt; 1</c> record keeps only its identity columns.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    /// <param name="aVersion">The record's schema version.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    /// <returns>The typed record.</returns>
    private static MissRecord BuildMiss(
        JsonElement aObj, ParseState aState, int aVersion, string aTs, bool aIsAboveV1)
    {
        var vOverflow = BuildOverflow(aObj, MissMapped, aIsAboveV1);
        if (aIsAboveV1)
        {
            return new MissRecord
            {
                UserId = aState.UserId,
                Repo = aState.Repo,
                SourceSha = aState.SourceSha,
                V = aVersion,
                Ts = aTs,
                App = ReadString(aObj, "app"),
                MissId = ReadString(aObj, "miss_id") ?? string.Empty,
                Overflow = vOverflow
            };
        }

        return new MissRecord
        {
            UserId = aState.UserId,
            Repo = aState.Repo,
            SourceSha = aState.SourceSha,
            V = aVersion,
            Ts = aTs,
            App = ReadString(aObj, "app"),
            ProjectType = ReadString(aObj, "project_type"),
            ProjectTypeInferred = ReadBool(aObj, "project_type_inferred"),
            Backfilled = ReadBool(aObj, "backfilled"),
            Harness = ReadString(aObj, "harness"),
            MissId = ReadString(aObj, "miss_id") ?? string.Empty,
            ReqId = ReadString(aObj, "req_id"),
            ReqClass = ReadString(aObj, "req_class"),
            MissClass = ReadString(aObj, "miss_class"),
            Artifact = ReadString(aObj, "artifact"),
            Severity = ReadString(aObj, "severity"),
            WhyMissed = ReadString(aObj, "why_missed"),
            OriginPhase = ReadString(aObj, "origin_phase"),
            OriginAgent = ReadString(aObj, "origin_agent"),
            OriginRunId = ReadString(aObj, "origin_run_id"),
            OriginConfidence = ReadString(aObj, "origin_confidence"),
            OriginModel = ReadString(aObj, "origin_model"),
            OriginHarness = ReadString(aObj, "origin_harness"),
            FoundBy = ReadString(aObj, "found_by"),
            FoundPhase = ReadString(aObj, "found_phase"),
            FoundGate = ReadString(aObj, "found_gate"),
            FoundRunId = ReadString(aObj, "found_run_id"),
            FailureClass = ReadString(aObj, "failure_class"),
            Overflow = vOverflow
        };
    }

    /// <summary>
    /// Builds one <c>miss-fix</c> record; a <c>v &gt; 1</c> record keeps only its identity columns.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    /// <param name="aVersion">The record's schema version.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    /// <returns>The typed record.</returns>
    private static MissFixRecord BuildMissFix(
        JsonElement aObj, ParseState aState, int aVersion, string aTs, bool aIsAboveV1)
    {
        var vOverflow = BuildOverflow(aObj, MissFixMapped, aIsAboveV1);
        if (aIsAboveV1)
        {
            return new MissFixRecord
            {
                UserId = aState.UserId,
                Repo = aState.Repo,
                SourceSha = aState.SourceSha,
                V = aVersion,
                Ts = aTs,
                App = ReadString(aObj, "app"),
                MissId = ReadString(aObj, "miss_id") ?? string.Empty,
                FixRunId = ReadString(aObj, "fix_run_id"),
                Overflow = vOverflow
            };
        }

        return new MissFixRecord
        {
            UserId = aState.UserId,
            Repo = aState.Repo,
            SourceSha = aState.SourceSha,
            V = aVersion,
            Ts = aTs,
            App = ReadString(aObj, "app"),
            ProjectType = ReadString(aObj, "project_type"),
            ProjectTypeInferred = ReadBool(aObj, "project_type_inferred"),
            Backfilled = ReadBool(aObj, "backfilled"),
            Harness = ReadString(aObj, "harness"),
            MissId = ReadString(aObj, "miss_id") ?? string.Empty,
            ReqId = ReadString(aObj, "req_id"),
            FixRunId = ReadString(aObj, "fix_run_id"),
            FixCmd = ReadString(aObj, "fix_cmd"),
            FixAttempt = ReadInt(aObj, "fix_attempt"),
            VerdictAfter = ReadString(aObj, "verdict_after"),
            Reopened = ReadBool(aObj, "reopened"),
            CostAttribution = ReadString(aObj, "cost_attribution"),
            TokensIn = ReadInt(aObj, "tokens_in"),
            TokensOut = ReadInt(aObj, "tokens_out"),
            TokensCacheRead = ReadInt(aObj, "tokens_cache_read"),
            TokensCacheWrite = ReadInt(aObj, "tokens_cache_write"),
            CostUsd = ReadDecimal(aObj, "cost_usd"),
            TokensScope = ReadString(aObj, "tokens_scope"),
            Model = ReadString(aObj, "model"),
            Overflow = vOverflow
        };
    }

    /// <summary>
    /// Builds one <c>miss-amend</c> record; a <c>v &gt; 1</c> record keeps only its identity columns.
    /// </summary>
    /// <remarks>
    /// The amendment is stored exactly as written. Nothing is folded here: the allowlist, the closed
    /// vocabulary and the never-overwrite-a-value rule are re-applied at read time so a rebuild
    /// re-derives identical values whatever order the archived files arrived in (ADR-020).
    /// </remarks>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    /// <param name="aVersion">The record's schema version.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    /// <returns>The typed record.</returns>
    private static MissAmendRecord BuildMissAmend(
        JsonElement aObj, ParseState aState, int aVersion, string aTs, bool aIsAboveV1)
    {
        var vOverflow = BuildOverflow(aObj, MissAmendMapped, aIsAboveV1);
        if (aIsAboveV1)
        {
            return new MissAmendRecord
            {
                UserId = aState.UserId,
                Repo = aState.Repo,
                SourceSha = aState.SourceSha,
                V = aVersion,
                Ts = aTs,
                App = ReadString(aObj, "app"),
                MissId = ReadString(aObj, "miss_id") ?? string.Empty,
                Field = ReadString(aObj, "field") ?? string.Empty,
                Overflow = vOverflow
            };
        }

        return new MissAmendRecord
        {
            UserId = aState.UserId,
            Repo = aState.Repo,
            SourceSha = aState.SourceSha,
            V = aVersion,
            Ts = aTs,
            App = ReadString(aObj, "app"),
            ProjectType = ReadString(aObj, "project_type"),
            ProjectTypeInferred = ReadBool(aObj, "project_type_inferred"),
            Backfilled = ReadBool(aObj, "backfilled"),
            Harness = ReadString(aObj, "harness"),
            MissId = ReadString(aObj, "miss_id") ?? string.Empty,
            Field = ReadString(aObj, "field") ?? string.Empty,
            Value = ReadString(aObj, "value"),
            Overflow = vOverflow
        };
    }

    /// <summary>
    /// Builds one <c>runs</c> record; a <c>v &gt; 1</c> record keeps only its identity columns.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state, carrying user, repo and source SHA.</param>
    /// <param name="aVersion">The record's schema version.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    /// <returns>The typed record.</returns>
    private static RunRecord BuildRun(
        JsonElement aObj, ParseState aState, int aVersion, string aTs, bool aIsAboveV1)
    {
        var vOverflow = BuildOverflow(aObj, RunMapped, aIsAboveV1);
        if (aIsAboveV1)
        {
            return new RunRecord
            {
                UserId = aState.UserId,
                Repo = aState.Repo,
                SourceSha = aState.SourceSha,
                V = aVersion,
                Ts = aTs,
                App = ReadString(aObj, "app"),
                Cmd = ReadString(aObj, "cmd"),
                Overflow = vOverflow
            };
        }

        return new RunRecord
        {
            UserId = aState.UserId,
            Repo = aState.Repo,
            SourceSha = aState.SourceSha,
            V = aVersion,
            Ts = aTs,
            App = ReadString(aObj, "app"),
            ProjectType = ReadString(aObj, "project_type"),
            ProjectTypeInferred = ReadBool(aObj, "project_type_inferred"),
            Backfilled = ReadBool(aObj, "backfilled"),
            Harness = ReadString(aObj, "harness"),
            Cmd = ReadString(aObj, "cmd"),
            Mode = ReadString(aObj, "mode"),
            Started = ReadString(aObj, "started"),
            Ended = ReadString(aObj, "ended"),
            DurationS = ReadInt(aObj, "duration_s"),
            ReqsTouched = ReadJsonText(aObj, "reqs_touched"),
            ReqsCount = ReadInt(aObj, "reqs_count"),
            Subagents = ReadJsonText(aObj, "subagents"),
            FilesWritten = ReadInt(aObj, "files_written"),
            BuildResult = ReadString(aObj, "build_result"),
            Tier = ReadString(aObj, "tier"),
            TierModel = ReadString(aObj, "tier_model"),
            Model = ReadString(aObj, "model"),
            Models = ReadJsonText(aObj, "models"),
            Routed = ReadBool(aObj, "routed"),
            TokensIn = ReadInt(aObj, "tokens_in"),
            TokensOut = ReadInt(aObj, "tokens_out"),
            TokensCacheRead = ReadInt(aObj, "tokens_cache_read"),
            TokensCacheWrite = ReadInt(aObj, "tokens_cache_write"),
            CostUsd = ReadDecimal(aObj, "cost_usd"),
            TokensScope = ReadString(aObj, "tokens_scope"),
            Attempt = ReadInt(aObj, "attempt"),
            Overflow = vOverflow
        };
    }

    /// <summary>
    /// Builds one <c>gates</c> record; a <c>v &gt; 1</c> record keeps only its identity columns.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    /// <param name="aVersion">The record's schema version.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    /// <returns>The typed record.</returns>
    private static GateRecord BuildGate(
        JsonElement aObj, ParseState aState, int aVersion, string aTs, bool aIsAboveV1)
    {
        var vOverflow = BuildOverflow(aObj, GateMapped, aIsAboveV1);
        if (aIsAboveV1)
        {
            return new GateRecord
            {
                UserId = aState.UserId,
                Repo = aState.Repo,
                SourceSha = aState.SourceSha,
                V = aVersion,
                Ts = aTs,
                App = ReadString(aObj, "app"),
                ReqId = ReadString(aObj, "req_id"),
                RunId = ReadString(aObj, "run_id"),
                Overflow = vOverflow
            };
        }

        return new GateRecord
        {
            UserId = aState.UserId,
            Repo = aState.Repo,
            SourceSha = aState.SourceSha,
            V = aVersion,
            Ts = aTs,
            App = ReadString(aObj, "app"),
            ProjectType = ReadString(aObj, "project_type"),
            ProjectTypeInferred = ReadBool(aObj, "project_type_inferred"),
            Backfilled = ReadBool(aObj, "backfilled"),
            Inferred = ReadJsonText(aObj, "inferred"),
            Harness = ReadString(aObj, "harness"),
            RunId = ReadString(aObj, "run_id"),
            ReqId = ReadString(aObj, "req_id"),
            ReqClass = ReadString(aObj, "req_class"),
            Attempt = ReadInt(aObj, "attempt"),
            Verdict = ReadString(aObj, "verdict"),
            Gate = ReadString(aObj, "gate"),
            GatesRun = ReadJsonText(aObj, "gates_run"),
            FailureClass = ReadString(aObj, "failure_class"),
            PriorVerdict = ReadString(aObj, "prior_verdict"),
            Proof = ReadString(aObj, "proof"),
            Overflow = vOverflow
        };
    }

    /// <summary>
    /// Builds one <c>sessions</c> record; a <c>v &gt; 1</c> record keeps only its identity columns.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    /// <param name="aVersion">The record's schema version.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    /// <returns>The typed record.</returns>
    private static SessionRecord BuildSession(
        JsonElement aObj, ParseState aState, int aVersion, string aTs, bool aIsAboveV1)
    {
        var vOverflow = BuildOverflow(aObj, SessionMapped, aIsAboveV1);
        if (aIsAboveV1)
        {
            return new SessionRecord
            {
                UserId = aState.UserId,
                Repo = aState.Repo,
                SourceSha = aState.SourceSha,
                V = aVersion,
                Ts = aTs,
                App = ReadString(aObj, "app"),
                SessionId = ReadString(aObj, "session_id") ?? string.Empty,
                Overflow = vOverflow
            };
        }

        return new SessionRecord
        {
            UserId = aState.UserId,
            Repo = aState.Repo,
            SourceSha = aState.SourceSha,
            V = aVersion,
            Ts = aTs,
            App = ReadString(aObj, "app"),
            ProjectType = ReadString(aObj, "project_type"),
            Harness = ReadString(aObj, "harness"),
            SessionId = ReadString(aObj, "session_id") ?? string.Empty,
            Model = ReadString(aObj, "model"),
            DurationS = ReadInt(aObj, "duration_s"),
            InputTokens = ReadInt(aObj, "input_tokens"),
            OutputTokens = ReadInt(aObj, "output_tokens"),
            CacheReadTokens = ReadInt(aObj, "cache_read_tokens"),
            CacheCreationTokens = ReadInt(aObj, "cache_creation_tokens"),
            CostUsd = ReadDecimal(aObj, "cost_usd"),
            Overflow = vOverflow
        };
    }

    /// <summary>
    /// Builds one <c>commits</c> record; a <c>v &gt; 1</c> record keeps only its identity columns.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    /// <param name="aVersion">The record's schema version.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    /// <returns>The typed record.</returns>
    private static CommitRecord BuildCommit(
        JsonElement aObj, ParseState aState, int aVersion, string aTs, bool aIsAboveV1)
    {
        var vOverflow = BuildOverflow(aObj, CommitMapped, aIsAboveV1);
        if (aIsAboveV1)
        {
            return new CommitRecord
            {
                UserId = aState.UserId,
                Repo = aState.Repo,
                SourceSha = aState.SourceSha,
                V = aVersion,
                Ts = aTs,
                App = ReadString(aObj, "app"),
                Sha = ReadString(aObj, "sha") ?? string.Empty,
                Overflow = vOverflow
            };
        }

        return new CommitRecord
        {
            UserId = aState.UserId,
            Repo = aState.Repo,
            SourceSha = aState.SourceSha,
            V = aVersion,
            Ts = aTs,
            App = ReadString(aObj, "app"),
            ProjectType = ReadString(aObj, "project_type"),
            Sha = ReadString(aObj, "sha") ?? string.Empty,
            Files = ReadInt(aObj, "files"),
            Insertions = ReadInt(aObj, "insertions"),
            Deletions = ReadInt(aObj, "deletions"),
            SubjectPrefix = ReadString(aObj, "subject_prefix"),
            Branch = ReadString(aObj, "branch"),
            Overflow = vOverflow
        };
    }

    /// <summary>
    /// Builds one Playbook <c>events</c> record.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state.</param>
    /// <param name="aTs">The record's timestamp.</param>
    /// <param name="aIsAboveV1">True when the whole record belongs in <c>Overflow</c>.</param>
    /// <returns>The typed record.</returns>
    private static PbEventRecord BuildPbEvent(JsonElement aObj, ParseState aState, string aTs, bool aIsAboveV1)
    {
        var vOverflow = BuildOverflow(aObj, EventMapped, aIsAboveV1);
        var vKind = ReadString(aObj, PlaybookWireFields.Kind);

        if (aIsAboveV1)
        {
            return new PbEventRecord
            {
                UserId = aState.UserId,
                Repo = aState.Repo,
                SourceSha = aState.SourceSha,
                Ts = aTs,
                Kind = vKind,
                SessionId = ReadString(aObj, PlaybookWireFields.SessionId),
                Overflow = vOverflow
            };
        }

        var vTokens = ReadTokenBlock(aObj);

        return new PbEventRecord
        {
            UserId = aState.UserId,
            Repo = aState.Repo,
            SourceSha = aState.SourceSha,
            Ts = aTs,
            Kind = vKind,
            PhaseGate = TrackPhaseGate(aObj, aState, vKind),
            Arguments = ReadString(aObj, PlaybookWireFields.Arguments),
            SessionId = ReadString(aObj, PlaybookWireFields.SessionId),
            ParentId = ReadString(aObj, PlaybookWireFields.ParentId),
            MessageId = ReadString(aObj, PlaybookWireFields.MessageId),
            Model = ReadString(aObj, PlaybookWireFields.Model),
            TokensInput = vTokens.Input,
            TokensOutput = vTokens.Output,
            TokensReasoning = vTokens.Reasoning,
            TokensCacheRead = vTokens.CacheRead,
            TokensCacheWrite = vTokens.CacheWrite,
            CostUsd = ReadDecimal(aObj, PlaybookWireFields.Cost),
            Overflow = vOverflow
        };
    }

    /// <summary>
    /// Resolves the Playbook process gate for one record, carrying it across the phase (REQ-FN-068).
    /// </summary>
    /// <remarks>
    /// <c>events.ndjson</c> names the phase only once, as <c>command</c> on the <c>phase-start</c>
    /// record; the <c>turn</c> and <c>phase-end</c> records that follow belong to that phase by sequence.
    /// The current command is therefore latched on <c>phase-start</c> and stamped onto every record until
    /// the next one — exactly how <c>scripts/playbook-telemetry.mjs</c> joins the same file.
    /// </remarks>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aState">Accumulating parse state, which holds the latch.</param>
    /// <param name="aKind">The record's <c>kind</c>.</param>
    /// <returns>The process gate, or <c>null</c> before the first <c>phase-start</c>.</returns>
    private static string? TrackPhaseGate(JsonElement aObj, ParseState aState, string? aKind)
    {
        if (string.Equals(aKind, PlaybookEventKinds.PhaseStart, StringComparison.Ordinal))
        {
            aState.CurrentPhaseGate = ReadString(aObj, PlaybookWireFields.Command);
        }

        return aState.CurrentPhaseGate;
    }

    /// <summary>
    /// Reads the nested <c>tokens</c> object a Playbook <c>turn</c> record carries.
    /// </summary>
    /// <remarks>
    /// The shape is <c>{input, output, reasoning, cache:{read, write}}</c>. Every leg stays <c>null</c>
    /// when absent rather than reading as zero usage (SCHEMA.md §2.5); a record with no <c>tokens</c>
    /// object at all — every <c>phase-start</c> and <c>phase-end</c> — yields five nulls.
    /// </remarks>
    /// <param name="aObj">The record's JSON object.</param>
    /// <returns>The five token legs.</returns>
    private static TokenBlock ReadTokenBlock(JsonElement aObj)
    {
        if (!aObj.TryGetProperty(PlaybookWireFields.Tokens, out var vTokens)
            || vTokens.ValueKind != JsonValueKind.Object)
        {
            return new TokenBlock(null, null, null, null, null);
        }

        int? vCacheRead = null;
        int? vCacheWrite = null;
        if (vTokens.TryGetProperty("cache", out var vCache) && vCache.ValueKind == JsonValueKind.Object)
        {
            vCacheRead = ReadInt(vCache, "read");
            vCacheWrite = ReadInt(vCache, "write");
        }

        return new TokenBlock(
            ReadInt(vTokens, "input"),
            ReadInt(vTokens, "output"),
            ReadInt(vTokens, "reasoning"),
            vCacheRead,
            vCacheWrite);
    }

    /// <summary>The five token legs of a Playbook <c>turn</c> record.</summary>
    /// <param name="Input">Input tokens.</param>
    /// <param name="Output">Output tokens.</param>
    /// <param name="Reasoning">Reasoning tokens.</param>
    /// <param name="CacheRead">Cache-read tokens.</param>
    /// <param name="CacheWrite">Cache-write tokens.</param>
    private sealed record TokenBlock(int? Input, int? Output, int? Reasoning, int? CacheRead, int? CacheWrite);

    /// <summary>
    /// Records the distinct property names SCHEMA.md does not document, for the Coverage report.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aKnown">Every wire name SCHEMA.md documents for this stream.</param>
    /// <param name="aState">Accumulating parse state.</param>
    private static void CollectUnknown(JsonElement aObj, HashSet<string> aKnown, ParseState aState)
    {
        foreach (var vProperty in aObj.EnumerateObject())
        {
            if (!aKnown.Contains(vProperty.Name))
            {
                aState.UnknownFields.Add(vProperty.Name);
            }
        }
    }

    /// <summary>
    /// Builds the <c>Overflow</c> payload so no field is ever silently lost (REQ-FN-031).
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aMapped">Wire names this stream's table has a column for.</param>
    /// <param name="aIsWholeRecord">True for <c>v &gt; 1</c> — the entire record is preserved verbatim.</param>
    /// <returns>A JSON object as text, or <c>null</c> when every property had a column.</returns>
    private static string? BuildOverflow(JsonElement aObj, HashSet<string> aMapped, bool aIsWholeRecord)
    {
        if (aIsWholeRecord)
        {
            return aObj.GetRawText();
        }

        var vExtras = aObj.EnumerateObject().Where(aP => !aMapped.Contains(aP.Name)).ToList();
        if (vExtras.Count == 0)
        {
            return null;
        }

        using var vStream = new MemoryStream();
        using (var vWriter = new Utf8JsonWriter(vStream))
        {
            vWriter.WriteStartObject();
            foreach (var vProperty in vExtras)
            {
                vProperty.WriteTo(vWriter);
            }

            vWriter.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(vStream.ToArray());
    }

    /// <summary>
    /// Reads a string property. An absent or JSON-null property yields <c>null</c>, never <c>""</c>.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aName">The SCHEMA.md wire name.</param>
    /// <returns>The string value, or <c>null</c>.</returns>
    private static string? ReadString(JsonElement aObj, string aName) =>
        aObj.TryGetProperty(aName, out var vValue) && vValue.ValueKind == JsonValueKind.String
            ? vValue.GetString()
            : null;

    /// <summary>
    /// Reads an array (or object) property as its verbatim JSON text — SCHEMA.md's <c>string[]</c> fields.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aName">The SCHEMA.md wire name.</param>
    /// <returns>The raw JSON text, or <c>null</c> when absent or JSON-null.</returns>
    private static string? ReadJsonText(JsonElement aObj, string aName) =>
        aObj.TryGetProperty(aName, out var vValue) && vValue.ValueKind != JsonValueKind.Null
            ? vValue.GetRawText()
            : null;

    /// <summary>
    /// Reads an integer property. <b>Absent is <c>null</c>; a present <c>0</c> is <c>0</c></b> (SCHEMA.md §2.5).
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aName">The SCHEMA.md wire name.</param>
    /// <returns>The value, or <c>null</c> when the field was not captured.</returns>
    private static int? ReadInt(JsonElement aObj, string aName)
    {
        if (!aObj.TryGetProperty(aName, out var vValue) || vValue.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return vValue.TryGetInt32(out var vInt) ? vInt : (int)vValue.GetDouble();
    }

    /// <summary>
    /// Reads a decimal property. Absent stays <c>null</c> so an unmeasured cost never reads as zero spend.
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aName">The SCHEMA.md wire name.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static decimal? ReadDecimal(JsonElement aObj, string aName)
    {
        if (!aObj.TryGetProperty(aName, out var vValue) || vValue.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return vValue.TryGetDecimal(out var vDecimal) ? vDecimal : (decimal)vValue.GetDouble();
    }

    /// <summary>
    /// Reads a boolean property. Absent stays <c>null</c> — never coerced to <c>false</c> (REQ-FN-036).
    /// </summary>
    /// <param name="aObj">The record's JSON object.</param>
    /// <param name="aName">The SCHEMA.md wire name.</param>
    /// <returns><c>true</c>, <c>false</c>, or <c>null</c> when the field was absent.</returns>
    private static bool? ReadBool(JsonElement aObj, string aName)
    {
        if (!aObj.TryGetProperty(aName, out var vValue))
        {
            return null;
        }

        return vValue.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    /// <summary>
    /// Builds a stream's mapped-field set: the common fields plus the stream's own, minus the ones that
    /// this stream's table has no column for.
    /// </summary>
    /// <param name="aStreamFields">The stream's documented fields.</param>
    /// <param name="aWithoutColumn">Documented fields this stream's table does not carry, which therefore overflow.</param>
    /// <returns>The set of wire names that are resolved rather than overflowed.</returns>
    /// <remarks>
    /// <c>kind</c> is in every set even though no table has a <c>Kind</c> column: it is resolved by the
    /// table the record lands in, so preserving it in <c>Overflow</c> would give every single row an
    /// overflow payload of <c>{"kind":"run"}</c> and lose the signal that the column is meant to carry.
    /// </remarks>
    private static HashSet<string> BuildMapped(string[] aStreamFields, params string[] aWithoutColumn)
    {
        var vSet = new HashSet<string>(CommonDocumented, StringComparer.Ordinal);
        vSet.UnionWith(aStreamFields);
        vSet.ExceptWith(aWithoutColumn);
        return vSet;
    }

    /// <summary>
    /// Builds a stream's documented-field set — the denominator for "field SCHEMA.md doesn't document".
    /// </summary>
    /// <param name="aStreamFields">The stream's documented fields.</param>
    /// <returns>Every wire name SCHEMA.md documents for the stream.</returns>
    private static HashSet<string> BuildKnown(string[] aStreamFields)
    {
        var vSet = new HashSet<string>(CommonDocumented, StringComparer.Ordinal);
        vSet.UnionWith(aStreamFields);
        return vSet;
    }

    /// <summary>Mutable accumulator for one file's parse, converted to a <see cref="ParseResult"/> at the end.</summary>
    /// <param name="aUserId">The user the records belong to.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aSourceSha">The SHA the file was fetched at.</param>
    /// <param name="aStream">Which stream is being parsed.</param>
    private sealed class ParseState(int aUserId, string aRepo, string aSourceSha, StreamKind aStream)
    {
        /// <summary>The user the records belong to.</summary>
        public int UserId { get; } = aUserId;

        /// <summary><c>owner/name</c> of the source repository.</summary>
        public string Repo { get; } = aRepo;

        /// <summary>The SHA the file was fetched at.</summary>
        public string SourceSha { get; } = aSourceSha;

        /// <summary>Which stream is being parsed.</summary>
        public StreamKind Stream { get; } = aStream;

        /// <summary>Run records read so far, before dedupe.</summary>
        public List<RunRecord> Runs { get; } = [];

        /// <summary>Gate records read so far, before dedupe.</summary>
        public List<GateRecord> Gates { get; } = [];

        /// <summary>Session records read so far, before dedupe.</summary>
        public List<SessionRecord> Sessions { get; } = [];

        /// <summary>Commit records read so far, before dedupe.</summary>
        public List<CommitRecord> Commits { get; } = [];

        /// <summary>Playbook event records read so far, before dedupe.</summary>
        public List<PbEventRecord> PbEvents { get; } = [];

        /// <summary><c>miss</c> records read so far, before dedupe.</summary>
        public List<MissRecord> Misses { get; } = [];

        /// <summary><c>miss-fix</c> records read so far, before dedupe.</summary>
        public List<MissFixRecord> MissFixes { get; } = [];

        /// <summary><c>miss-amend</c> records read so far, before dedupe.</summary>
        public List<MissAmendRecord> MissAmends { get; } = [];

        /// <summary>Distinct field names SCHEMA.md does not document, in first-seen order.</summary>
        public HashSet<string> UnknownFields { get; } = new(StringComparer.Ordinal);

        /// <summary>Lines that were not valid JSON.</summary>
        public int InvalidLines { get; set; }

        /// <summary>Records whose schema version was greater than 1.</summary>
        public int RecordsAboveSchemaV1 { get; set; }

        /// <summary>
        /// The Playbook process gate the file is currently inside — the <c>command</c> of the most recent
        /// <c>phase-start</c> record, stamped onto the <c>turn</c> and <c>phase-end</c> records that
        /// follow it. <c>null</c> before the first <c>phase-start</c>.
        /// </summary>
        public string? CurrentPhaseGate { get; set; }

        /// <summary>
        /// Applies the stream's dedupe rule and produces the immutable result.
        /// </summary>
        /// <returns>The parse result, deduped on the stream's natural key.</returns>
        public ParseResult ToResult()
        {
            var vRuns = Dedupe.Runs(Runs);
            var vGates = Dedupe.Gates(Gates);
            var vSessions = Dedupe.Sessions(Sessions);
            var vCommits = Dedupe.Commits(Commits);
            var vEvents = Dedupe.PbEvents(PbEvents);
            var vMisses = Dedupe.Misses(Misses);
            var vMissFixes = Dedupe.MissFixes(MissFixes);
            var vMissAmends = Dedupe.MissAmends(MissAmends);

            return new ParseResult
            {
                UserId = UserId,
                Repo = Repo,
                SourceSha = SourceSha,
                Stream = Stream,
                Runs = vRuns.Records,
                Gates = vGates.Records,
                Sessions = vSessions.Records,
                Commits = vCommits.Records,
                PbEvents = vEvents.Records,
                Misses = vMisses.Records,
                MissFixes = vMissFixes.Records,
                MissAmends = vMissAmends.Records,
                InvalidLines = InvalidLines,
                DuplicatesCollapsed = vRuns.Collapsed + vGates.Collapsed + vSessions.Collapsed
                    + vCommits.Collapsed + vEvents.Collapsed
                    + vMisses.Collapsed + vMissFixes.Collapsed + vMissAmends.Collapsed,
                SessionDuplicatesCollapsed = vSessions.Collapsed,
                UnknownFields = UnknownFields.OrderBy(aN => aN, StringComparer.Ordinal).ToList(),
                RecordsAboveSchemaV1 = RecordsAboveSchemaV1
            };
        }
    }
}
