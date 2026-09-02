using System.Globalization;
using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// The words and export keys the Playbook axis of <c>/effort</c> is fixed to
/// (REQ-FN-097, REQ-FN-098, REQ-FN-101, BRD-163).
/// </summary>
/// <remarks>
/// They live in one place because each of them is a requirement rather than a caption. "Command phase"
/// is a promise that no command window was split between conceptual stages; the unsupported message is
/// a promise that a data gap is not a zero; and the <c>zero-unverified</c> caveat is a promise that a
/// provider's hardcoded zero is never rendered as free.
/// </remarks>
public static class PlaybookPhaseVocabulary
{
    /// <summary>The label of the phase dimension — never "phase", which reads as a lifecycle stage.</summary>
    /// <remarks>
    /// The producer's <c>phase</c> is the slash command, and one command can contain several conceptual
    /// stages (<c>/implement</c> is build <i>and</i> self-review). Splitting its tokens between them by
    /// proportion would be arithmetic dressed as measurement, so the dimension is named for what was
    /// actually measured (BRD-157, §3.3).
    /// </remarks>
    public const string CommandPhaseLabel = "Command phase";

    /// <summary>The export key of the same dimension, in the API's snake_case.</summary>
    public const string CommandPhaseKey = "command_phase";

    /// <summary>What a harness with no normalized phase producer renders (BRD-163).</summary>
    public const string UnsupportedHarnessMessage = "Phase effort telemetry unsupported for this harness";

    /// <summary>What any figure with nothing behind it renders — never <c>0</c>.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The prefix that marks a partial-coverage active time as an explicit lower bound.</summary>
    public const string LowerBoundPrefix = "at least ";

    /// <summary>What an open window renders where an elapsed time would be.</summary>
    public const string OpenWindowMessage = "Phase end not observed; elapsed time unavailable.";

    /// <summary>What a partial active coverage renders beside its lower bound.</summary>
    public const string PartialCoverageMessage = "Observed active time is a lower bound.";

    /// <summary>The caveat a zero provider cost against non-zero tokens carries — never "free", never $0.</summary>
    public const string ZeroUnverifiedCaveat =
        "The provider reported no dollars against non-zero tokens. The OpenCode v2 engine can return a "
        + "hardcoded zero, so this is unverified rather than free, and it enters no measured-cost total.";

    /// <summary>How the difference between spawned and contributing children is described.</summary>
    /// <remarks>
    /// Never "failed". A child that produced no tokens is a zero-token child; inferring a failure from
    /// the arithmetic would be a conclusion the transcripts do not support (BRD-159, §6).
    /// </remarks>
    public const string NonContributingChildLabel = "zero-token / non-contributing child";

    /// <summary>The name observed active time is published under, and the three it is never published under.</summary>
    /// <remarks>
    /// It is busy wall time observed across assistant and tool intervals with overlaps counted once. It
    /// is not human effort, not CPU time, not utilization and not additive compute (§7, ADR-027).
    /// </remarks>
    public const string ObservedActiveLabel = "Observed active time";

    /// <summary>The coverage value that admits an execution to an active-time comparison.</summary>
    public const string CoverageComplete = "complete";

    /// <summary>The coverage value that makes the figure a lower bound.</summary>
    public const string CoveragePartial = "partial";

    /// <summary>The status a complete token window or provider cost carries.</summary>
    public const string StatusComplete = "complete";

    /// <summary>A provider cost of zero dollars against non-zero tokens.</summary>
    public const string StatusZeroUnverified = "zero-unverified";

    /// <summary>A cost that some contributing model did not report completely.</summary>
    public const string StatusPartial = "partial";
}

/// <summary>The three cases a phase figure can be — a number, too few records, or nothing at all.</summary>
public enum PhaseValueKind
{
    /// <summary>A real number, computed from the cohort stated beside it.</summary>
    Measured = 0,

    /// <summary>Fewer than <see cref="MetricsConstants.MinN"/> records in a comparative cohort.</summary>
    InsufficientData = 1,

    /// <summary>Nothing eligible supported the figure; it renders as <c>unavailable</c>, never as zero.</summary>
    Unavailable = 2
}

/// <summary>
/// A phase figure, or an honest statement of why there is none (REQ-FN-102).
/// </summary>
/// <remarks>
/// The same technique as <see cref="Figure"/>, with one difference that matters here: a <i>total</i> is
/// legitimate at <c>n = 1</c> while a <i>comparison</i> is not, so the three-record floor is applied by
/// the builder that forms a comparative cohort rather than by the constructor. There is deliberately no
/// accessor that returns a default: an unavailable figure has no number to read.
/// </remarks>
public readonly record struct PhaseValue
{
    private readonly double objValue;
    private readonly string? objRendered;

    private PhaseValue(PhaseValueKind aKind, double aValue, int aRecords, string? aRendered)
    {
        Kind = aKind;
        objValue = aValue;
        Records = aRecords;
        objRendered = aRendered;
    }

    /// <summary>Which of the three cases this figure is.</summary>
    public PhaseValueKind Kind { get; }

    /// <summary>How many records supported — or failed to support — the figure.</summary>
    public int Records { get; }

    /// <summary>True only when the figure carries a number that may be rendered.</summary>
    public bool HasValue => Kind == PhaseValueKind.Measured;

    /// <summary>
    /// Builds a measured figure.
    /// </summary>
    /// <param name="aValue">The computed value.</param>
    /// <param name="aRecords">The records it was computed from.</param>
    /// <param name="aRendered">The display string, when it is not simply the number.</param>
    /// <returns>A measured figure, or an unavailable one when no record supported it.</returns>
    public static PhaseValue Measured(double aValue, int aRecords, string? aRendered = null) =>
        aRecords <= 0
            ? Unavailable()
            : new PhaseValue(PhaseValueKind.Measured, aValue, aRecords, aRendered);

    /// <summary>
    /// Builds a comparative figure, refusing to be a number below the three-record floor.
    /// </summary>
    /// <param name="aValue">The computed value.</param>
    /// <param name="aRecords">The records in the cohort.</param>
    /// <param name="aRendered">The display string, when it is not simply the number.</param>
    /// <returns>A measured figure, <c>insufficient data</c>, or unavailable on an empty cohort.</returns>
    public static PhaseValue Comparative(double aValue, int aRecords, string? aRendered = null)
    {
        if (aRecords <= 0)
        {
            return Unavailable();
        }

        return aRecords < MetricsConstants.MinN
            ? InsufficientData(aRecords)
            : new PhaseValue(PhaseValueKind.Measured, aValue, aRecords, aRendered);
    }

    /// <summary>
    /// Builds a figure that refuses to be a number because too few records support it.
    /// </summary>
    /// <param name="aRecords">How many records there were.</param>
    /// <returns>An <see cref="PhaseValueKind.InsufficientData"/> figure.</returns>
    public static PhaseValue InsufficientData(int aRecords) =>
        new(PhaseValueKind.InsufficientData, 0d, aRecords, null);

    /// <summary>
    /// Builds the figure an empty, absent, unreadable or unsupported cohort produces.
    /// </summary>
    /// <returns>An <see cref="PhaseValueKind.Unavailable"/> figure.</returns>
    public static PhaseValue Unavailable() => new(PhaseValueKind.Unavailable, 0d, 0, null);

    /// <summary>
    /// Reads the number out of a measured figure.
    /// </summary>
    /// <param name="aValue">Receives the value when this figure carries one.</param>
    /// <returns><c>true</c> when a number was available.</returns>
    public bool TryGetValue(out double aValue)
    {
        aValue = objValue;
        return Kind == PhaseValueKind.Measured;
    }

    /// <summary>
    /// The display string for the figure.
    /// </summary>
    /// <returns>The rendered value, <c>insufficient data (n=…)</c>, or <c>unavailable</c>.</returns>
    public string Display() => Kind switch
    {
        PhaseValueKind.Measured => objRendered ?? objValue.ToString(CultureInfo.InvariantCulture),
        PhaseValueKind.InsufficientData => $"insufficient data (n={Records})",
        _ => PlaybookPhaseVocabulary.Unavailable
    };

    /// <summary>Renders the figure through <see cref="Display"/>.</summary>
    /// <returns>The display string.</returns>
    public override string ToString() => Display();
}

/// <summary>One reason records were left out of a cohort, and how many were.</summary>
/// <param name="Code">The stable code, for the export.</param>
/// <param name="Explanation">The sentence rendered beside the figure — never in a global footer.</param>
/// <param name="Records">How many records the reason excluded.</param>
public sealed record PhaseExclusion(string Code, string Explanation, int Records);

/// <summary>
/// A figure with the cohort it rests on, and the exclusions stated <b>beside</b> it (REQ-FN-102).
/// </summary>
/// <remarks>
/// The counts travel with the figure rather than in a page footer for the same reason
/// <c>FanoutObservation</c> carries its denominator: a reader who can see the number must be able to see
/// what it was computed over without looking anywhere else.
/// </remarks>
/// <param name="Key">The export key, snake_case.</param>
/// <param name="Label">The label shown on the page.</param>
/// <param name="Value">The figure itself.</param>
/// <param name="N">Records in the cohort.</param>
/// <param name="Exclusions">Why records were left out, and how many each reason cost.</param>
public sealed record PhaseFigure(
    string Key,
    string Label,
    PhaseValue Value,
    int N,
    IReadOnlyList<PhaseExclusion> Exclusions)
{
    /// <summary>The caption a page renders under the figure: its <c>n</c> and every exclusion.</summary>
    public string Caption =>
        Exclusions.Count == 0
            ? $"n={N}"
            : $"n={N} · " + string.Join(" · ", Exclusions.Select(aE => $"{aE.Explanation} ({aE.Records})"));
}

/// <summary>
/// A timing component the producer publishes for diagnosis only, and that no aggregate accepts
/// (REQ-FN-097, ADR-027).
/// </summary>
/// <remarks>
/// <b>It carries no number.</b> <c>assistant_elapsed_ms</c> and <c>tool_elapsed_ms</c> legitimately
/// overlap — an assistant envelope can contain tool execution, which is exactly why the producer
/// publishes a single unioned <c>observed_active_ms</c> — so their sum is a number with no referent, and
/// it is the number a well-meaning contributor computes because both values are right there. Exposing
/// them as rendered text makes the wrong sum a compile-time absence rather than a rule someone has to
/// remember.
/// </remarks>
/// <param name="Key">The export key.</param>
/// <param name="Label">The label shown on the page.</param>
/// <param name="Text">The rendered value, or <c>unavailable</c>.</param>
public sealed record PhaseDiagnostic(string Key, string Label, string Text);

/// <summary>The five token legs of a cohort, each carrying the cohort it was summed over.</summary>
/// <param name="Input">Input tokens.</param>
/// <param name="Output">Output tokens.</param>
/// <param name="Reasoning">Reasoning tokens.</param>
/// <param name="CacheRead">Cache-read tokens.</param>
/// <param name="CacheWrite">Cache-write tokens.</param>
/// <param name="N">Records in the cohort.</param>
/// <param name="Exclusions">Why records were left out, and how many each reason cost.</param>
public sealed record PhaseTokenTotals(
    PhaseValue Input,
    PhaseValue Output,
    PhaseValue Reasoning,
    PhaseValue CacheRead,
    PhaseValue CacheWrite,
    int N,
    IReadOnlyList<PhaseExclusion> Exclusions);

/// <summary>
/// Measured provider dollars, and nothing else (REQ-FN-101).
/// </summary>
/// <remarks>
/// There is deliberately no estimate member. A rate-card figure is an input, not spend, and the moment
/// it shares a record with measured cost the label that distinguishes them stops travelling with the
/// number. <see cref="Usd"/> is <c>null</c> — never <c>0</c> — when no execution qualified.
/// </remarks>
/// <param name="Usd">The measured total, or <c>null</c> when nothing qualified.</param>
/// <param name="N">Executions that contributed.</param>
/// <param name="Exclusions">Why executions were left out, and how many each reason cost.</param>
public sealed record PhaseMeasuredCost(decimal? Usd, int N, IReadOnlyList<PhaseExclusion> Exclusions)
{
    /// <summary>The display string — the dollars, or <c>unavailable</c> when there are none to state.</summary>
    public string Display() =>
        Usd is null
            ? PlaybookPhaseVocabulary.Unavailable
            : Usd.Value.ToString("C4", CultureInfo.InvariantCulture);
}

/// <summary>
/// One execution's cost, told through the producer's status rather than through its number
/// (REQ-FN-101).
/// </summary>
/// <param name="Status">The producer's <c>cost_status</c>, possibly demoted to <c>partial</c> by a model row.</param>
/// <param name="Usd">The provider figure, kept for display only when it is not measured.</param>
/// <param name="IsMeasured">True only when the figure may enter a measured-cost total.</param>
/// <param name="Caveat">The engine caveat, when the status carries one.</param>
public sealed record PhaseCostView(string? Status, decimal? Usd, bool IsMeasured, string? Caveat)
{
    /// <summary>The display string — dollars only when measured, otherwise the status, never <c>$0</c>.</summary>
    public string Display() =>
        IsMeasured && Usd is not null
            ? Usd.Value.ToString("C4", CultureInfo.InvariantCulture)
            : Status ?? PlaybookPhaseVocabulary.Unavailable;
}

/// <summary>Whether this harness has a normalized phase producer at all (BRD-163).</summary>
/// <param name="IsSupported">True when the harness emits the schema-2 record.</param>
/// <param name="Message">What to render when it does not; <c>null</c> when it does.</param>
public sealed record PhaseHarnessSupport(bool IsSupported, string? Message)
{
    /// <summary>
    /// Resolves the support state for a harness.
    /// </summary>
    /// <remarks>
    /// A harness with no adapter is a <b>data gap</b>, which is a different fact from a harness that ran
    /// and spent nothing. Rendering zero for it would state the second while measuring the first.
    /// </remarks>
    /// <param name="aHarness">The harness name, or <c>null</c> when none was detected.</param>
    /// <returns>The support state.</returns>
    public static PhaseHarnessSupport For(string? aHarness) =>
        PlaybookPhaseAdapter.IsHarnessSupported(aHarness)
            ? new PhaseHarnessSupport(true, null)
            : new PhaseHarnessSupport(false, PlaybookPhaseVocabulary.UnsupportedHarnessMessage);
}

/// <summary>The data-quality counts every comparison on the page is read against.</summary>
/// <param name="Records">Executions stored.</param>
/// <param name="Completed">Windows that closed.</param>
/// <param name="Incomplete">Windows that ended at EOF.</param>
/// <param name="ActiveComplete">Windows whose active coverage was complete.</param>
/// <param name="ActivePartial">Windows whose active coverage was a lower bound.</param>
/// <param name="ActiveUnavailable">Windows with no observable active interval.</param>
/// <param name="Quarantined">Rows excluded from every numeric aggregate.</param>
/// <param name="LegacyUnverified">Schema-1 rows, reachable by drill-down and absent from comparisons.</param>
public sealed record PhaseQuality(
    int Records,
    int Completed,
    int Incomplete,
    int ActiveComplete,
    int ActivePartial,
    int ActiveUnavailable,
    int Quarantined,
    int LegacyUnverified);

/// <summary>One model's own usage, aggregated from <c>"PbPhaseModelUsage"</c> (REQ-FN-099).</summary>
/// <remarks>
/// Never from <see cref="PbPhaseExecutionRecord.DominantModel"/>, which is a label: assigning a whole
/// mixed-model execution to the model that answered most turns is the misattribution BRD-150 forbids.
/// </remarks>
/// <param name="Model">The model, exactly as the producer named it.</param>
/// <param name="Executions">Executions this model appeared in.</param>
/// <param name="Turns">Turns it answered, summed where reported.</param>
/// <param name="TokensIn">Its own input-side tokens.</param>
/// <param name="TokensOut">Its own output-side tokens.</param>
/// <param name="MeasuredCostUsd">Its own measured dollars, or <c>null</c> when none were complete.</param>
public sealed record PhaseModelUsageView(
    string Model,
    int Executions,
    long Turns,
    long TokensIn,
    long TokensOut,
    decimal? MeasuredCostUsd);

/// <summary>
/// One sub-agent session in the tree, with its own children beneath it (REQ-FN-100).
/// </summary>
/// <param name="SessionId">The child's session id.</param>
/// <param name="ParentSessionId">The session that spawned it, when one was named.</param>
/// <param name="AgentDisplay">The agent type, or <c>unavailable</c> — never inferred.</param>
/// <param name="TokensOut">Its output-side tokens, when reported.</param>
/// <param name="CostUsd">Its provider cost, when reported.</param>
/// <param name="Children">Sessions it spawned in turn.</param>
public sealed record PhaseSubagentNode(
    string SessionId,
    string? ParentSessionId,
    string AgentDisplay,
    long? TokensOut,
    decimal? CostUsd,
    IReadOnlyList<PhaseSubagentNode> Children);

/// <summary>
/// Sub-agent fan-out for one execution, as <c>contributors / spawned</c> (REQ-FN-100).
/// </summary>
/// <remarks>
/// The difference is a zero-token child, not an inferred failure, and the child tokens are already
/// inside the phase totals — the tree is a <i>drill-down</i>, and adding it to the totals would count
/// the same work twice (§6).
/// </remarks>
/// <param name="Contributors">Sessions that produced tokens.</param>
/// <param name="Spawned">Sessions launched, including the ones that produced nothing.</param>
/// <param name="NonContributing">The difference, when both counts were reported.</param>
/// <param name="ChildTokenShare">Child share of output tokens, only where the denominator is positive.</param>
/// <param name="Tree">The recursive session tree; every session appears exactly once.</param>
public sealed record PhaseFanoutView(
    int? Contributors,
    int? Spawned,
    int? NonContributing,
    PhaseValue ChildTokenShare,
    IReadOnlyList<PhaseSubagentNode> Tree)
{
    /// <summary>The headline, e.g. <c>1 / 3</c>, or <c>unavailable</c> when the window never looked.</summary>
    public string Display() =>
        Contributors is null || Spawned is null
            ? PlaybookPhaseVocabulary.Unavailable
            : $"{Contributors} / {Spawned}";

    /// <summary>How the difference is described — a zero-token child, never a failure.</summary>
    public string NonContributingLabel => PlaybookPhaseVocabulary.NonContributingChildLabel;
}

/// <summary>
/// One row of the execution table: everything the producer measured, and everything it did not.
/// </summary>
/// <remarks>
/// A quarantined row is here like any other, with <see cref="QuarantineReasons"/> in place of its
/// numbers. It is visible precisely so a reader does not conclude the work never happened — and it is in
/// no cohort, so none of its retained zeroes reach a total.
/// </remarks>
public sealed record PhaseExecutionView
{
    /// <summary>The producer's stable id for the execution.</summary>
    public required string PhaseExecutionId { get; init; }

    /// <summary>The command that ran — a <b>command phase</b>, never a lifecycle stage.</summary>
    public required string? CommandPhase { get; init; }

    /// <summary>ISO-8601 UTC start of the window; display localizes, storage and filtering do not.</summary>
    public string? StartedAtUtc { get; init; }

    /// <summary>Whether the window closed.</summary>
    public bool? IsComplete { get; init; }

    /// <summary>Why the window ended.</summary>
    public string? EndReason { get; init; }

    /// <summary>Wall-clock duration — present only on a closed window.</summary>
    public PhaseValue ElapsedMs { get; init; }

    /// <summary>
    /// The producer's union of assistant and tool intervals, overlaps counted once.
    /// </summary>
    /// <remarks>
    /// Unavailable coverage yields no figure at all; partial coverage yields an explicit lower bound.
    /// It is never labelled human effort, CPU time, utilization or additive compute.
    /// </remarks>
    public PhaseValue ObservedActiveMs { get; init; }

    /// <summary>The producer's coverage word, which the active figure must be read with.</summary>
    public string? ActiveCoverage { get; init; }

    /// <summary>The assistant-interval sum, as text; never added to <see cref="ToolElapsed"/>.</summary>
    public required PhaseDiagnostic AssistantElapsed { get; init; }

    /// <summary>The tool-interval sum, as text; never added to <see cref="AssistantElapsed"/>.</summary>
    public required PhaseDiagnostic ToolElapsed { get; init; }

    /// <summary>Input tokens over the window.</summary>
    public long? TokensInput { get; init; }

    /// <summary>Output tokens over the window.</summary>
    public long? TokensOutput { get; init; }

    /// <summary>Reasoning tokens over the window.</summary>
    public long? TokensReasoning { get; init; }

    /// <summary>Cache-read tokens over the window.</summary>
    public long? TokensCacheRead { get; init; }

    /// <summary>Cache-write tokens over the window.</summary>
    public long? TokensCacheWrite { get; init; }

    /// <summary>Assistant turns the producer finalized.</summary>
    public int? Turns { get; init; }

    /// <summary>The cost, told through its status.</summary>
    public required PhaseCostView Cost { get; init; }

    /// <summary>Every model that ran, from the per-model rows — a filter matches any of them.</summary>
    public required IReadOnlyList<string> Models { get; init; }

    /// <summary>The dominant-model label; displayed, never aggregated on.</summary>
    public string? DominantModelLabel { get; init; }

    /// <summary>Sub-agent fan-out and the session tree.</summary>
    public required PhaseFanoutView Fanout { get; init; }

    /// <summary>True when the row may not enter any numeric aggregate.</summary>
    public bool IsQuarantined { get; init; }

    /// <summary>Why it is quarantined; empty on a clean row.</summary>
    public required IReadOnlyList<PhaseQuarantineReason> QuarantineReasons { get; init; }

    /// <summary>The producer's token status; <c>legacy-unverified</c> is drill-down only.</summary>
    public string? TokenStatus { get; init; }

    /// <summary>The snapshot of the attempt number when the window ended — never a historical outcome.</summary>
    public int? AttemptSnapshot { get; init; }

    /// <summary>The snapshot of the gate verdict when the window ended — never a historical outcome.</summary>
    public string? GateVerdictSnapshot { get; init; }

    /// <summary>The declared or inferred project type.</summary>
    public string? ProjectType { get; init; }

    /// <summary>The data-quality sentence a reader needs, or <c>null</c> when the row needs none.</summary>
    public string? DataQualityNote { get; init; }
}

/// <summary>
/// An explicitly supplied whole-task cohort (REQ-FN-098, BRD-157).
/// </summary>
/// <remarks>
/// <para>
/// The producer emits no trustworthy cross-command task id, so a task total is only ever computed over a
/// cohort the ingestion workflow states outright: the repository, the checklist identity, and either the
/// exact phase execution ids or a UTC time boundary.
/// </para>
/// <para>
/// <b>A reused session id is not a cohort.</b> One OpenCode session may execute several tasks, so
/// grouping by it would silently pool unrelated work into a total that looks authoritative. There is
/// deliberately no constructor that accepts one.
/// </para>
/// </remarks>
public sealed record PhaseTaskCohort
{
    private PhaseTaskCohort(
        string aRepo, string aChecklistId, IReadOnlyList<string> aExecutionIds, string? aFrom, string? aTo)
    {
        Repo = aRepo;
        ChecklistId = aChecklistId;
        ExecutionIds = aExecutionIds;
        FromUtc = aFrom;
        ToUtc = aTo;
    }

    /// <summary><c>owner/name</c> of the repository the task ran in.</summary>
    public string Repo { get; }

    /// <summary>The checklist the task belongs to.</summary>
    public string ChecklistId { get; }

    /// <summary>The exact executions in the task; empty when a time boundary was supplied instead.</summary>
    public IReadOnlyList<string> ExecutionIds { get; }

    /// <summary>Inclusive UTC start of the boundary, when one was supplied.</summary>
    public string? FromUtc { get; }

    /// <summary>Exclusive UTC end of the boundary, when one was supplied.</summary>
    public string? ToUtc { get; }

    /// <summary>
    /// Builds a cohort, refusing anything that would let a total be inferred rather than stated.
    /// </summary>
    /// <param name="aRepo">The repository; required.</param>
    /// <param name="aChecklistId">The checklist identity; required.</param>
    /// <param name="aExecutionIds">The exact execution ids, or <c>null</c> when using a boundary.</param>
    /// <param name="aFromUtc">Inclusive UTC start, or <c>null</c> when using explicit ids.</param>
    /// <param name="aToUtc">Exclusive UTC end, or <c>null</c> when using explicit ids.</param>
    /// <param name="aCohort">The cohort when the method returns <c>true</c>.</param>
    /// <returns><c>false</c> when the caller supplied less than a cohort — the total is then unavailable.</returns>
    public static bool TryCreate(
        string? aRepo,
        string? aChecklistId,
        IEnumerable<string>? aExecutionIds,
        string? aFromUtc,
        string? aToUtc,
        out PhaseTaskCohort? aCohort)
    {
        aCohort = null;

        var vIds = aExecutionIds?.Where(aId => !string.IsNullOrWhiteSpace(aId)).ToList() ?? [];
        var vHasBoundary = !string.IsNullOrWhiteSpace(aFromUtc) && !string.IsNullOrWhiteSpace(aToUtc);

        if (string.IsNullOrWhiteSpace(aRepo) || string.IsNullOrWhiteSpace(aChecklistId))
        {
            return false;
        }

        if (vIds.Count == 0 && !vHasBoundary)
        {
            return false;
        }

        aCohort = new PhaseTaskCohort(aRepo, aChecklistId, vIds, aFromUtc, aToUtc);
        return true;
    }

    /// <summary>
    /// Tells whether one execution belongs to this cohort.
    /// </summary>
    /// <param name="aExecution">The execution row.</param>
    /// <returns><c>true</c> when the cohort names it, or its UTC start falls inside the boundary.</returns>
    public bool Contains(PbPhaseExecutionRecord aExecution)
    {
        ArgumentNullException.ThrowIfNull(aExecution);

        if (!string.Equals(aExecution.Repo, Repo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ExecutionIds.Count > 0
            ? ExecutionIds.Contains(aExecution.PhaseExecutionId, StringComparer.Ordinal)
            : IsInsideBoundary(aExecution.StartedAt);
    }

    /// <summary>Tells whether an ISO-8601 instant falls inside the cohort's UTC boundary.</summary>
    /// <param name="aStartedAt">The execution's start, as stored.</param>
    /// <returns><c>true</c> when it parses and falls inside.</returns>
    private bool IsInsideBoundary(string? aStartedAt)
    {
        if (!TryUtc(aStartedAt, out var vStarted)
            || !TryUtc(FromUtc, out var vFrom)
            || !TryUtc(ToUtc, out var vTo))
        {
            return false;
        }

        return vStarted >= vFrom && vStarted < vTo;
    }

    /// <summary>Parses an instant as UTC — filtering is UTC and only display is localized.</summary>
    /// <param name="aText">The instant text.</param>
    /// <param name="aMoment">The parsed instant.</param>
    /// <returns><c>true</c> when the text parsed.</returns>
    private static bool TryUtc(string? aText, out DateTimeOffset aMoment) =>
        DateTimeOffset.TryParse(
            aText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out aMoment);
}
