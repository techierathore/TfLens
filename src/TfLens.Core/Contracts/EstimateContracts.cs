namespace TfLens.Core.Contracts;

/// <summary>
/// A forecast's low–mid–high triple (SCHEMA.md §2.8) — one estimate figure, whole.
/// </summary>
/// <remarks>
/// The three legs are what <c>tf-estimate.sh</c> wrote and nothing else: this type deliberately carries
/// no mean-of-actual, no delta and no ratio, because the fourth separation (SCHEMA.md §6, BRD-213,
/// ADR-034) forbids any member that would combine a forecast with a measurement. Values are
/// <c>long</c> because they are token and second counts a phase total sums across rows.
/// </remarks>
/// <param name="Low">The first quartile of the per-row history.</param>
/// <param name="Mid">The median — the figure a card headline shows, always beside its range.</param>
/// <param name="High">The third quartile.</param>
public sealed record EstimateRange(long Low, long Mid, long High);

/// <summary>
/// The estimate column of one reconciliation line: the two forecast figures and the history behind them.
/// </summary>
/// <remarks>
/// A <c>null</c> range is <b>no precedent</b> (<c>basis: "none"</c>) and renders in words, never as a
/// zero (BRD-216). <see cref="RowsCovered"/> is the <c>[n/m]</c> of the phase card: the rows this step
/// had a figure for, which are exactly the rows the actual beside it is measured over (BRD-212).
/// </remarks>
/// <param name="TokensOut">The output-token forecast, or <c>null</c> for no precedent.</param>
/// <param name="DurationS">The wall-seconds forecast, or <c>null</c> for no precedent.</param>
/// <param name="Basis"><c>repo</c> | <c>family</c> | <c>none</c> — what history produced the figure.</param>
/// <param name="BasisRuns">How many runs the figure was computed over.</param>
/// <param name="RowsCovered">Rows of the phase this step had a figure for.</param>
public sealed record EstimateSide(
    EstimateRange? TokensOut,
    EstimateRange? DurationS,
    string? Basis,
    int? BasisRuns,
    int RowsCovered);

/// <summary>
/// The actual column of one reconciliation line — the live runs, apportioned by row share (BRD-212).
/// </summary>
/// <remarks>
/// <para>
/// Every count the reader needs to judge the two figures travels beside them:
/// <see cref="ApportionedN"/> says how many runs counted only a share of their window,
/// <see cref="TokensUnrecordedN"/> counts the runs that carried no usable token window (never summed as
/// zeros — the TF-005 lesson), <see cref="TokensMainScopeN"/> the main-thread-only windows, and
/// <see cref="RowlessRunsInRepoN"/> the runs of the step that named no row anywhere in the repository —
/// counted over the whole repository and <b>never shared out</b> into a phase.
/// </para>
/// <para>
/// <see cref="TokensOut"/> and <see cref="DurationS"/> are <see cref="Figure"/>s: below
/// <see cref="MetricsConstants.MinN"/> supporting runs they read <c>insufficient data (n=…)</c> and no
/// page can print a number for them, exactly as the oracle refuses one.
/// </para>
/// </remarks>
/// <param name="RunsN">Live runs of the step that touched the covered rows.</param>
/// <param name="ApportionedN">Runs whose share of rows was below one — an inference, labelled as one.</param>
/// <param name="RowlessRunsInRepoN">Runs of the step in the whole repository that named no row.</param>
/// <param name="TokensOut">Apportioned output tokens, or an honest refusal below the floor.</param>
/// <param name="TokensN">Runs the token figure rests on — its own divisor.</param>
/// <param name="TokensUnrecordedN">Counted runs with no usable token window; excluded, never zeros.</param>
/// <param name="TokensMainScopeN">Counted runs whose window was main-thread only.</param>
/// <param name="DurationS">Apportioned wall seconds, or an honest refusal below the floor.</param>
/// <param name="DurationN">Runs the time figure rests on — its own divisor.</param>
public sealed record EstimateActual(
    int RunsN,
    int ApportionedN,
    int RowlessRunsInRepoN,
    Figure TokensOut,
    int TokensN,
    int TokensUnrecordedN,
    int TokensMainScopeN,
    Figure DurationS,
    int DurationN)
{
    /// <summary>True when any counted run contributed only a share of its window.</summary>
    /// <remarks>
    /// The card's <b>apportioned</b> label (BRD-214). It qualifies the actual; it is not a figure and
    /// combines nothing.
    /// </remarks>
    public bool IsApportioned => ApportionedN > 0;

    /// <summary>The actual for a line no run touched.</summary>
    public static EstimateActual Empty { get; } = new(
        0, 0, 0, Figure.InsufficientData(0), 0, 0, 0, Figure.InsufficientData(0), 0);
}

/// <summary>
/// Where a numeric actual sits against its forecast range — <c>within</c>, <c>above</c> or <c>below</c>
/// (BRD-212).
/// </summary>
/// <remarks>
/// A position, never a distance: this type has no delta, no ratio and no accuracy percentage, because
/// the fourth separation forbids the number that would say <i>how far</i> (BRD-213). <c>null</c> is the
/// dash the oracle prints — either side is not a number, so there is nothing to position.
/// </remarks>
/// <param name="TokensOut">The token figure's position, or <c>null</c>.</param>
/// <param name="DurationS">The time figure's position, or <c>null</c>.</param>
public sealed record EstimateRangePosition(string? TokensOut, string? DurationS)
{
    /// <summary>No position on either figure.</summary>
    public static EstimateRangePosition None { get; } = new(null, null);
}

/// <summary>
/// One step's reconciliation on a phase card: the estimate column beside the actual column, and the
/// actual's position (BRD-212).
/// </summary>
/// <param name="Estimate">The forecast side.</param>
/// <param name="Actual">The measured side.</param>
/// <param name="ActualInRange">Within / above / below per figure, or a dash.</param>
public sealed record EstimateCmdFigures(
    EstimateSide Estimate,
    EstimateActual Actual,
    EstimateRangePosition ActualInRange);

/// <summary>
/// One part of a phase — build, machine QA or after handoff — estimate beside actual over the SAME
/// rows (BRD-212).
/// </summary>
/// <remarks>
/// The part's estimate is the sum of the covered rows' own figures and its actual is the part's runs
/// on those same rows, so the two columns describe one row set and are comparable — while remaining
/// two columns. A sum over different rows is not one figure, which is why the covered set is the
/// intersection of the part's steps.
/// </remarks>
/// <param name="RowsCovered">Rows every step of the part had a figure for — the <c>[n/m]</c> numerator.</param>
/// <param name="TokensOut">The summed token forecast, or <c>null</c> when no row had one.</param>
/// <param name="DurationS">The summed time forecast, or <c>null</c> when no row had one.</param>
/// <param name="Actual">The measured side over the same rows.</param>
public sealed record EstimatePart(
    int RowsCovered,
    EstimateRange? TokensOut,
    EstimateRange? DurationS,
    EstimateActual Actual);

/// <summary>
/// The <b>Total (build + machine QA)</b> line — a TfLens addition with no parity oracle (BRD-217,
/// REQ-FN-150, ADR-035).
/// </summary>
/// <remarks>
/// <para>
/// Over the rows that build, verify and fix <b>all</b> had a figure for: the estimate is the sum of
/// those rows' low, mid and high across the three steps — the rule <c>tf-estimate.sh</c> prints as
/// "build + machine QA" — and the actual is the part rule applied to the three steps over the same
/// rows. After handoff is <b>never</b> inside the total, so a phase with no triage history still has
/// one. Where those rows are none, the total reads <see cref="NotOneFigureText"/> instead of a figure.
/// </para>
/// <para>
/// <see cref="NoOracleLabel"/> travels on the value because <c>tf-metrics.sh</c> computes neither this
/// nor the per-row actual: both stand where <c>extras</c> stands — hand-checked once against the raw
/// JSONL, recorded in DECISIONS.md, and never quoted on a passing parity diff.
/// </para>
/// </remarks>
/// <param name="Part">The three-step part over the commonly covered rows.</param>
/// <param name="NotOneFigure">
/// <see cref="NotOneFigureText"/> when no row was covered by all three steps; <c>null</c> otherwise.
/// </param>
public sealed record EstimateTotal(EstimatePart Part, string? NotOneFigure)
{
    /// <summary>The words that replace the figure when the three steps cover different rows (BRD-217).</summary>
    public const string NotOneFigureText = "not one figure: the steps cover different rows";

    /// <summary>The label every rendering of this figure carries — it has no oracle (ADR-035).</summary>
    public const string NoOracleLabel = "TfLens addition — no parity oracle; hand-checked against the raw JSONL";
}

/// <summary>
/// One step's own forecast on one checklist row, inside the per-row drill-down (BRD-215).
/// </summary>
/// <param name="Cmd">The step — <c>build-phase</c>, <c>verify-phase</c>, <c>fix-issues</c> or <c>triage</c>.</param>
/// <param name="TokensOut">The row's token forecast for the step, or <c>null</c> for no precedent.</param>
/// <param name="DurationS">The row's time forecast for the step, or <c>null</c>.</param>
/// <param name="Basis"><c>repo</c> | <c>family</c> | <c>none</c>.</param>
/// <param name="BasisRuns">How many runs the row's figure was computed over.</param>
public sealed record EstimateRowStep(
    string Cmd,
    EstimateRange? TokensOut,
    EstimateRange? DurationS,
    string? Basis,
    int? BasisRuns);

/// <summary>
/// One part of one checklist row: the step forecasts beside the row's own actual (BRD-215, BRD-217).
/// </summary>
/// <remarks>
/// The actual is the live runs of the part's steps that touched the row, each counting
/// <c>1 ÷ reqs_count</c> of its tokens and duration, labelled apportioned when that share is below one,
/// and <c>insufficient data (n=…)</c> under three runs. Nothing here is ever summed into a per-row
/// total: the row line has no total member, so the forbidden number is unrepresentable (ADR-007).
/// </remarks>
/// <param name="Steps">The part's per-step forecasts, in step order — basis and run count stay per step.</param>
/// <param name="TokensOut">The summed token forecast over the part's steps, or <c>null</c> when any was null.</param>
/// <param name="DurationS">The summed time forecast, or <c>null</c>.</param>
/// <param name="Actual">The row's own measured side — a TfLens figure with no oracle (ADR-035).</param>
public sealed record EstimateRowPart(
    IReadOnlyList<EstimateRowStep> Steps,
    EstimateRange? TokensOut,
    EstimateRange? DurationS,
    EstimateActual Actual)
{
    /// <summary>The label the per-row actual carries — it has no oracle (BRD-217, ADR-035).</summary>
    public const string NoOracleLabel = EstimateTotal.NoOracleLabel;
}

/// <summary>
/// One checklist row of an estimate batch, with its three parts (BRD-215).
/// </summary>
/// <param name="ReqId">The checklist row id.</param>
/// <param name="Build">Build's forecast beside the row's build actual.</param>
/// <param name="MachineQa">Verify + fix, over the row.</param>
/// <param name="AfterHandoff">Triage, over the row.</param>
public sealed record EstimateRowLine(
    string ReqId,
    EstimateRowPart Build,
    EstimateRowPart MachineQa,
    EstimateRowPart AfterHandoff);

/// <summary>
/// One phase card: a repository's one phase as one estimating model forecast it, beside what the live
/// runs measured (BRD-212, BRD-214).
/// </summary>
/// <remarks>
/// Keyed exactly as the oracle keys it — <c>(app, phase, model)</c> — and carrying the batch identity
/// (<see cref="Ts"/>, <see cref="Retro"/>) so a hindsight batch is legible as one. The four steps live
/// in <see cref="Cmds"/> in the oracle's order; the three parts and the two TfLens additions
/// (<see cref="Total"/>, <see cref="Rows"/>) sit beside them and never inside them.
/// </remarks>
public sealed record EstimatePhaseEntry
{
    /// <summary>The application the batch estimated.</summary>
    public required string App { get; init; }

    /// <summary>The phase estimated; <c>null</c> when the batch named none.</summary>
    public required int? Phase { get; init; }

    /// <summary>The estimating model; <c>null</c> when the store could not tell.</summary>
    public required string? Model { get; init; }

    /// <summary>The batch timestamp — one <c>ts</c> is one batch.</summary>
    public required string Ts { get; init; }

    /// <summary>The <c>owner/name</c> the batch was read from; a card never pools repositories.</summary>
    public required string Repo { get; init; }

    /// <summary>Checklist rows the batch covers.</summary>
    public required int RowsN { get; init; }

    /// <summary>True when the batch was estimated after the fact — hindsight, not a forecast.</summary>
    public required bool Retro { get; init; }

    /// <summary>The four steps, keyed by the oracle's cmd names, in its order.</summary>
    public required IReadOnlyList<KeyValuePair<string, EstimateCmdFigures>> Cmds { get; init; }

    /// <summary>The build part — <c>build-phase</c> alone.</summary>
    public required EstimatePart Build { get; init; }

    /// <summary>The machine-QA part — verify + fix over the rows both had a figure for.</summary>
    public required EstimatePart MachineQa { get; init; }

    /// <summary>The after-handoff part — triage.</summary>
    public required EstimatePart AfterHandoff { get; init; }

    /// <summary>
    /// Total (build + machine QA) — a TfLens addition with no oracle (BRD-217, REQ-FN-150).
    /// </summary>
    public required EstimateTotal Total { get; init; }

    /// <summary>
    /// The per-row drill-down, one line per checklist row of the batch, ordered by row id (BRD-215).
    /// </summary>
    public required IReadOnlyList<EstimateRowLine> Rows { get; init; }
}

/// <summary>
/// The whole reconciliation for one user and framework — the shape of <c>tf-metrics.sh --estimates
/// --json</c>, plus the two labelled TfLens additions per card (BRD-212, BRD-217).
/// </summary>
/// <remarks>
/// <para>
/// <b>One card per (repository, phase, estimating model), never pooled.</b> With All repos picked the
/// reconciliation runs once per repository and concatenates the cards; there is no member that could
/// hold a cross-repository figure (BRD-216, ADR-034).
/// </para>
/// <para>
/// The fourth separation is enforced by this shape: every estimate value is an
/// <see cref="EstimateRange"/> and every actual a <see cref="Figure"/> inside an
/// <see cref="EstimateActual"/>, and no member anywhere in the tree divides, subtracts or averages one
/// against the other — there is no property a page could bind to render a ratio, a difference or an
/// accuracy percentage (BRD-213).
/// </para>
/// </remarks>
public sealed record EstimateReconciliation
{
    /// <summary>One card per (repository, phase, estimating model), in the oracle's sort order.</summary>
    public IReadOnlyList<EstimatePhaseEntry> Phases { get; init; } = [];

    /// <summary>Repositories whose estimate stream carried at least one record.</summary>
    public int ReposWithEstimates { get; init; }

    /// <summary>What a framework with no estimate stream reports — empty, never absent (BRD-216).</summary>
    public static EstimateReconciliation Empty { get; } = new();
}
