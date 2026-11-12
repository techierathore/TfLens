using System.Text.Json;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The estimates-against-actual reconciliation — a field-for-field port of <c>analyse_estimates</c> in
/// <c>.tfcore/telemetry/tf-metrics.sh</c> (SCHEMA.md §2.8; REQ-FN-148, BRD-212), plus the two figures
/// the oracle does not compute (REQ-FN-150, BRD-217, ADR-035).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two columns, never one number.</b> An estimate is a forecast and a run record is a measurement,
/// and the fourth provenance separation (SCHEMA.md §6, BRD-213, ADR-034) keeps them apart: the
/// estimate's mid and range sit beside the actual in <see cref="EstimateCmdFigures"/>, and nothing
/// here divides one by the other, subtracts one from the other or writes either back. This class is
/// the <b>only</b> reader of the <c>"Estimate"</c> table on the figure path — no other metric in
/// TfLens sees a forecast.
/// </para>
/// <para>
/// Per repository, phase and estimating model the <b>latest forecast batch</b> (one <c>ts</c>) is
/// read; a batch carrying <c>basis_note: "retro"</c> is used only when that phase and model has no
/// forecast, and is labelled <see cref="EstimatePhaseEntry.Retro"/> — a later hindsight estimate never
/// replaces the forecast it would flatter. The actual is the <b>live</b> runs (never backfilled, never
/// voided by a <c>run-void</c>) of the step whose <c>reqs_touched</c> meet the rows the estimate
/// covered, each counting the share <c>rows hit ÷ reqs_count</c> of its tokens and duration — its
/// timestamps win, as in every reader (<see cref="RunDuration.UsableSeconds"/>) — capped at one, and
/// counted <b>apportioned</b> when that share is below one. Fewer than
/// <see cref="MetricsConstants.MinN"/> runs: <c>insufficient data (n=…)</c>.
/// </para>
/// <para>
/// No network call, no model call and no write anywhere in this file (BRD-219): every figure is
/// arithmetic over the two streams, re-done at request time.
/// </para>
/// </remarks>
public static class EstimateMetrics
{
    /// <summary>The four steps the reconciliation reports, in the oracle's order (<c>EST_CMDS</c>).</summary>
    public static readonly IReadOnlyList<string> StepCmds =
        ["build-phase", "verify-phase", "fix-issues", "triage"];

    /// <summary>The part names, in the oracle's order (<c>parts</c>).</summary>
    public static readonly IReadOnlyList<string> PartNames = ["build", "machine_qa", "after_handoff"];

    /// <summary>The <c>basis_note</c> that marks a hindsight batch rather than a forecast.</summary>
    public const string RetroNote = "retro";

    /// <summary>The <c>basis</c> value that means no precedent — the figure is null, never zero.</summary>
    public const string BasisNone = "none";

    /// <summary>The steps of the machine-QA part.</summary>
    private static readonly string[] MachineQaSteps = ["verify-phase", "fix-issues"];

    /// <summary>The steps of the Total — after handoff is NEVER inside it (BRD-217).</summary>
    private static readonly string[] TotalSteps = ["build-phase", "verify-phase", "fix-issues"];

    /// <summary>
    /// Maps a run's <c>cmd</c> onto the step it counts as (<c>EST_STEP_OF</c>): <c>triage-and-fix</c>
    /// and <c>triage-issues</c> both count as <c>triage</c> — bugs a person found after the checks
    /// passed, never counted inside machine QA's <c>fix-issues</c>.
    /// </summary>
    /// <param name="aCmd">The run's command.</param>
    /// <returns>The step name, or <c>null</c> for a run that is no estimate step.</returns>
    public static string? StepOf(string? aCmd) => aCmd switch
    {
        "build-phase" => "build-phase",
        "verify-phase" => "verify-phase",
        "fix-issues" => "fix-issues",
        "triage-and-fix" => "triage",
        "triage-issues" => "triage",
        _ => null
    };

    /// <summary>
    /// Reconciles the estimate stream against the live runs for one user and framework.
    /// </summary>
    /// <remarks>
    /// The one door the <c>"Estimate"</c> table is read through on the figure path (BRD-213). Runs are
    /// read here rather than passed in so the void fold the oracle applies (<c>apply_voids</c>) happens
    /// on this path too and cannot be forgotten by a caller. With All repos, the reconciliation runs
    /// once per repository and concatenates the cards — never pooled (BRD-216).
    /// </remarks>
    /// <param name="aStore">The store every record is read through; reads are scoped by user and framework.</param>
    /// <param name="aUserId">The AppManager user id — a required parameter, never a filter (ADR-013).</param>
    /// <param name="aFramework">The provenance axis; the Playbook writes no estimate stream.</param>
    /// <param name="aRepo">One repository (<c>owner/name</c>), or <c>null</c> for all of the user's.</param>
    /// <param name="aCancellationToken">Cancels the reads.</param>
    /// <returns>The reconciliation; empty — never absent — when no estimate stream exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aStore"/> is <c>null</c>.</exception>
    public static async Task<EstimateReconciliation> ReconcileAsync(
        ITelemetryStore aStore,
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aStore);

        var vEstimates = await aStore.ReadEstimatesAsync(aUserId, aFramework, aRepo, aCancellationToken)
            .ConfigureAwait(false);

        if (vEstimates.Count == 0)
        {
            return EstimateReconciliation.Empty;
        }

        var vRuns = RunVoid.Apply(
                await aStore.ReadRunsAsync(aUserId, aFramework, aRepo, aCancellationToken).ConfigureAwait(false))
            .Kept;

        return Compute(vEstimates, vRuns);
    }

    /// <summary>
    /// The reconciliation over records already read — the port proper.
    /// </summary>
    /// <param name="aEstimates">Every estimate record in scope.</param>
    /// <param name="aRuns">Every run record in scope, voids already applied.</param>
    /// <returns>The reconciliation.</returns>
    /// <exception cref="ArgumentNullException">A record list is <c>null</c>.</exception>
    public static EstimateReconciliation Compute(
        IReadOnlyList<EstimateRecord> aEstimates,
        IReadOnlyList<RunRecord> aRuns)
    {
        ArgumentNullException.ThrowIfNull(aEstimates);
        ArgumentNullException.ThrowIfNull(aRuns);

        var vCards = new List<EstimatePhaseEntry>();
        var vReposWithEstimates = 0;

        foreach (var vRepoGroup in aEstimates
                     .GroupBy(aEstimate => aEstimate.Repo, StringComparer.Ordinal)
                     .OrderBy(aGroup => aGroup.Key, StringComparer.Ordinal))
        {
            vReposWithEstimates++;

            // The oracle's run set: kind == "run" exactly, never backfilled, voids already dropped.
            var vRuns = aRuns
                .Where(aRun => string.Equals(aRun.Repo, vRepoGroup.Key, StringComparison.Ordinal)
                               && string.Equals(aRun.Kind, "run", StringComparison.Ordinal)
                               && aRun.Backfilled != true)
                .ToList();

            vCards.AddRange(ComputeRepo(vRepoGroup.Key, vRepoGroup.ToList(), vRuns));
        }

        return new EstimateReconciliation { Phases = vCards, ReposWithEstimates = vReposWithEstimates };
    }

    /// <summary>
    /// One repository's cards — the oracle's per-repo loop body.
    /// </summary>
    /// <param name="aRepo">The <c>owner/name</c> the records came from.</param>
    /// <param name="aEstimates">The repository's estimate records, in stream order.</param>
    /// <param name="aRuns">The repository's live run records, voids applied.</param>
    /// <returns>One card per (app, phase, estimating model), in the oracle's sort order.</returns>
    private static IReadOnlyList<EstimatePhaseEntry> ComputeRepo(
        string aRepo,
        IReadOnlyList<EstimateRecord> aEstimates,
        IReadOnlyList<RunRecord> aRuns)
    {
        var vAppName = AppNameOf(aRepo);

        // The latest FORECAST per (app, phase, model); a phase estimated only after the fact shows its
        // latest retro batch, labelled. A later hindsight estimate never replaces the forecast it would
        // flatter — so retro fills only keys no forecast batch holds.
        var vLatest = new Dictionary<BatchKey, string>(BatchKeyComparer.Instance);
        var vLatestRetro = new Dictionary<BatchKey, string>(BatchKeyComparer.Instance);

        foreach (var vEstimate in aEstimates)
        {
            var vKey = KeyOf(vEstimate, vAppName);

            if (string.IsNullOrEmpty(vEstimate.Ts))
            {
                continue;
            }

            var vPick = string.Equals(vEstimate.BasisNote, RetroNote, StringComparison.Ordinal)
                ? vLatestRetro
                : vLatest;

            if (!vPick.TryGetValue(vKey, out var vHeld)
                || string.CompareOrdinal(vEstimate.Ts, vHeld) > 0)
            {
                vPick[vKey] = vEstimate.Ts;
            }
        }

        foreach (var (vKey, vTs) in vLatestRetro)
        {
            vLatest.TryAdd(vKey, vTs);
        }

        var vCards = new List<EstimatePhaseEntry>(vLatest.Count);

        foreach (var (vKey, vTs) in vLatest
                     .OrderBy(aEntry => aEntry.Key.App, StringComparer.Ordinal)
                     .ThenBy(aEntry => aEntry.Key.Phase ?? 0)
                     .ThenBy(aEntry => aEntry.Key.Model ?? "None", StringComparer.Ordinal))
        {
            vCards.Add(ComputeCard(
                aRepo, vKey, vTs, vAppName, aEstimates, aRuns));
        }

        return vCards;
    }

    /// <summary>
    /// One card: the batch's rows, the four steps, the three parts, the Total and the per-row lines.
    /// </summary>
    /// <param name="aRepo">The repository.</param>
    /// <param name="aKey">The (app, phase, model) the card reports.</param>
    /// <param name="aTs">The batch timestamp the selection settled on.</param>
    /// <param name="aAppName">The repository's app-name fallback.</param>
    /// <param name="aEstimates">Every estimate record of the repository.</param>
    /// <param name="aRuns">The repository's live runs, voids applied.</param>
    /// <returns>The card.</returns>
    private static EstimatePhaseEntry ComputeCard(
        string aRepo,
        BatchKey aKey,
        string aTs,
        string aAppName,
        IReadOnlyList<EstimateRecord> aEstimates,
        IReadOnlyList<RunRecord> aRuns)
    {
        var vBatch = aEstimates
            .Where(aEstimate => string.Equals(AppOf(aEstimate, aAppName), aKey.App, StringComparison.Ordinal)
                                && Nullable.Equals(aEstimate.Phase, aKey.Phase)
                                && string.Equals(aEstimate.Model, aKey.Model, StringComparison.Ordinal)
                                && string.Equals(aEstimate.Ts, aTs, StringComparison.Ordinal))
            .ToList();

        var vPhaseRows = vBatch
            .Where(aEstimate => !string.IsNullOrEmpty(aEstimate.ReqId))
            .Select(aEstimate => aEstimate.ReqId!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(aReqId => aReqId, StringComparer.Ordinal)
            .ToList();

        // The row records by (cmd, req_id); a repeat of the key inside one batch keeps the LAST record,
        // exactly as the oracle's dict comprehension does.
        var vRowRecords = new Dictionary<(string Cmd, string ReqId), EstimateRecord>(RowKeyComparer.Instance);
        foreach (var vEstimate in vBatch.Where(aEstimate => !string.IsNullOrEmpty(aEstimate.ReqId)))
        {
            vRowRecords[(vEstimate.Cmd, vEstimate.ReqId!)] = vEstimate;
        }

        // Per step, the rows the actual is measured over: those whose estimate for that step HAD a
        // figure. `basis: "none"` is no precedent, so the row leaves the step's covered set — and a
        // missing basis (null) is not "none", exactly as the oracle's `e.get("basis") != "none"` reads.
        var vCovered = StepCmds.ToDictionary(
            aCmd => aCmd,
            aCmd => vRowRecords
                .Where(aEntry => string.Equals(aEntry.Key.Cmd, aCmd, StringComparison.Ordinal)
                                 && !string.Equals(aEntry.Value.Basis, BasisNone, StringComparison.Ordinal))
                .Select(aEntry => aEntry.Key.ReqId)
                .ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        // Runs that recorded no rows cannot be placed in ANY phase: their cost is real and is in no
        // actual. Counted per step over the whole repository and published, never guessed into a phase.
        var vRowless = StepCmds.ToDictionary(
            aCmd => aCmd,
            aCmd => aRuns.Count(aRun =>
                string.Equals(StepOf(aRun.Cmd), aCmd, StringComparison.Ordinal)
                && ReqsTouched(aRun).Count == 0),
            StringComparer.Ordinal);

        var vCmds = new List<KeyValuePair<string, EstimateCmdFigures>>(StepCmds.Count);
        foreach (var vCmd in StepCmds)
        {
            var vTotal = vBatch.FirstOrDefault(
                aEstimate => string.Equals(aEstimate.Cmd, vCmd, StringComparison.Ordinal)
                             && aEstimate.ReqId is null);

            var vActual = ActualFor(aRuns, [vCmd], vCovered[vCmd], vRowless);
            var vEstimateSide = SideOf(vTotal, vCovered[vCmd].Count);

            vCmds.Add(new KeyValuePair<string, EstimateCmdFigures>(
                vCmd,
                new EstimateCmdFigures(
                    vEstimateSide,
                    vActual,
                    PositionOf(vEstimateSide, vActual))));
        }

        var vBuild = PartFor(vRowRecords, vCovered, aRuns, vRowless, ["build-phase"]);
        var vMachineQa = PartFor(vRowRecords, vCovered, aRuns, vRowless, MachineQaSteps);
        var vAfterHandoff = PartFor(vRowRecords, vCovered, aRuns, vRowless, ["triage"]);
        var vTotalPart = PartFor(vRowRecords, vCovered, aRuns, vRowless, TotalSteps);

        return new EstimatePhaseEntry
        {
            App = aKey.App,
            Phase = aKey.Phase,
            Model = aKey.Model,
            Ts = aTs,
            Repo = aRepo,
            RowsN = vPhaseRows.Count,
            Retro = vBatch.Any(aEstimate =>
                string.Equals(aEstimate.BasisNote, RetroNote, StringComparison.Ordinal)),
            Cmds = vCmds,
            Build = vBuild,
            MachineQa = vMachineQa,
            AfterHandoff = vAfterHandoff,
            // BRD-217: the Total is the three-step part rule, and where the steps cover no common row
            // it refuses to be one figure rather than summing across different row sets.
            Total = new EstimateTotal(
                vTotalPart,
                vTotalPart.RowsCovered == 0 ? EstimateTotal.NotOneFigureText : null),
            Rows = RowLines(vPhaseRows, vRowRecords, vCovered, aRuns, vRowless)
        };
    }

    /// <summary>
    /// One part: estimate and actual over the SAME rows — those every step of the part had a figure for
    /// (the oracle's <c>_part</c>).
    /// </summary>
    /// <param name="aRowRecords">The batch's row records by (cmd, req_id).</param>
    /// <param name="aCovered">Per step, the rows that had a figure.</param>
    /// <param name="aRuns">The repository's live runs.</param>
    /// <param name="aRowless">Per step, the repository's runs that named no row.</param>
    /// <param name="aSteps">The part's steps.</param>
    /// <returns>The part.</returns>
    private static EstimatePart PartFor(
        IReadOnlyDictionary<(string Cmd, string ReqId), EstimateRecord> aRowRecords,
        IReadOnlyDictionary<string, HashSet<string>> aCovered,
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyDictionary<string, int> aRowless,
        IReadOnlyList<string> aSteps)
    {
        var vCoveredRows = Intersect(aSteps.Select(aStep => aCovered[aStep]));
        var vActual = ActualFor(aRuns, aSteps, vCoveredRows, aRowless);

        // The estimate is the sum of the covered rows' own figures for the part's steps — added only
        // when EVERY leg of EVERY step had one: a sum over rows where one step had no precedent is not
        // a forecast of anything (the oracle's `all(isinstance(x, dict))` rule).
        var vTokens = SumRanges(aSteps, vCoveredRows, aRowRecords, aRecord =>
            (aRecord.TokensOutLow, aRecord.TokensOutMid, aRecord.TokensOutHigh));
        var vDuration = SumRanges(aSteps, vCoveredRows, aRowRecords, aRecord =>
            (aRecord.DurationSLow, aRecord.DurationSMid, aRecord.DurationSHigh));

        return new EstimatePart(vCoveredRows.Count, vTokens, vDuration, vActual);
    }

    /// <summary>
    /// The per-row drill-down: one line per checklist row of the batch (BRD-215, REQ-FN-150).
    /// </summary>
    /// <remarks>
    /// The row's actual is the same part rule with the covered set narrowed to the one row, which is
    /// exactly BRD-215's stated rule: each run of the part's steps that touched the row counts
    /// <c>1 ÷ reqs_count</c> of its tokens and duration, labelled apportioned when the share is below
    /// one, <c>insufficient data (n=…)</c> under three runs. Nothing on a row line is ever summed into
    /// a per-row total — the type has no member for one.
    /// </remarks>
    /// <param name="aPhaseRows">The batch's row ids, sorted.</param>
    /// <param name="aRowRecords">The batch's row records by (cmd, req_id).</param>
    /// <param name="aCovered">Per step, the rows that had a figure.</param>
    /// <param name="aRuns">The repository's live runs.</param>
    /// <param name="aRowless">Per step, the repository's runs that named no row.</param>
    /// <returns>One line per row, ordered by row id.</returns>
    private static IReadOnlyList<EstimateRowLine> RowLines(
        IReadOnlyList<string> aPhaseRows,
        IReadOnlyDictionary<(string Cmd, string ReqId), EstimateRecord> aRowRecords,
        IReadOnlyDictionary<string, HashSet<string>> aCovered,
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyDictionary<string, int> aRowless)
    {
        var vLines = new List<EstimateRowLine>(aPhaseRows.Count);

        foreach (var vReqId in aPhaseRows)
        {
            var vRow = new HashSet<string>(StringComparer.Ordinal) { vReqId };

            vLines.Add(new EstimateRowLine(
                vReqId,
                RowPartFor(["build-phase"], vReqId, vRow, aRowRecords, aRuns, aRowless),
                RowPartFor(MachineQaSteps, vReqId, vRow, aRowRecords, aRuns, aRowless),
                RowPartFor(["triage"], vReqId, vRow, aRowRecords, aRuns, aRowless)));
        }

        return vLines;
    }

    /// <summary>
    /// One row's one part: its per-step forecasts beside its own actual.
    /// </summary>
    /// <param name="aSteps">The part's steps.</param>
    /// <param name="aReqId">The row id.</param>
    /// <param name="aRow">The covered set narrowed to the one row.</param>
    /// <param name="aRowRecords">The batch's row records by (cmd, req_id).</param>
    /// <param name="aRuns">The repository's live runs.</param>
    /// <param name="aRowless">Per step, the repository's runs that named no row.</param>
    /// <returns>The row part.</returns>
    private static EstimateRowPart RowPartFor(
        IReadOnlyList<string> aSteps,
        string aReqId,
        HashSet<string> aRow,
        IReadOnlyDictionary<(string Cmd, string ReqId), EstimateRecord> aRowRecords,
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyDictionary<string, int> aRowless)
    {
        var vStepRecords = aSteps
            .Select(aStep => aRowRecords.GetValueOrDefault((aStep, aReqId)))
            .ToList();

        var vSteps = new List<EstimateRowStep>(aSteps.Count);
        for (var vIndex = 0; vIndex < aSteps.Count; vIndex++)
        {
            var vRecord = vStepRecords[vIndex];
            vSteps.Add(new EstimateRowStep(
                aSteps[vIndex],
                RangeOf(vRecord?.TokensOutLow, vRecord?.TokensOutMid, vRecord?.TokensOutHigh),
                RangeOf(vRecord?.DurationSLow, vRecord?.DurationSMid, vRecord?.DurationSHigh),
                vRecord?.Basis,
                vRecord?.BasisRuns));
        }

        // A step the batch has no row record for, or whose basis is `none`, forecasts nothing — and
        // with a null leg anywhere the summed range refuses to exist, exactly as the part rule does.
        var vFigured = vStepRecords
            .Where(aRecord => aRecord is not null
                              && !string.Equals(aRecord.Basis, BasisNone, StringComparison.Ordinal))
            .ToList();

        var vTokens = vFigured.Count == aSteps.Count
            ? SumLegs(vFigured.Select(aRecord => (aRecord!.TokensOutLow, aRecord.TokensOutMid, aRecord.TokensOutHigh)))
            : null;
        var vDuration = vFigured.Count == aSteps.Count
            ? SumLegs(vFigured.Select(aRecord => (aRecord!.DurationSLow, aRecord.DurationSMid, aRecord.DurationSHigh)))
            : null;

        return new EstimateRowPart(
            vSteps,
            vTokens,
            vDuration,
            ActualFor(aRuns, aSteps, aRow, aRowless));
    }

    /// <summary>
    /// The actual: the live runs of these steps that touched the covered rows, each counting the share
    /// of its tokens and time its covered rows make up of its <c>reqs_count</c> (the oracle's
    /// <c>_actual</c>; BRD-212).
    /// </summary>
    /// <param name="aRuns">The repository's live runs.</param>
    /// <param name="aSteps">The steps the actual spans.</param>
    /// <param name="aCovered">The rows the actual is measured over.</param>
    /// <param name="aRowless">Per step, the repository's runs that named no row.</param>
    /// <returns>The actual column.</returns>
    private static EstimateActual ActualFor(
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyList<string> aSteps,
        HashSet<string> aCovered,
        IReadOnlyDictionary<string, int> aRowless)
    {
        var vSteps = aSteps.ToHashSet(StringComparer.Ordinal);

        var vRunsN = 0;
        var vApportionedN = 0;
        var vTokens = 0d;
        var vTokensN = 0;
        var vTokensUnrecordedN = 0;
        var vTokensMainScopeN = 0;
        var vDuration = 0d;
        var vDurationN = 0;

        foreach (var vRun in aRuns)
        {
            var vStep = StepOf(vRun.Cmd);
            if (vStep is null || !vSteps.Contains(vStep))
            {
                continue;
            }

            var vTouched = ReqsTouched(vRun);
            var vDenominator = vRun.ReqsCount is > 0 ? vRun.ReqsCount.Value : vTouched.Count;
            var vHits = vTouched.Count(aReqId => aCovered.Contains(aReqId));

            if (vHits == 0 || vDenominator <= 0)
            {
                continue;
            }

            var vShare = Math.Min(1d, (double)vHits / vDenominator);

            vRunsN++;
            if (vShare < 1d)
            {
                vApportionedN++;
            }

            // Tokens count only from runs whose window is real: a scope that is absent or `none` is
            // counted as *with no tokens* and never summed as a zero (the TF-005 lesson).
            var vHasScope = vRun.TokensScope is not null
                            && !string.Equals(vRun.TokensScope, "none", StringComparison.Ordinal);

            if (vRun.TokensOut is { } vTokensOut && vHasScope)
            {
                vTokens += vTokensOut * vShare;
                vTokensN++;

                if (string.Equals(vRun.TokensScope, "main", StringComparison.Ordinal))
                {
                    vTokensMainScopeN++;
                }
            }
            else
            {
                vTokensUnrecordedN++;
            }

            // The timestamps win, as in every reader (ADR-031, the oracle's `_dur`).
            if (RunDuration.UsableSeconds(vRun) is { } vSeconds)
            {
                vDuration += vSeconds * vShare;
                vDurationN++;
            }
        }

        return new EstimateActual(
            vRunsN,
            vApportionedN,
            aSteps.Sum(aStep => aRowless.GetValueOrDefault(aStep)),
            Floor(vTokens, vTokensN),
            vTokensN,
            vTokensUnrecordedN,
            vTokensMainScopeN,
            Floor(vDuration, vDurationN),
            vDurationN);
    }

    /// <summary>
    /// The oracle's <c>MIN_N</c> rule on an apportioned sum: an integer at or above the floor, an
    /// honest refusal below it — banker's rounding, exactly as Python's <c>int(round(x))</c>.
    /// </summary>
    /// <param name="aSum">The apportioned sum.</param>
    /// <param name="aN">The runs the sum rests on.</param>
    /// <returns>The figure.</returns>
    private static Figure Floor(double aSum, int aN) =>
        aN >= MetricsConstants.MinN
            ? Figure.Value(
                Math.Round(aSum, MidpointRounding.ToEven),
                aN,
                ((long)Math.Round(aSum, MidpointRounding.ToEven)).ToString(System.Globalization.CultureInfo.InvariantCulture))
            : Figure.InsufficientData(aN);

    /// <summary>
    /// The actual's position against the forecast range — <c>within</c>, <c>above</c> or <c>below</c>,
    /// per figure, and only where both sides are numbers (BRD-212).
    /// </summary>
    /// <param name="aEstimate">The estimate column.</param>
    /// <param name="aActual">The actual column.</param>
    /// <returns>The two positions; <c>null</c> is the oracle's dash.</returns>
    private static EstimateRangePosition PositionOf(EstimateSide aEstimate, EstimateActual aActual)
    {
        // A position, never a distance: the fourth separation allows the word and forbids the number
        // that would say how far (BRD-213).
        return new EstimateRangePosition(
            Position(aEstimate.TokensOut, aActual.TokensOut),
            Position(aEstimate.DurationS, aActual.DurationS));
    }

    /// <summary>One figure's position.</summary>
    /// <param name="aRange">The forecast range, or <c>null</c>.</param>
    /// <param name="aActual">The actual, which may be a refusal.</param>
    /// <returns><c>within</c>, <c>above</c>, <c>below</c> or <c>null</c>.</returns>
    private static string? Position(EstimateRange? aRange, Figure aActual)
    {
        if (aRange is null || !aActual.TryGetValue(out var vValue))
        {
            return null;
        }

        if (vValue < aRange.Low)
        {
            return "below";
        }

        return vValue > aRange.High ? "above" : "within";
    }

    /// <summary>
    /// The estimate column of one step's phase total record.
    /// </summary>
    /// <param name="aTotal">The batch's phase-total record for the step, or <c>null</c> when it has none.</param>
    /// <param name="aRowsCovered">Rows the step had a figure for.</param>
    /// <returns>The estimate side.</returns>
    private static EstimateSide SideOf(EstimateRecord? aTotal, int aRowsCovered) => new(
        RangeOf(aTotal?.TokensOutLow, aTotal?.TokensOutMid, aTotal?.TokensOutHigh),
        RangeOf(aTotal?.DurationSLow, aTotal?.DurationSMid, aTotal?.DurationSHigh),
        aTotal?.Basis,
        aTotal?.BasisRuns,
        aRowsCovered);

    /// <summary>
    /// Builds a range from its three legs; a figure with no precedent stays <c>null</c> whole.
    /// </summary>
    /// <param name="aLow">The first quartile.</param>
    /// <param name="aMid">The median.</param>
    /// <param name="aHigh">The third quartile.</param>
    /// <returns>The range, or <c>null</c> when any leg is missing.</returns>
    private static EstimateRange? RangeOf(long? aLow, long? aMid, long? aHigh) =>
        aLow is { } vLow && aMid is { } vMid && aHigh is { } vHigh
            ? new EstimateRange(vLow, vMid, vHigh)
            : null;

    /// <summary>
    /// Sums one figure's legs over every (step, covered row) record — the oracle's part-estimate rule.
    /// </summary>
    /// <param name="aSteps">The part's steps.</param>
    /// <param name="aCoveredRows">The rows every step had a figure for.</param>
    /// <param name="aRowRecords">The batch's row records by (cmd, req_id).</param>
    /// <param name="aLegs">Reads one record's three legs.</param>
    /// <returns>The summed range, or <c>null</c> when any record lacked one or no record existed.</returns>
    private static EstimateRange? SumRanges(
        IReadOnlyList<string> aSteps,
        IReadOnlyCollection<string> aCoveredRows,
        IReadOnlyDictionary<(string Cmd, string ReqId), EstimateRecord> aRowRecords,
        Func<EstimateRecord, (long? Low, long? Mid, long? High)> aLegs)
    {
        var vFigures = new List<(long? Low, long? Mid, long? High)>();

        foreach (var vStep in aSteps)
        {
            foreach (var vReqId in aCoveredRows.OrderBy(aReqId => aReqId, StringComparer.Ordinal))
            {
                if (aRowRecords.TryGetValue((vStep, vReqId), out var vRecord))
                {
                    vFigures.Add(aLegs(vRecord));
                }
            }
        }

        return vFigures.Count > 0 ? SumLegs(vFigures) : null;
    }

    /// <summary>Sums leg triples; one incomplete triple refuses the whole sum.</summary>
    /// <param name="aFigures">The legs to sum.</param>
    /// <returns>The summed range, or <c>null</c>.</returns>
    private static EstimateRange? SumLegs(IEnumerable<(long? Low, long? Mid, long? High)> aFigures)
    {
        long vLow = 0;
        long vMid = 0;
        long vHigh = 0;
        var vAny = false;

        foreach (var (aLow, aMid, aHigh) in aFigures)
        {
            if (aLow is null || aMid is null || aHigh is null)
            {
                return null;
            }

            vAny = true;
            vLow += aLow.Value;
            vMid += aMid.Value;
            vHigh += aHigh.Value;
        }

        return vAny ? new EstimateRange(vLow, vMid, vHigh) : null;
    }

    /// <summary>The intersection of the steps' covered row sets — the rows every step had a figure for.</summary>
    /// <param name="aSets">One set per step.</param>
    /// <returns>The common rows.</returns>
    private static HashSet<string> Intersect(IEnumerable<HashSet<string>> aSets)
    {
        HashSet<string>? vResult = null;

        foreach (var vSet in aSets)
        {
            if (vResult is null)
            {
                vResult = new HashSet<string>(vSet, StringComparer.Ordinal);
                continue;
            }

            vResult.IntersectWith(vSet);
        }

        return vResult ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads a run's <c>reqs_touched</c> array — the strings it names, in order.
    /// </summary>
    /// <remarks>
    /// Stored verbatim as JSON text, so the read is a parse; a malformed array reads as no rows, which
    /// makes the run rowless exactly as the oracle's <c>r.get("reqs_touched") or []</c> does.
    /// </remarks>
    /// <param name="aRun">The run.</param>
    /// <returns>The requirement ids it touched.</returns>
    private static IReadOnlyList<string> ReqsTouched(RunRecord aRun)
    {
        if (string.IsNullOrWhiteSpace(aRun.ReqsTouched))
        {
            return [];
        }

        try
        {
            using var vDocument = JsonDocument.Parse(aRun.ReqsTouched);

            return vDocument.RootElement.ValueKind != JsonValueKind.Array
                ? []
                : vDocument.RootElement.EnumerateArray()
                    .Where(aElement => aElement.ValueKind == JsonValueKind.String)
                    .Select(aElement => aElement.GetString()!)
                    .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The repository's app-name fallback — the oracle's <c>app_name(repo)</c> in the one case TfLens
    /// can answer: the repository's own name (the checklist file lives in a tree TfLens never reads).
    /// </summary>
    /// <param name="aRepo">The <c>owner/name</c>.</param>
    /// <returns>The segment after the slash.</returns>
    private static string AppNameOf(string aRepo)
    {
        var vSlash = aRepo.LastIndexOf('/');
        return vSlash >= 0 && vSlash < aRepo.Length - 1 ? aRepo[(vSlash + 1)..] : aRepo;
    }

    /// <summary>A record's effective app — its own, or the repository's name.</summary>
    /// <param name="aEstimate">The record.</param>
    /// <param name="aAppName">The repository's fallback name.</param>
    /// <returns>The effective app.</returns>
    private static string AppOf(EstimateRecord aEstimate, string aAppName) =>
        string.IsNullOrEmpty(aEstimate.App) ? aAppName : aEstimate.App;

    /// <summary>The batch key of one record — the oracle's <c>(app or app_name, phase, model)</c>.</summary>
    /// <param name="aEstimate">The record.</param>
    /// <param name="aAppName">The repository's fallback name.</param>
    /// <returns>The key.</returns>
    private static BatchKey KeyOf(EstimateRecord aEstimate, string aAppName) =>
        new(AppOf(aEstimate, aAppName), aEstimate.Phase, aEstimate.Model);

    /// <summary>The (app, phase, estimating model) triple a batch is selected by.</summary>
    /// <param name="App">The effective application name.</param>
    /// <param name="Phase">The phase, or <c>null</c> when the batch named none.</param>
    /// <param name="Model">The estimating model, or <c>null</c> when the store could not tell.</param>
    private sealed record BatchKey(string App, int? Phase, string? Model);

    /// <summary>
    /// Ordinal, null-safe equality for <see cref="BatchKey"/> — <c>null</c> model equals <c>null</c>
    /// model, exactly as the oracle's tuple comparison does.
    /// </summary>
    private sealed class BatchKeyComparer : IEqualityComparer<BatchKey>
    {
        /// <summary>The shared instance.</summary>
        public static readonly BatchKeyComparer Instance = new();

        /// <inheritdoc />
        public bool Equals(BatchKey? aX, BatchKey? aY) =>
            aX is not null && aY is not null
            && string.Equals(aX.App, aY.App, StringComparison.Ordinal)
            && Nullable.Equals(aX.Phase, aY.Phase)
            && string.Equals(aX.Model, aY.Model, StringComparison.Ordinal);

        /// <inheritdoc />
        public int GetHashCode(BatchKey aObj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(aObj.App),
                aObj.Phase,
                aObj.Model is null ? (int?)null : StringComparer.Ordinal.GetHashCode(aObj.Model));
    }

    /// <summary>
    /// Ordinal, null-free equality for the <c>(cmd, req_id)</c> row-record key.
    /// </summary>
    private sealed class RowKeyComparer : IEqualityComparer<(string Cmd, string ReqId)>
    {
        /// <summary>The shared instance.</summary>
        public static readonly RowKeyComparer Instance = new();

        /// <inheritdoc />
        public bool Equals((string Cmd, string ReqId) aX, (string Cmd, string ReqId) aY) =>
            string.Equals(aX.Cmd, aY.Cmd, StringComparison.Ordinal)
            && string.Equals(aX.ReqId, aY.ReqId, StringComparison.Ordinal);

        /// <inheritdoc />
        public int GetHashCode((string Cmd, string ReqId) aObj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(aObj.Cmd),
                StringComparer.Ordinal.GetHashCode(aObj.ReqId));
    }
}
