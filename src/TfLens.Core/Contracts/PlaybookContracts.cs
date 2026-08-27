using System.Globalization;

namespace TfLens.Core.Contracts;

/// <summary>
/// A Playbook <b>process</b>-gate name — plan review, verify, gap report, post-verification bugs.
/// </summary>
/// <remarks>
/// SCHEMA.md §11 reserves <c>gate</c> for TechieFlow <i>assertion</i>-gates and <c>phase_gate</c> for
/// Playbook <i>process</i>-gates, and forbids the two axes sharing a table, column or chart. This type
/// is the structural half of that rule (REQ-FN-066): every Playbook result member that names a gate is
/// typed <see cref="PhaseGateKey"/>, while every TechieFlow gate member (<see cref="GateCount.Gate"/>,
/// <see cref="LateGateCoverage.Gate"/>) is a plain <see cref="string"/>. There is no implicit
/// conversion between them in either direction, so a phase-gate cannot be dropped into an assertion-gate
/// slot — or vice versa — by accident, and <c>PlaybookAxisSeparationTests</c> fails the build if a
/// future change puts the two into one shape.
/// </remarks>
public readonly record struct PhaseGateKey
{
    private readonly string? objName;

    private PhaseGateKey(string aName) => objName = aName;

    /// <summary>The bucket used when an event names no process gate.</summary>
    public const string Unattributed = "unattributed";

    /// <summary>The process-gate name as the Playbook emitted it, or <see cref="Unattributed"/>.</summary>
    public string Name => objName ?? Unattributed;

    /// <summary>
    /// Builds a key from a raw <c>phase_gate</c> value.
    /// </summary>
    /// <param name="aPhaseGate">The value the event carried; <c>null</c> or blank becomes <see cref="Unattributed"/>.</param>
    /// <returns>The key.</returns>
    public static PhaseGateKey From(string? aPhaseGate) =>
        new(string.IsNullOrWhiteSpace(aPhaseGate) ? Unattributed : aPhaseGate.Trim());

    /// <summary>Renders the key as its process-gate name.</summary>
    /// <returns>The value of <see cref="Name"/>.</returns>
    public override string ToString() => Name;
}

/// <summary>
/// The <c>kind</c> values the Playbook's telemetry plugin emits (REQ-FN-068).
/// </summary>
/// <remarks>
/// Read off <c>harness/opencode/plugin/telemetry.ts</c>: the plugin writes exactly these three and
/// nothing else. Recorded in <c>DECISIONS.md</c> §Playbook.
/// </remarks>
public static class PlaybookEventKinds
{
    /// <summary>Written on <c>command.execute.before</c>; the only record that names the phase.</summary>
    public const string PhaseStart = "phase-start";

    /// <summary>Written on every assistant <c>message.updated</c>; the only record that carries tokens, model and cost.</summary>
    public const string Turn = "turn";

    /// <summary>Written on <c>session.idle</c>; closes the phase.</summary>
    public const string PhaseEnd = "phase-end";

    /// <summary>The three kinds in emission order.</summary>
    public static readonly IReadOnlyList<string> All = [PhaseStart, Turn, PhaseEnd];
}

/// <summary>
/// The wire field names <c>events.ndjson</c> carries, exactly as the emitter spells them.
/// </summary>
/// <remarks>
/// The REQ-FN-068 record, in code as well as in <c>DECISIONS.md</c> §Playbook. Note the spelling: the
/// Playbook emits camelCase with capitalised acronyms (<c>sessionID</c>, not <c>session_id</c>), which is
/// a different convention from the four snake_case TechieFlow streams, and <c>tokens</c> is a nested
/// object rather than a scalar.
/// </remarks>
public static class PlaybookWireFields
{
    /// <summary>Record kind — <c>phase-start</c> | <c>turn</c> | <c>phase-end</c>. On every record.</summary>
    public const string Kind = "kind";

    /// <summary>ISO-8601 timestamp. Stamped by the emitter onto every record.</summary>
    public const string Ts = "ts";

    /// <summary>The phase command. <c>phase-start</c> only — the source of the derived process gate.</summary>
    public const string Command = "command";

    /// <summary>Arguments the phase command was invoked with. <c>phase-start</c> only.</summary>
    public const string Arguments = "arguments";

    /// <summary>Harness session id. On every record.</summary>
    public const string SessionId = "sessionID";

    /// <summary>Parent session id, <c>null</c> on a main session. <c>turn</c> only.</summary>
    public const string ParentId = "parentID";

    /// <summary>Assistant message id — the turn dedupe key. <c>turn</c> only.</summary>
    public const string MessageId = "messageID";

    /// <summary><c>providerID/modelID</c>. <c>turn</c> only.</summary>
    public const string Model = "model";

    /// <summary>Nested object: <c>input</c>, <c>output</c>, <c>reasoning</c>, <c>cache.read</c>, <c>cache.write</c>. <c>turn</c> only.</summary>
    public const string Tokens = "tokens";

    /// <summary>Measured USD for the turn. <c>turn</c> only.</summary>
    public const string Cost = "cost";

    /// <summary>Every top-level field name, in the order the emitter writes them.</summary>
    public static readonly IReadOnlyList<string> Names =
        [Kind, Command, SessionId, Arguments, ParentId, MessageId, Model, Tokens, Cost, Ts];
}

/// <summary>
/// Whether the Playbook column set has been confirmed against a real <c>events.ndjson</c>.
/// </summary>
/// <remarks>
/// ADR-010 / REQ-FN-068: the adapter's first task is to parse a real file and record the observed field
/// names in <c>DECISIONS.md</c> before any column or chart is fixed. Until that has happened the status
/// is <see cref="Provisional"/> and every Playbook figure is rendered with that caveat, so a provisional
/// number can never be mistaken for a discovered one.
/// </remarks>
public enum PlaybookSchemaStatus
{
    /// <summary>Nothing real has been read; the <c>"PbEvent"</c> columns are guesses from the brief's prose.</summary>
    Provisional = 0,

    /// <summary>
    /// The columns were read off the Playbook's own emitter source rather than a captured file.
    /// </summary>
    /// <remarks>
    /// Stronger than <see cref="Provisional"/> — the field names, wire spellings and types come from the
    /// code that writes the file, so no column is invented — but weaker than <see cref="Discovered"/>:
    /// value ranges, cardinalities and real-world edge cases are still unobserved, because no
    /// <c>events.ndjson</c> has ever been captured. Figures carry that caveat.
    /// </remarks>
    EmitterSourceDerived = 1,

    /// <summary>A real file was parsed and its field names are recorded in <c>DECISIONS.md</c> §Playbook.</summary>
    Discovered = 2
}

/// <summary>One field name seen in a real <c>events.ndjson</c>, as the schema probe observed it.</summary>
/// <param name="Name">The JSON property name exactly as emitted.</param>
/// <param name="Occurrences">How many records carried it.</param>
/// <param name="JsonKinds">The distinct JSON value kinds seen for it, sorted.</param>
/// <param name="SampleValues">Up to a handful of distinct values, for the DECISIONS.md entry.</param>
public sealed record ObservedField(
    string Name,
    int Occurrences,
    IReadOnlyList<string> JsonKinds,
    IReadOnlyList<string> SampleValues);

/// <summary>What the schema probe found in one raw Playbook stream file.</summary>
/// <param name="Records">Records the probe read.</param>
/// <param name="InvalidLines">Lines that were not valid JSON objects.</param>
/// <param name="Fields">Every observed field, most frequent first.</param>
public sealed record PlaybookSchemaObservation(
    int Records,
    int InvalidLines,
    IReadOnlyList<ObservedField> Fields)
{
    /// <summary>The observed field names in probe order — the list REQ-FN-068 requires in DECISIONS.md.</summary>
    public IReadOnlyList<string> FieldNames => Fields.Select(aF => aF.Name).ToList();
}

/// <summary>What one Playbook ingest pass fetched, archived, parsed and stored.</summary>
/// <param name="Repo"><c>owner/name</c> of the source repository.</param>
/// <param name="Sha">The commit SHA the files were fetched at.</param>
/// <param name="FilesFetched">Playbook stream files GitHub answered with content for.</param>
/// <param name="FilesAbsent">Playbook stream files GitHub answered 404 for — a legitimate "stream absent".</param>
/// <param name="RawArchivePaths">Where the bytes were archived, written before any parse (REQ-FN-027).</param>
/// <param name="RecordsWritten">Rows newly written to <c>"PbEvent"</c>.</param>
/// <param name="Observation">What the schema probe saw, or <c>null</c> when no file arrived.</param>
public sealed record PlaybookIngestResult(
    string Repo,
    string Sha,
    int FilesFetched,
    int FilesAbsent,
    IReadOnlyList<string> RawArchivePaths,
    int RecordsWritten,
    PlaybookSchemaObservation? Observation);

/// <summary>Event, session, token and cost totals for one Playbook process gate.</summary>
/// <param name="PhaseGate">The process gate — never a TechieFlow assertion gate (SCHEMA.md §11).</param>
/// <param name="Events">Event records attributed to it.</param>
/// <param name="Sessions">Distinct sessions that touched it.</param>
/// <param name="Tokens">Token total across its events; absent token fields contribute nothing, never zero.</param>
/// <param name="CostUsd">Measured spend, or <c>null</c> when the events carry none — rendered <c>—</c>.</param>
public sealed record PhaseGateTotals(
    PhaseGateKey PhaseGate,
    int Events,
    int Sessions,
    long Tokens,
    decimal? CostUsd);

/// <summary>
/// The Playbook-native equivalents of the three questions, computed for one process gate.
/// </summary>
/// <remarks>
/// The figures obey the same minimum-n rule as the TechieFlow engine because they are
/// <see cref="Figure"/> values, which cannot carry a number below <see cref="MetricsConstants.MinN"/>
/// supporting records. They stay <see cref="FigureKind.NotApplicable"/> — with
/// <paramref name="UnavailableReason"/> saying why — until the verdict vocabulary mapping is recorded
/// in <c>DECISIONS.md</c> §Playbook (SCHEMA.md §11, Architecture §12): inventing that mapping from the
/// brief's prose is exactly what ADR-010 forbids.
/// </remarks>
/// <param name="PhaseGate">The process gate the questions are asked about.</param>
/// <param name="FirstPassRate">Work that cleared this gate on its first attempt.</param>
/// <param name="CatchShare">This gate's share of the defects some gate caught.</param>
/// <param name="EscapeRate">Work no gate caught before it escaped.</param>
/// <param name="SupportingEvents">Events the three figures were computed over.</param>
/// <param name="UnavailableReason">Why the figures are not applicable, or <c>null</c> when they are computed.</param>
public sealed record PhaseGateQuestions(
    PhaseGateKey PhaseGate,
    Figure FirstPassRate,
    Figure CatchShare,
    Figure EscapeRate,
    int SupportingEvents,
    string? UnavailableReason);

/// <summary>
/// The main-vs-subagent split, resolved through the <c>parentID</c> chain.
/// </summary>
/// <remarks>
/// A session whose events carry a parent id is a sub-agent session; the chain is walked to its root so a
/// sub-agent of a sub-agent still resolves to the main session that started the work. A parent id that
/// names a session no event ever reports is counted in <see cref="UnresolvedParentSessions"/> rather
/// than being silently promoted to a main session.
/// </remarks>
/// <param name="MainSessions">Sessions with no parent — the roots of the chains.</param>
/// <param name="MainTokens">Tokens spent directly in main sessions.</param>
/// <param name="MainCostUsd">Measured spend in main sessions, or <c>null</c> when the events carry none.</param>
/// <param name="SubagentSessions">Sessions that resolved to a parent.</param>
/// <param name="SubagentTokens">Tokens spent in sub-agent sessions.</param>
/// <param name="SubagentCostUsd">Measured spend in sub-agent sessions, or <c>null</c>.</param>
/// <param name="UnresolvedParentSessions">Sub-agent sessions whose parent chain never reached a known root.</param>
public sealed record PlaybookAgentSplit(
    int MainSessions,
    long MainTokens,
    decimal? MainCostUsd,
    int SubagentSessions,
    long SubagentTokens,
    decimal? SubagentCostUsd,
    int UnresolvedParentSessions)
{
    /// <summary>Sessions on both sides of the split.</summary>
    public int SessionsTotal => MainSessions + SubagentSessions;

    /// <summary>Tokens on both sides of the split.</summary>
    public long TokensTotal => MainTokens + SubagentTokens;

    /// <summary>
    /// The sub-agent share of tokens, subject to the same minimum-n rule as every other figure.
    /// </summary>
    /// <remarks>Not applicable when no tokens were reported at all — a zero denominator is never a zero share.</remarks>
    public Figure SubagentTokenShare =>
        TokensTotal <= 0
            ? Figure.NotApplicable()
            : SessionsTotal < MetricsConstants.MinN
                ? Figure.InsufficientData(SessionsTotal)
                : Figure.Value(
                    (double)SubagentTokens / TokensTotal,
                    SessionsTotal,
                    (100.0 * SubagentTokens / TokensTotal).ToString("F0", CultureInfo.InvariantCulture) + "%");
}

/// <summary>Per-repository facts for the Playbook state of the Coverage page.</summary>
/// <param name="Repo"><c>owner/name</c> of the repository.</param>
/// <param name="Events">Event records stored for it.</param>
/// <param name="Sessions">Distinct sessions seen in those events.</param>
/// <param name="PhaseGates">Distinct process gates seen in those events.</param>
/// <param name="EarliestTs">ISO-8601 timestamp of its earliest event, or <c>null</c>.</param>
/// <param name="LatestTs">ISO-8601 timestamp of its latest event, or <c>null</c>.</param>
public sealed record PlaybookRepoFacts(
    string Repo,
    int Events,
    int Sessions,
    int PhaseGates,
    string? EarliestTs,
    string? LatestTs);

/// <summary>
/// The whole Playbook-native report set for one user, computed from the separate <c>"PbEvent"</c> table.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="AnalysisResult"/> for the Playbook axis, and deliberately not the same
/// type: <see cref="AnalysisResult"/> is keyed by TechieFlow assertion gates and project types, this one
/// by <see cref="PhaseGateKey"/>. <see cref="Framework"/> is computed, not settable, so a Playbook
/// result can never claim to be a TechieFlow one and the two can never be pooled (ADR-016, REQ-FN-055).
/// </remarks>
public sealed record PlaybookAnalysis
{
    /// <summary>The user the figures belong to; every read is scoped by it (ADR-013).</summary>
    public required int UserId { get; init; }

    /// <summary>Always <see cref="FrameworkNames.Playbook"/> — this shape cannot express a TechieFlow figure.</summary>
    public string Framework => FrameworkNames.Playbook;

    /// <summary>Whether the columns behind these figures have been confirmed against a real file.</summary>
    public required PlaybookSchemaStatus SchemaStatus { get; init; }

    /// <summary>One line per repository the events came from.</summary>
    public required IReadOnlyList<PlaybookRepoFacts> PerRepo { get; init; }

    /// <summary>Token and cost totals per process gate, busiest first.</summary>
    public required IReadOnlyList<PhaseGateTotals> PhaseTotals { get; init; }

    /// <summary>The three questions per process gate, in the same order as <see cref="PhaseTotals"/>.</summary>
    public required IReadOnlyList<PhaseGateQuestions> PhaseQuestions { get; init; }

    /// <summary>The main-vs-subagent split resolved through <c>parentID</c>.</summary>
    public required PlaybookAgentSplit AgentSplit { get; init; }

    /// <summary>Token totals per observed model, empty when the events carry no model field.</summary>
    public required IReadOnlyList<ModelTokens> TokensByModel { get; init; }

    /// <summary>Field names observed in the raw files behind these figures — the REQ-FN-068 evidence.</summary>
    public required IReadOnlyList<string> ObservedFields { get; init; }

    /// <summary>Caveats the pages and the export must render beside these figures.</summary>
    public required IReadOnlyList<string> ProvisionalNotes { get; init; }

    /// <summary>The parser version that produced the figures.</summary>
    public required string ParserVersion { get; init; }

    /// <summary>Event records behind the whole result.</summary>
    public int EventsTotal => PerRepo.Sum(aR => aR.Events);

    /// <summary>
    /// Projects the result into the flat key layout the snapshot export writes (REQ-FN-070).
    /// </summary>
    /// <remarks>
    /// The keys are all prefixed <c>playbook.</c> so a snapshot for this framework cannot collide with —
    /// or be diffed against — a TechieFlow snapshot's keys. Figures are written through
    /// <see cref="Figure.Display"/>, so <c>insufficient data (n=…)</c> and <c>—</c> survive the export
    /// as themselves rather than becoming numbers.
    /// </remarks>
    /// <returns>The export payload, ordered for a stable diff.</returns>
    public IReadOnlyList<KeyValuePair<string, string>> ToExportPayload()
    {
        var vPayload = new List<KeyValuePair<string, string>>
        {
            new("playbook.framework", Framework),
            new("playbook.schemaStatus", SchemaStatus.ToString()),
            new("playbook.parserVersion", ParserVersion),
            new("playbook.eventsTotal", EventsTotal.ToString()),
            new("playbook.repos", PerRepo.Count.ToString()),
            new("playbook.split.mainSessions", AgentSplit.MainSessions.ToString()),
            new("playbook.split.mainTokens", AgentSplit.MainTokens.ToString()),
            new("playbook.split.subagentSessions", AgentSplit.SubagentSessions.ToString()),
            new("playbook.split.subagentTokens", AgentSplit.SubagentTokens.ToString()),
            new("playbook.split.unresolvedParents", AgentSplit.UnresolvedParentSessions.ToString()),
            new("playbook.split.subagentTokenShare", AgentSplit.SubagentTokenShare.Display())
        };

        foreach (var vTotals in PhaseTotals)
        {
            var vKey = "playbook.phaseGate." + vTotals.PhaseGate.Name;
            vPayload.Add(new KeyValuePair<string, string>(vKey + ".events", vTotals.Events.ToString()));
            vPayload.Add(new KeyValuePair<string, string>(vKey + ".sessions", vTotals.Sessions.ToString()));
            vPayload.Add(new KeyValuePair<string, string>(vKey + ".tokens", vTotals.Tokens.ToString()));
            vPayload.Add(new KeyValuePair<string, string>(vKey + ".costUsd", vTotals.CostUsd?.ToString() ?? "—"));
        }

        foreach (var vQuestions in PhaseQuestions)
        {
            var vKey = "playbook.phaseGate." + vQuestions.PhaseGate.Name;
            vPayload.Add(new KeyValuePair<string, string>(vKey + ".firstPassRate", vQuestions.FirstPassRate.Display()));
            vPayload.Add(new KeyValuePair<string, string>(vKey + ".catchShare", vQuestions.CatchShare.Display()));
            vPayload.Add(new KeyValuePair<string, string>(vKey + ".escapeRate", vQuestions.EscapeRate.Display()));
        }

        vPayload.AddRange(ProvisionalNotes.Select(
            (aNote, aIndex) => new KeyValuePair<string, string>($"playbook.note.{aIndex}", aNote)));

        return vPayload;
    }
}

/// <summary>
/// How a connected repository's telemetry is read — the adapter, or the schema-v1 path (REQ-FN-069).
/// </summary>
public enum TelemetryRoute
{
    /// <summary>
    /// <c>docs/metrics/*.jsonl</c> — the four schema-v1 streams, read by the same parser, engine and
    /// pages as a TechieFlow repository regardless of which framework the repository is tagged with.
    /// </summary>
    SchemaV1Streams = 0,

    /// <summary><c>verification/telemetry/events.ndjson</c> — read by <c>PlaybookAdapter</c>.</summary>
    PlaybookAdapter = 1
}
