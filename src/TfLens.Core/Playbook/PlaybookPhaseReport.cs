using System.Globalization;
using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// The Playbook axis of <c>/effort</c>: every schema-2 figure, each with the cohort it rests on
/// (REQ-FN-096 … REQ-FN-102, BRD-155 … BRD-163).
/// </summary>
/// <remarks>
/// <para>
/// <b>Quarantine first, then cohorts, then figures.</b> Every row is judged by
/// <see cref="PlaybookPhaseInvariants"/> before anything is summed, because an invalid row's retained
/// zero is indistinguishable from a real one once it is inside a total. Nothing that fails is repaired
/// and nothing that fails is hidden: it stays in <see cref="Executions"/> with its reasons.
/// </para>
/// <para>
/// <b>Four cohorts, deliberately nested (§6).</b> Duration takes closed windows; active time
/// additionally takes complete coverage; token totals additionally take a valid row with a complete
/// token status; measured cost additionally takes a complete cost status. Each figure carries its
/// <c>n</c> and its exclusions <i>beside</i> it rather than in a page footer, and none of these statuses
/// is evidence of end-to-end event-delivery completeness — the producer's writes are best-effort and no
/// status here claims otherwise.
/// </para>
/// <para>
/// <b>Nothing on this record is human effort.</b> Neither framework captures it. Wall-clock elapsed and
/// the producer's unioned observed active time are two separate members, and the two diagnostic
/// component sums are <see cref="PhaseDiagnostic"/> text that no aggregate can accept (ADR-027).
/// </para>
/// </remarks>
public sealed record PlaybookPhaseReport
{
    /// <summary>Whether the harness has a normalized phase producer at all (BRD-163).</summary>
    public required PhaseHarnessSupport Harness { get; init; }

    /// <summary>The data-quality counts every comparison is read against.</summary>
    public required PhaseQuality Quality { get; init; }

    /// <summary>Every execution, quarantined ones included, each with its reasons.</summary>
    public required IReadOnlyList<PhaseExecutionView> Executions { get; init; }

    /// <summary>Median wall-clock duration over closed windows.</summary>
    public required PhaseFigure ElapsedMsMedian { get; init; }

    /// <summary>90th-percentile wall-clock duration over closed windows.</summary>
    public required PhaseFigure ElapsedMsP90 { get; init; }

    /// <summary>Observed active time summed over closed windows with complete coverage.</summary>
    public required PhaseFigure ObservedActiveMsTotal { get; init; }

    /// <summary>The five token legs over valid, complete-status windows.</summary>
    public required PhaseTokenTotals Tokens { get; init; }

    /// <summary>Measured provider dollars; a <c>zero-unverified</c> cost is excluded, never shown as $0.</summary>
    public required PhaseMeasuredCost MeasuredCostUsd { get; init; }

    /// <summary>Per-model usage, aggregated from the per-model rows and never from the dominant label.</summary>
    public required IReadOnlyList<PhaseModelUsageView> Models { get; init; }

    /// <summary>Sub-agent sessions launched, over the windows whose scope could have seen them.</summary>
    public required PhaseFigure SubagentsSpawned { get; init; }

    /// <summary>Sub-agent sessions that produced tokens, over the same windows.</summary>
    public required PhaseFigure SubagentsContributors { get; init; }

    /// <summary>One group per <b>command phase</b> — never per conceptual lifecycle stage.</summary>
    public required IReadOnlyList<PhaseCommandGroup> CommandPhases { get; init; }

    /// <summary>
    /// Builds the report from the three stored row sets.
    /// </summary>
    /// <param name="aHarness">The repository's harness; one with no adapter renders unsupported.</param>
    /// <param name="aExecutions">The <c>"PbPhaseExecution"</c> rows.</param>
    /// <param name="aModels">The <c>"PbPhaseModelUsage"</c> rows.</param>
    /// <param name="aSubagents">The <c>"PbPhaseSubagent"</c> rows.</param>
    /// <returns>The report; every figure is <c>unavailable</c> rather than zero when nothing qualified.</returns>
    public static PlaybookPhaseReport Build(
        string? aHarness,
        IReadOnlyList<PbPhaseExecutionRecord> aExecutions,
        IReadOnlyList<PbPhaseModelUsageRecord> aModels,
        IReadOnlyList<PbPhaseSubagentRecord> aSubagents)
    {
        ArgumentNullException.ThrowIfNull(aExecutions);
        ArgumentNullException.ThrowIfNull(aModels);
        ArgumentNullException.ThrowIfNull(aSubagents);

        // REQ-FN-096 — validation runs over every row BEFORE a single cohort is formed.
        var vJudged = aExecutions.Select(aE => new JudgedExecution(aE, PlaybookPhaseInvariants.Validate(aE)))
            .ToList();

        var vClean = vJudged.Where(aJ => !aJ.Validation.IsQuarantined).Select(aJ => aJ.Record).ToList();
        var vDuration = vClean.Where(IsDurationEligible).ToList();
        var vActive = vDuration.Where(IsActiveEligible).ToList();
        var vTokens = vDuration.Where(aE => IsTokenEligible(aE, aModels)).ToList();
        var vCost = vTokens.Where(aE => IsMeasuredCostEligible(aE, aModels)).ToList();

        return new PlaybookPhaseReport
        {
            Harness = PhaseHarnessSupport.For(aHarness),
            Quality = QualityOf(vJudged),
            Executions = vJudged.Select(aJ => ViewOf(aJ, aModels, aSubagents)).ToList(),
            ElapsedMsMedian = ElapsedFigure(vJudged, vDuration, "elapsed_ms_median", "Median elapsed", true),
            ElapsedMsP90 = ElapsedFigure(vJudged, vDuration, "elapsed_ms_p90", "p90 elapsed", false),
            ObservedActiveMsTotal = ActiveFigure(vJudged, vActive),
            Tokens = TokenTotals(vJudged, vTokens),
            MeasuredCostUsd = CostTotal(vJudged, vTokens, vCost, aModels),
            Models = ModelViews(vTokens, vCost, aModels),
            SubagentsSpawned = FanoutFigure(vDuration, "subagents_spawned", "Sub-agents spawned", true),
            SubagentsContributors =
                FanoutFigure(vDuration, "subagents_contributors", "Token contributors", false),
            CommandPhases = CommandGroups(vJudged, vTokens)
        };
    }

    /// <summary>
    /// Every execution that ran a model, matching <b>any</b> <c>models[]</c> member (REQ-FN-099).
    /// </summary>
    /// <remarks>
    /// Never the dominant-model label: a mixed-model execution filtered only by its winner disappears
    /// from the very comparison it belongs in.
    /// </remarks>
    /// <param name="aModel">The model to filter on.</param>
    /// <returns>The matching execution views, quarantined ones included for drill-down.</returns>
    public IReadOnlyList<PhaseExecutionView> WhereModel(string aModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aModel);

        return Executions
            .Where(aE => aE.Models.Contains(aModel, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// The whole-task duration total, which exists only for an explicitly supplied cohort (REQ-FN-098).
    /// </summary>
    /// <remarks>
    /// With no cohort the answer is <c>unavailable</c>. It is never assembled from a reused
    /// <c>session_id</c>, because one session may execute several tasks and the resulting total would
    /// pool unrelated work while looking authoritative.
    /// </remarks>
    /// <param name="aCohort">The cohort ingestion supplied, or <c>null</c> when it supplied none.</param>
    /// <param name="aExecutions">The stored execution rows the cohort selects from.</param>
    /// <returns>The summed wall-clock duration of the cohort's closed windows.</returns>
    public static PhaseFigure TaskElapsedMsTotal(
        PhaseTaskCohort? aCohort, IReadOnlyList<PbPhaseExecutionRecord> aExecutions)
    {
        ArgumentNullException.ThrowIfNull(aExecutions);

        if (aCohort is null)
        {
            return new PhaseFigure(
                "task_elapsed_ms_total",
                "Whole-task elapsed",
                PhaseValue.Unavailable(),
                0,
                [new PhaseExclusion(
                    "no_explicit_cohort",
                    "No repository + checklist + execution-id or time cohort was supplied, and a reused "
                    + "session id is not one",
                    aExecutions.Count)]);
        }

        var vRows = aExecutions
            .Where(aE => aCohort.Contains(aE) && !PlaybookPhaseInvariants.Validate(aE).IsQuarantined)
            .Where(IsDurationEligible)
            .ToList();

        return new PhaseFigure(
            "task_elapsed_ms_total",
            "Whole-task elapsed",
            PhaseValue.Measured(vRows.Sum(aE => (double)(aE.ElapsedMs ?? 0L)), vRows.Count, MsOf(
                vRows.Sum(aE => aE.ElapsedMs ?? 0L))),
            vRows.Count,
            []);
    }

    /// <summary>A closed window with a duration — the only rows a duration aggregate may read.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <returns><c>true</c> when the window closed and reported an elapsed time.</returns>
    public static bool IsDurationEligible(PbPhaseExecutionRecord aExecution)
    {
        ArgumentNullException.ThrowIfNull(aExecution);
        return aExecution.Complete == true && aExecution.ElapsedMs is not null;
    }

    /// <summary>A closed window whose active coverage was complete — the only active-time comparison rows.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <returns><c>true</c> when every interval in the window was observable.</returns>
    public static bool IsActiveEligible(PbPhaseExecutionRecord aExecution)
    {
        ArgumentNullException.ThrowIfNull(aExecution);

        return IsDurationEligible(aExecution)
            && aExecution.ObservedActiveMs is not null
            && string.Equals(
                aExecution.ActiveCoverage, PlaybookPhaseVocabulary.CoverageComplete, StringComparison.Ordinal);
    }

    /// <summary>A window whose token counts may be totalled — valid, complete, and not schema-1.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aModels">The per-model rows, unused here but taken for symmetry with the cost rule.</param>
    /// <returns><c>true</c> when the token window may enter a total.</returns>
    public static bool IsTokenEligible(
        PbPhaseExecutionRecord aExecution, IReadOnlyList<PbPhaseModelUsageRecord> aModels)
    {
        ArgumentNullException.ThrowIfNull(aExecution);

        return IsDurationEligible(aExecution)
            && aExecution.DataQualityValid == true
            && string.Equals(
                aExecution.TokenStatus, PlaybookPhaseVocabulary.StatusComplete, StringComparison.Ordinal);
    }

    /// <summary>
    /// A window whose dollars are measured — the phase status <b>and</b> every contributing model.
    /// </summary>
    /// <remarks>
    /// One <c>zero-unverified</c> model makes the phase's cost partial, not complete (§6), which is why
    /// the model rows are consulted rather than trusted to agree with the phase-level status.
    /// </remarks>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aModels">The per-model rows.</param>
    /// <returns><c>true</c> when the cost may enter a measured total.</returns>
    public static bool IsMeasuredCostEligible(
        PbPhaseExecutionRecord aExecution, IReadOnlyList<PbPhaseModelUsageRecord> aModels)
    {
        ArgumentNullException.ThrowIfNull(aExecution);
        ArgumentNullException.ThrowIfNull(aModels);

        return IsTokenEligible(aExecution, aModels)
            && aExecution.CostUsd is not null
            && string.Equals(
                CostStatusOf(aExecution, aModels),
                PlaybookPhaseVocabulary.StatusComplete,
                StringComparison.Ordinal);
    }

    /// <summary>
    /// The cost status of one execution, demoted to <c>partial</c> when a model row is not complete.
    /// </summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aModels">The per-model rows.</param>
    /// <returns>The effective status.</returns>
    public static string? CostStatusOf(
        PbPhaseExecutionRecord aExecution, IReadOnlyList<PbPhaseModelUsageRecord> aModels)
    {
        ArgumentNullException.ThrowIfNull(aExecution);
        ArgumentNullException.ThrowIfNull(aModels);

        var vOwn = ModelsOf(aExecution, aModels);

        var vAllComplete = vOwn.Count == 0
                           || vOwn.All(aM => string.Equals(
                               aM.CostStatus,
                               PlaybookPhaseVocabulary.StatusComplete,
                               StringComparison.Ordinal));

        return vAllComplete ? aExecution.CostStatus : PlaybookPhaseVocabulary.StatusPartial;
    }

    /// <summary>The per-model rows belonging to one execution.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aModels">Every per-model row.</param>
    /// <returns>The rows for that execution.</returns>
    private static List<PbPhaseModelUsageRecord> ModelsOf(
        PbPhaseExecutionRecord aExecution, IReadOnlyList<PbPhaseModelUsageRecord> aModels) =>
        aModels
            .Where(aM => aM.UserId == aExecution.UserId
                         && string.Equals(aM.Repo, aExecution.Repo, StringComparison.Ordinal)
                         && string.Equals(
                             aM.PhaseExecutionId, aExecution.PhaseExecutionId, StringComparison.Ordinal))
            .ToList();

    /// <summary>Counts the data-quality facts every comparison on the page is read against.</summary>
    /// <param name="aJudged">Every row with its verdict.</param>
    /// <returns>The counts.</returns>
    private static PhaseQuality QualityOf(IReadOnlyList<JudgedExecution> aJudged) =>
        new(
            aJudged.Count,
            aJudged.Count(aJ => aJ.Record.Complete == true),
            aJudged.Count(aJ => aJ.Record.Complete == false),
            aJudged.Count(aJ => Coverage(aJ.Record) == PlaybookPhaseVocabulary.CoverageComplete),
            aJudged.Count(aJ => Coverage(aJ.Record) == PlaybookPhaseVocabulary.CoveragePartial),
            aJudged.Count(aJ => Coverage(aJ.Record) is not PlaybookPhaseVocabulary.CoverageComplete
                                and not PlaybookPhaseVocabulary.CoveragePartial),
            aJudged.Count(aJ => aJ.Validation.IsQuarantined),
            aJudged.Count(aJ => IsLegacy(aJ.Record)));

    /// <summary>The coverage word of a row, with an absent one reading as no observable activity.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <returns>The coverage word, or an empty string.</returns>
    private static string Coverage(PbPhaseExecutionRecord aExecution) =>
        aExecution.ActiveCoverage ?? string.Empty;

    /// <summary>Tells whether a row is a sparse schema-1 event, which is drill-down only.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <returns><c>true</c> when the row is legacy-unverified.</returns>
    private static bool IsLegacy(PbPhaseExecutionRecord aExecution) =>
        string.Equals(
            aExecution.TokenStatus, PlaybookPhaseAdapter.LegacyUnverified, StringComparison.Ordinal);

    /// <summary>Builds a duration figure over the closed windows, stating what it left out.</summary>
    /// <param name="aJudged">Every row with its verdict.</param>
    /// <param name="aEligible">The closed, non-quarantined windows.</param>
    /// <param name="aKey">The export key.</param>
    /// <param name="aLabel">The page label.</param>
    /// <param name="aIsMedian">True for the median, false for the 90th percentile.</param>
    /// <returns>The figure with its cohort and exclusions.</returns>
    private static PhaseFigure ElapsedFigure(
        IReadOnlyList<JudgedExecution> aJudged,
        IReadOnlyList<PbPhaseExecutionRecord> aEligible,
        string aKey,
        string aLabel,
        bool aIsMedian)
    {
        var vValues = aEligible.Select(aE => (double)(aE.ElapsedMs ?? 0L)).ToList();
        var vPoint = aIsMedian ? MetricsConstants.Median(vValues) : Percentile(vValues, 0.90);

        return new PhaseFigure(
            aKey,
            aLabel,
            vPoint is null
                ? PhaseValue.Unavailable()
                : PhaseValue.Comparative(vPoint.Value, vValues.Count, MsOf((long)vPoint.Value)),
            vValues.Count,
            DurationExclusions(aJudged));
    }

    /// <summary>Builds the observed-active total over closed windows with complete coverage.</summary>
    /// <param name="aJudged">Every row with its verdict.</param>
    /// <param name="aEligible">The rows that qualified.</param>
    /// <returns>The figure with its cohort and exclusions.</returns>
    private static PhaseFigure ActiveFigure(
        IReadOnlyList<JudgedExecution> aJudged, IReadOnlyList<PbPhaseExecutionRecord> aEligible)
    {
        var vTotal = aEligible.Sum(aE => aE.ObservedActiveMs ?? 0L);

        var vExclusions = DurationExclusions(aJudged).ToList();
        vExclusions.Add(new PhaseExclusion(
            "active_coverage_partial",
            "partial coverage, which is a lower bound and never enters a comparison",
            aJudged.Count(aJ => Coverage(aJ.Record) == PlaybookPhaseVocabulary.CoveragePartial)));
        vExclusions.Add(new PhaseExclusion(
            "active_coverage_unavailable",
            "no observable active interval",
            aJudged.Count(aJ => Coverage(aJ.Record) is not PlaybookPhaseVocabulary.CoverageComplete
                                and not PlaybookPhaseVocabulary.CoveragePartial)));

        return new PhaseFigure(
            "observed_active_ms_complete_records",
            PlaybookPhaseVocabulary.ObservedActiveLabel,
            PhaseValue.Measured(vTotal, aEligible.Count, MsOf(vTotal)),
            aEligible.Count,
            vExclusions);
    }

    /// <summary>Sums the five token legs over the rows whose window may be totalled.</summary>
    /// <param name="aJudged">Every row with its verdict.</param>
    /// <param name="aEligible">The rows that qualified.</param>
    /// <returns>The five legs, each carrying the cohort.</returns>
    private static PhaseTokenTotals TokenTotals(
        IReadOnlyList<JudgedExecution> aJudged, IReadOnlyList<PbPhaseExecutionRecord> aEligible)
    {
        var vN = aEligible.Count;

        var vExclusions = DurationExclusions(aJudged).ToList();
        vExclusions.Add(new PhaseExclusion(
            "token_status_not_complete",
            "token status not complete, or the producer did not call the row valid",
            aJudged.Count(aJ => aJ.Record.TokenStatus is not null
                                && !string.Equals(
                                    aJ.Record.TokenStatus,
                                    PlaybookPhaseVocabulary.StatusComplete,
                                    StringComparison.Ordinal))));
        vExclusions.Add(new PhaseExclusion(
            "legacy_unverified",
            "sparse schema-1 rows, available for drill-down and excluded from schema-2 comparisons",
            aJudged.Count(aJ => IsLegacy(aJ.Record))));

        return new PhaseTokenTotals(
            Leg(aEligible, aE => aE.TokensInput, vN),
            Leg(aEligible, aE => aE.TokensOutput, vN),
            Leg(aEligible, aE => aE.TokensReasoning, vN),
            Leg(aEligible, aE => aE.TokensCacheRead, vN),
            Leg(aEligible, aE => aE.TokensCacheWrite, vN),
            vN,
            vExclusions);
    }

    /// <summary>Sums one token leg, yielding <c>unavailable</c> rather than zero on an empty cohort.</summary>
    /// <param name="aRows">The eligible rows.</param>
    /// <param name="aLeg">The leg to read.</param>
    /// <param name="aN">The cohort size.</param>
    /// <returns>The summed leg.</returns>
    private static PhaseValue Leg(
        IReadOnlyList<PbPhaseExecutionRecord> aRows, Func<PbPhaseExecutionRecord, long?> aLeg, int aN)
    {
        var vTotal = aRows.Sum(aRow => aLeg(aRow) ?? 0L);
        return PhaseValue.Measured(vTotal, aN, vTotal.ToString("N0", CultureInfo.InvariantCulture));
    }

    /// <summary>Totals measured dollars, stating the zero-unverified and partial exclusions beside it.</summary>
    /// <param name="aJudged">Every row with its verdict.</param>
    /// <param name="aTokenEligible">Rows whose tokens may be totalled — the cost cohort's parent.</param>
    /// <param name="aEligible">Rows whose dollars are measured.</param>
    /// <param name="aModels">The per-model rows.</param>
    /// <returns>The measured total, or <c>null</c> dollars when nothing qualified.</returns>
    private static PhaseMeasuredCost CostTotal(
        IReadOnlyList<JudgedExecution> aJudged,
        IReadOnlyList<PbPhaseExecutionRecord> aTokenEligible,
        IReadOnlyList<PbPhaseExecutionRecord> aEligible,
        IReadOnlyList<PbPhaseModelUsageRecord> aModels)
    {
        var vExclusions = new List<PhaseExclusion>
        {
            new(
                "cost_status_zero_unverified",
                "zero dollars reported against non-zero tokens; unverified rather than free",
                aJudged.Count(aJ => string.Equals(
                    aJ.Record.CostStatus,
                    PlaybookPhaseVocabulary.StatusZeroUnverified,
                    StringComparison.Ordinal))),
            new(
                "cost_status_not_complete",
                "a contributing model did not report its cost completely, so the phase cost is partial",
                aTokenEligible.Count(aE => !IsMeasuredCostEligible(aE, aModels)))
        };

        return new PhaseMeasuredCost(
            aEligible.Count == 0 ? null : aEligible.Sum(aE => aE.CostUsd ?? 0m),
            aEligible.Count,
            vExclusions);
    }

    /// <summary>Aggregates per-model usage from the child rows — never from the dominant label.</summary>
    /// <param name="aTokenEligible">Executions whose tokens may be totalled.</param>
    /// <param name="aCostEligible">Executions whose dollars are measured.</param>
    /// <param name="aModels">Every per-model row.</param>
    /// <returns>One view per model, ordered by name.</returns>
    private static IReadOnlyList<PhaseModelUsageView> ModelViews(
        IReadOnlyList<PbPhaseExecutionRecord> aTokenEligible,
        IReadOnlyList<PbPhaseExecutionRecord> aCostEligible,
        IReadOnlyList<PbPhaseModelUsageRecord> aModels)
    {
        var vTokenIds = aTokenEligible.Select(aE => aE.PhaseExecutionId).ToHashSet(StringComparer.Ordinal);
        var vCostIds = aCostEligible.Select(aE => aE.PhaseExecutionId).ToHashSet(StringComparer.Ordinal);

        return aModels
            .Where(aM => vTokenIds.Contains(aM.PhaseExecutionId))
            .GroupBy(aM => aM.Model, StringComparer.Ordinal)
            .OrderBy(aG => aG.Key, StringComparer.Ordinal)
            .Select(aG => new PhaseModelUsageView(
                aG.Key,
                aG.Select(aM => aM.PhaseExecutionId).Distinct(StringComparer.Ordinal).Count(),
                aG.Sum(aM => (long)(aM.Turns ?? 0)),
                aG.Sum(aM => aM.TokensIn ?? 0L),
                aG.Sum(aM => aM.TokensOut ?? 0L),
                MeasuredModelCost(aG.Where(aM => vCostIds.Contains(aM.PhaseExecutionId)).ToList())))
            .ToList();
    }

    /// <summary>Sums one model's dollars, but only the rows that reported them completely.</summary>
    /// <param name="aRows">The model's rows on cost-eligible executions.</param>
    /// <returns>The measured dollars, or <c>null</c> when none were complete.</returns>
    private static decimal? MeasuredModelCost(IReadOnlyList<PbPhaseModelUsageRecord> aRows)
    {
        var vComplete = aRows
            .Where(aM => aM.CostUsd is not null
                         && string.Equals(
                             aM.CostStatus,
                             PlaybookPhaseVocabulary.StatusComplete,
                             StringComparison.Ordinal))
            .ToList();

        return vComplete.Count == 0 ? null : vComplete.Sum(aM => aM.CostUsd ?? 0m);
    }

    /// <summary>
    /// Sums the fan-out counts over the windows whose scope could have seen a child (ADR-026).
    /// </summary>
    /// <remarks>
    /// A window that is not <c>tree</c> scope never read the sub-agent transcripts, so it did not report
    /// "no sub-agents" — it reported nothing, and it is excluded rather than counted as a zero.
    /// </remarks>
    /// <param name="aDuration">The closed, non-quarantined windows.</param>
    /// <param name="aKey">The export key.</param>
    /// <param name="aLabel">The page label.</param>
    /// <param name="aIsSpawned">True for spawned sessions, false for token contributors.</param>
    /// <returns>The figure with its cohort and exclusion.</returns>
    private static PhaseFigure FanoutFigure(
        IReadOnlyList<PbPhaseExecutionRecord> aDuration, string aKey, string aLabel, bool aIsSpawned)
    {
        var vObserved = aDuration
            .Where(aE => string.Equals(aE.TokensScope, "tree", StringComparison.Ordinal))
            .Where(aE => aE.SubagentsSpawned is not null && aE.SubagentsContributors is not null)
            .ToList();

        var vTotal = vObserved.Sum(aE => (long)(aIsSpawned ? aE.SubagentsSpawned!.Value : aE.SubagentsContributors!.Value));

        return new PhaseFigure(
            aKey,
            aLabel,
            PhaseValue.Measured(vTotal, vObserved.Count, vTotal.ToString("N0", CultureInfo.InvariantCulture)),
            vObserved.Count,
            [new PhaseExclusion(
                "scope_never_read_children",
                "the window was not tree scope, so it never read the sub-agent transcripts",
                aDuration.Count - vObserved.Count)]);
    }

    /// <summary>Groups the token cohort by <b>command phase</b>, with no conceptual-stage allocation.</summary>
    /// <param name="aJudged">Every row with its verdict.</param>
    /// <param name="aTokenEligible">The rows whose tokens may be totalled.</param>
    /// <returns>One group per command, ordered by name.</returns>
    private static IReadOnlyList<PhaseCommandGroup> CommandGroups(
        IReadOnlyList<JudgedExecution> aJudged, IReadOnlyList<PbPhaseExecutionRecord> aTokenEligible) =>
        aTokenEligible
            .GroupBy(aE => aE.Phase ?? PlaybookPhaseVocabulary.Unavailable, StringComparer.Ordinal)
            .OrderBy(aG => aG.Key, StringComparer.Ordinal)
            .Select(aG => new PhaseCommandGroup(
                aG.Key,
                aG.Count(),
                ElapsedFigure(
                    aJudged, aG.ToList(), "elapsed_ms_median", $"Median elapsed — {aG.Key}", true),
                TokenTotals(aJudged, aG.ToList())))
            .ToList();

    /// <summary>The exclusions every cohort inherits — quarantined rows and open windows.</summary>
    /// <param name="aJudged">Every row with its verdict.</param>
    /// <returns>The two shared exclusions.</returns>
    private static List<PhaseExclusion> DurationExclusions(IReadOnlyList<JudgedExecution> aJudged) =>
    [
        new(
            "quarantined",
            "rows failing an invariant or marked invalid by the producer",
            aJudged.Count(aJ => aJ.Validation.IsQuarantined)),
        new(
            "incomplete_window",
            "windows that ended at EOF, which have no duration at all",
            aJudged.Count(aJ => !aJ.Validation.IsQuarantined && aJ.Record.Complete != true))
    ];

    /// <summary>Builds one execution's table row, including its cost view and its sub-agent tree.</summary>
    /// <param name="aJudged">The row and its verdict.</param>
    /// <param name="aModels">Every per-model row.</param>
    /// <param name="aSubagents">Every sub-agent row.</param>
    /// <returns>The view.</returns>
    private static PhaseExecutionView ViewOf(
        JudgedExecution aJudged,
        IReadOnlyList<PbPhaseModelUsageRecord> aModels,
        IReadOnlyList<PbPhaseSubagentRecord> aSubagents)
    {
        var vRecord = aJudged.Record;
        var vOwnModels = ModelsOf(vRecord, aModels);

        return new PhaseExecutionView
        {
            PhaseExecutionId = vRecord.PhaseExecutionId,
            CommandPhase = vRecord.Phase,
            StartedAtUtc = vRecord.StartedAt,
            IsComplete = vRecord.Complete,
            EndReason = vRecord.EndReason,
            ElapsedMs = ElapsedOf(vRecord),
            ObservedActiveMs = ActiveOf(vRecord),
            ActiveCoverage = vRecord.ActiveCoverage,
            AssistantElapsed = new PhaseDiagnostic(
                "assistant_elapsed_ms", "Assistant intervals (diagnostic)", TextOf(vRecord.AssistantElapsedMs)),
            ToolElapsed = new PhaseDiagnostic(
                "tool_elapsed_ms", "Tool intervals (diagnostic)", TextOf(vRecord.ToolElapsedMs)),
            TokensInput = vRecord.TokensInput,
            TokensOutput = vRecord.TokensOutput,
            TokensReasoning = vRecord.TokensReasoning,
            TokensCacheRead = vRecord.TokensCacheRead,
            TokensCacheWrite = vRecord.TokensCacheWrite,
            Turns = vRecord.Turns,
            Cost = CostViewOf(vRecord, aModels),
            Models = vOwnModels.Select(aM => aM.Model).ToList(),
            DominantModelLabel = vRecord.DominantModel,
            Fanout = FanoutOf(vRecord, aSubagents),
            IsQuarantined = aJudged.Validation.IsQuarantined,
            QuarantineReasons = aJudged.Validation.Reasons,
            TokenStatus = vRecord.TokenStatus,
            AttemptSnapshot = vRecord.AttemptSnapshot,
            GateVerdictSnapshot = vRecord.GateVerdictSnapshot,
            ProjectType = vRecord.ProjectType,
            DataQualityNote = NoteOf(vRecord)
        };
    }

    /// <summary>The wall-clock figure for one row; an open window has none at all.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <returns>The figure.</returns>
    private static PhaseValue ElapsedOf(PbPhaseExecutionRecord aExecution) =>
        IsDurationEligible(aExecution)
            ? PhaseValue.Measured(aExecution.ElapsedMs!.Value, 1, MsOf(aExecution.ElapsedMs!.Value))
            : PhaseValue.Unavailable();

    /// <summary>
    /// The observed-active figure for one row: exact on complete coverage, an explicit lower bound on
    /// partial coverage, and nothing at all on unavailable coverage (REQ-FN-097).
    /// </summary>
    /// <param name="aExecution">The execution row.</param>
    /// <returns>The figure.</returns>
    private static PhaseValue ActiveOf(PbPhaseExecutionRecord aExecution)
    {
        if (aExecution.ObservedActiveMs is null)
        {
            return PhaseValue.Unavailable();
        }

        var vMs = aExecution.ObservedActiveMs.Value;

        return Coverage(aExecution) switch
        {
            PlaybookPhaseVocabulary.CoverageComplete => PhaseValue.Measured(vMs, 1, MsOf(vMs)),
            PlaybookPhaseVocabulary.CoveragePartial =>
                PhaseValue.Measured(vMs, 1, PlaybookPhaseVocabulary.LowerBoundPrefix + MsOf(vMs)),
            _ => PhaseValue.Unavailable()
        };
    }

    /// <summary>The cost view for one row, honouring the producer's status over its number.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aModels">Every per-model row.</param>
    /// <returns>The view.</returns>
    private static PhaseCostView CostViewOf(
        PbPhaseExecutionRecord aExecution, IReadOnlyList<PbPhaseModelUsageRecord> aModels)
    {
        var vStatus = CostStatusOf(aExecution, aModels);
        var vIsMeasured = IsMeasuredCostEligible(aExecution, aModels);

        var vCaveat = string.Equals(
            vStatus, PlaybookPhaseVocabulary.StatusZeroUnverified, StringComparison.Ordinal)
            ? PlaybookPhaseVocabulary.ZeroUnverifiedCaveat
            : null;

        return new PhaseCostView(vStatus, aExecution.CostUsd, vIsMeasured, vCaveat);
    }

    /// <summary>
    /// Builds the fan-out view and the recursive session tree for one execution (REQ-FN-100).
    /// </summary>
    /// <param name="aExecution">The execution row.</param>
    /// <param name="aSubagents">Every sub-agent row.</param>
    /// <returns>The view.</returns>
    private static PhaseFanoutView FanoutOf(
        PbPhaseExecutionRecord aExecution, IReadOnlyList<PbPhaseSubagentRecord> aSubagents)
    {
        var vOwn = aSubagents
            .Where(aS => aS.UserId == aExecution.UserId
                         && string.Equals(aS.Repo, aExecution.Repo, StringComparison.Ordinal)
                         && string.Equals(
                             aS.PhaseExecutionId, aExecution.PhaseExecutionId, StringComparison.Ordinal))
            .ToList();

        var vDifference = aExecution.SubagentsSpawned is null || aExecution.SubagentsContributors is null
            ? (int?)null
            : aExecution.SubagentsSpawned.Value - aExecution.SubagentsContributors.Value;

        return new PhaseFanoutView(
            aExecution.SubagentsContributors,
            aExecution.SubagentsSpawned,
            vDifference,
            ChildShareOf(aExecution, vOwn),
            TreeOf(vOwn));
    }

    /// <summary>
    /// The child share of output tokens — only where the denominator is positive (§6).
    /// </summary>
    /// <param name="aExecution">The execution row, whose totals already include the children.</param>
    /// <param name="aOwn">The execution's sub-agent rows.</param>
    /// <returns>The share, or <c>unavailable</c> when there is no positive denominator.</returns>
    private static PhaseValue ChildShareOf(
        PbPhaseExecutionRecord aExecution, IReadOnlyList<PbPhaseSubagentRecord> aOwn)
    {
        var vDenominator = aExecution.TokensOut;

        if (vDenominator is null || vDenominator <= 0 || aOwn.Count == 0)
        {
            return PhaseValue.Unavailable();
        }

        var vChildren = aOwn.Sum(aS => aS.TokensOut ?? 0L);
        var vShare = (double)vChildren / vDenominator.Value;

        return PhaseValue.Measured(
            vShare, 1, (100d * vShare).ToString("F0", CultureInfo.InvariantCulture) + "%");
    }

    /// <summary>
    /// Rebuilds the session tree from the parent links, placing every session exactly once.
    /// </summary>
    /// <remarks>
    /// A parent id naming a session no row reports is left where it is — a root of its own — rather than
    /// dropped, so a grandchild whose parent went missing is still visible and still counted once.
    /// </remarks>
    /// <param name="aOwn">The execution's sub-agent rows.</param>
    /// <returns>The roots of the tree.</returns>
    private static IReadOnlyList<PhaseSubagentNode> TreeOf(IReadOnlyList<PbPhaseSubagentRecord> aOwn)
    {
        var vKnown = aOwn.Select(aS => aS.SessionId).ToHashSet(StringComparer.Ordinal);

        var vRoots = aOwn
            .Where(aS => aS.ParentSessionId is null || !vKnown.Contains(aS.ParentSessionId))
            .ToList();

        return vRoots.Select(aS => NodeOf(aS, aOwn)).ToList();
    }

    /// <summary>Builds one node and, recursively, the sessions beneath it.</summary>
    /// <param name="aSession">The session row.</param>
    /// <param name="aOwn">The execution's sub-agent rows.</param>
    /// <returns>The node.</returns>
    private static PhaseSubagentNode NodeOf(
        PbPhaseSubagentRecord aSession, IReadOnlyList<PbPhaseSubagentRecord> aOwn)
    {
        var vChildren = aOwn
            .Where(aS => string.Equals(aS.ParentSessionId, aSession.SessionId, StringComparison.Ordinal))
            .Select(aS => NodeOf(aS, aOwn))
            .ToList();

        return new PhaseSubagentNode(
            aSession.SessionId,
            aSession.ParentSessionId,
            aSession.Agent ?? PlaybookPhaseVocabulary.Unavailable,
            aSession.TokensOut,
            aSession.CostUsd,
            vChildren);
    }

    /// <summary>The sentence a row needs a reader to see, or <c>null</c> when it needs none.</summary>
    /// <param name="aExecution">The execution row.</param>
    /// <returns>The note.</returns>
    private static string? NoteOf(PbPhaseExecutionRecord aExecution)
    {
        if (aExecution.Complete == false)
        {
            return PlaybookPhaseVocabulary.OpenWindowMessage;
        }

        return Coverage(aExecution) == PlaybookPhaseVocabulary.CoveragePartial
            ? PlaybookPhaseVocabulary.PartialCoverageMessage
            : null;
    }

    /// <summary>Renders a diagnostic component sum as text, which is the only form it is published in.</summary>
    /// <param name="aMs">The component sum, when the producer reported one.</param>
    /// <returns>The rendered text, or <c>unavailable</c>.</returns>
    private static string TextOf(long? aMs) => aMs is null ? PlaybookPhaseVocabulary.Unavailable : MsOf(aMs.Value);

    /// <summary>Renders a millisecond count.</summary>
    /// <param name="aMs">The count.</param>
    /// <returns>The rendered text.</returns>
    private static string MsOf(long aMs) => aMs.ToString("N0", CultureInfo.InvariantCulture) + " ms";

    /// <summary>The nearest-rank percentile of a sample.</summary>
    /// <param name="aValues">The sample.</param>
    /// <param name="aFraction">The percentile as a fraction, e.g. <c>0.90</c>.</param>
    /// <returns>The value, or <c>null</c> on an empty sample.</returns>
    private static double? Percentile(IReadOnlyList<double> aValues, double aFraction)
    {
        if (aValues.Count == 0)
        {
            return null;
        }

        var vSorted = aValues.OrderBy(aX => aX).ToList();
        var vRank = (int)Math.Ceiling(aFraction * vSorted.Count) - 1;

        return vSorted[Math.Clamp(vRank, 0, vSorted.Count - 1)];
    }

    /// <summary>One execution row paired with the verdict that decides whether it may be counted.</summary>
    /// <param name="Record">The stored row.</param>
    /// <param name="Validation">What the invariants found.</param>
    private sealed record JudgedExecution(PbPhaseExecutionRecord Record, PhaseValidation Validation);
}

/// <summary>
/// One <b>command phase</b> group — the measured slash command, never a conceptual lifecycle stage
/// (REQ-FN-098).
/// </summary>
/// <param name="CommandPhase">The command that ran.</param>
/// <param name="Executions">Executions in the group.</param>
/// <param name="ElapsedMsMedian">Median wall-clock duration inside the group.</param>
/// <param name="Tokens">The five token legs inside the group.</param>
public sealed record PhaseCommandGroup(
    string CommandPhase,
    int Executions,
    PhaseFigure ElapsedMsMedian,
    PhaseTokenTotals Tokens)
{
    /// <summary>The dimension's label, fixed so no surface can rename it to "phase".</summary>
    public static string DimensionLabel => PlaybookPhaseVocabulary.CommandPhaseLabel;

    /// <summary>The dimension's export key.</summary>
    public static string DimensionKey => PlaybookPhaseVocabulary.CommandPhaseKey;
}
