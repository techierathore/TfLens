using System.Globalization;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The miss and rework figures (REQ-FN-077, REQ-FN-078, REQ-FN-079; BRD-118..BRD-123).
/// </summary>
/// <remarks>
/// <para>
/// Every figure is <b>live-only</b> and segmented per <c>project_type</c> through
/// <see cref="Segment.ByProjectType{T}"/>, exactly as the three questions are, and every rate returns a
/// <see cref="Figure"/> so <c>insufficient data (n=…)</c> and <i>not applicable</i> are unrepresentable
/// as numbers. Amendments are folded through <see cref="MissAmendFolder.Fold"/> before anything here is
/// counted, so a <c>why_missed</c> supplied only by an amendment reaches the failed-practice
/// distribution (REQ-FN-075).
/// </para>
/// <para>
/// Four rules shape this class and none of them is a switch (REQ-NFR-013):
/// </para>
/// <list type="number">
/// <item><description>
/// The failed-practice denominator is records that <i>carry</i> <c>why_missed</c>, read against the
/// eligibility floor in <see cref="MetricsConstants.FieldSince"/> — never the miss count.
/// </description></item>
/// <item><description>
/// <c>wont-fix</c> is its own figure and is never folded into open. <c>deferred</c> stays open. The
/// producer's collapse check asks a different question and the two are deliberately not reconciled.
/// </description></item>
/// <item><description>
/// Every per-origin figure comes from <see cref="MissAttributionTaint"/>, so an <c>inferred</c>
/// attribution can never reach a per-phase, per-model or per-agent number, and the excluded count leaves
/// the engine as data.
/// </description></item>
/// <item><description>
/// Token cost returns a <see cref="MissCost"/>, which has no property that could hold a blended
/// measured-plus-apportioned number, and measured dollars come from OpenCode records only.
/// </description></item>
/// </list>
/// <para>
/// Nothing here touches <see cref="EscapeRate"/>. The miss-stream escape share is a second, adjacent
/// figure; the <c>gates</c>-derived escape rate keeps its definition and its source untouched.
/// </para>
/// </remarks>
public static class MissFigures
{
    /// <summary>The <c>miss_class</c> that means the requirement was never written down.</summary>
    public const string DesignMissClass = "unspecified-gap";

    /// <summary>The verdict that closes a miss.</summary>
    public const string VerifiedVerdict = "Verified";

    /// <summary>The verdict that declines a miss — its own figure, never folded into open.</summary>
    public const string WontFixVerdict = "wont-fix";

    /// <summary><c>cost_attribution</c> for a fix run that repaired exactly one miss.</summary>
    public const string SoleAttribution = "sole";

    /// <summary><c>cost_attribution</c> prefix for a fix run that repaired several misses.</summary>
    public const string SharedAttributionPrefix = "shared:";

    /// <summary><c>cost_attribution</c> for a fix that can carry no cost — a count, never a divisor.</summary>
    public const string NoneAttribution = "none";

    /// <summary>The one harness whose <c>cost_usd</c> is a measurement (SCHEMA.md §4).</summary>
    public const string OpenCodeHarness = "opencode";

    /// <summary><c>found_by</c> values that mean no gate caught it before it reached a human.</summary>
    public static readonly IReadOnlyList<string> EscapeFoundBy = ["owner", "production"];

    /// <summary>Wire field name behind <see cref="MissSegmentFigures.ClassDistribution"/>.</summary>
    public const string MissClassField = "miss_class";

    /// <summary>Wire field name behind <see cref="MissSegmentFigures.FoundBy"/>.</summary>
    public const string FoundByField = "found_by";

    /// <summary>
    /// Every wire field a distribution here is computed over, in the order the page prints them.
    /// </summary>
    /// <remarks>
    /// <see cref="MissSegmentFigures.AmendedValues"/> carries one entry per name on this list, so a
    /// distribution can never be rendered without the count of values an amendment completed beside it
    /// (BRD-176). Two of the four are not amendable at all and report <c>0</c>; that is an answer, and
    /// leaving the key out would not be.
    /// </remarks>
    public static readonly IReadOnlyList<string> DistributionFields =
    [
        MissClassField,
        MissAmendFolder.WhyMissedField,
        MissSorts.Field,
        FoundByField
    ];

    /// <summary>
    /// Computes the whole miss block for one user and one framework.
    /// </summary>
    /// <param name="aMisses">Every stored <c>miss</c> row, live and backfilled, unfolded.</param>
    /// <param name="aFixes">Every stored <c>miss-fix</c> row.</param>
    /// <param name="aAmends">Every stored <c>miss-amend</c> row.</param>
    /// <param name="aRuns">Every run record for the framework — the denominator of the per-phase rate.</param>
    /// <returns>The miss block, with zeros rather than absence when the stream is empty.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static MissAnalysis Compute(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes,
        IReadOnlyList<MissAmendRecord> aAmends,
        IReadOnlyList<RunRecord> aRuns)
    {
        ArgumentNullException.ThrowIfNull(aMisses);
        ArgumentNullException.ThrowIfNull(aFixes);
        ArgumentNullException.ThrowIfNull(aAmends);
        ArgumentNullException.ThrowIfNull(aRuns);

        // ---- read-time amendment folding, before a single figure is counted (REQ-FN-075).
        var vFolded = MissAmendFolder.Fold(aMisses, aAmends);

        // ---- live only, and the excluded halves are counted rather than dropped.
        var vLiveMisses = vFolded.Misses.Where(aMiss => aMiss.Backfilled != true).ToList();
        var vLiveFixes = aFixes.Where(aFix => aFix.Backfilled != true).ToList();

        // Orphans are judged against every stored miss, live or backfilled: a fix is only an orphan when
        // TfLens holds no miss for it at all, never merely because its parent sits in the other bucket.
        var vKnownMisses = vFolded.Misses
            .Select(aMiss => LinkKey(aMiss.Repo, aMiss.MissId))
            .ToHashSet(StringComparer.Ordinal);

        var vFixesByMiss = FixesByMiss(vLiveFixes);
        var vSegmentOf = SegmentOfMiss(vLiveMisses);

        var vSegments = new SortedDictionary<string, MissSegmentFigures>(StringComparer.Ordinal);
        foreach (var vBucket in Segment.ByProjectType(
                     vLiveMisses,
                     aMiss => aMiss.ProjectType,
                     aMiss => aMiss.ProjectTypeInferred))
        {
            var vFixesHere = vLiveFixes
                .Where(aFix => SegmentOfFix(aFix, vSegmentOf) == vBucket.Key)
                .ToList();

            var vRunsHere = aRuns
                .Where(aRun => aRun.Backfilled != true
                    && Segment.KeyFor(aRun.ProjectType, aRun.ProjectTypeInferred) == vBucket.Key)
                .ToList();

            vSegments[vBucket.Key] = FiguresFor(
                vBucket.Value, vFixesHere, vRunsHere, vFixesByMiss, vKnownMisses, vFolded);
        }

        // A fix whose parent miss lives in no live segment still has to be counted somewhere, or the
        // per-repo totals and the segment totals would silently disagree.
        var vOrphanFixes = vLiveFixes.Count(aFix => !vKnownMisses.Contains(LinkKey(aFix.Repo, aFix.MissId)));

        return new MissAnalysis
        {
            MissesTotal = vLiveMisses.Count,
            MissFixesTotal = vLiveFixes.Count,
            OrphanFixes = vOrphanFixes,
            OpenMisses = vLiveMisses.Count(aMiss => IsOpen(aMiss, vFixesByMiss)),
            WontFix = vLiveMisses.Count(aMiss => IsWontFix(aMiss, vFixesByMiss)),
            ResolvedMisses = vLiveMisses.Count(aMiss => IsResolved(aMiss, vFixesByMiss)),
            // The eligibility floor applies here too (REQ-FN-076). An escape written before
            // `why_missed` existed had no field to leave empty — counting it would raise the warning
            // loudest against the oldest records, which are precisely the ones nobody can complete.
            // The reference implementation bounds this the same way; without it the two disagree on
            // any repository holding pre-2026-08-28 escapes, and BRD §13 is zero-tolerance.
            EscapesMissingWhy = vLiveMisses.Count(aMiss =>
                IsEscape(aMiss)
                && aMiss.WhyMissed is null
                && LateGateCoverageCalculator.IsEligibleForField(MissAmendFolder.WhyMissedField, aMiss.Ts)),
            AmendmentsApplied = vFolded.AmendmentsApplied,
            AmendmentsIgnored = vFolded.AmendmentsIgnored,
            OrphanAmends = vFolded.OrphanAmends,
            BackfilledMissesExcluded = vFolded.Misses.Count - vLiveMisses.Count,
            BackfilledMissFixesExcluded = aFixes.Count - vLiveFixes.Count,
            Live = vSegments
        };
    }

    /// <summary>
    /// The figure block for one project type.
    /// </summary>
    /// <param name="aMisses">The segment's live misses, amendments already folded.</param>
    /// <param name="aFixes">The live fix records attributed to this segment.</param>
    /// <param name="aRuns">The segment's live runs — the per-phase rate's denominator.</param>
    /// <param name="aFixesByMiss">Every live fix, indexed by the miss it names.</param>
    /// <param name="aKnownMisses">Link keys of every stored miss, for the orphan test.</param>
    /// <param name="aFold">The read-time fold, for the values an amendment completed here.</param>
    /// <returns>The segment's figures.</returns>
    private static MissSegmentFigures FiguresFor(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes,
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyDictionary<string, List<MissFixRecord>> aFixesByMiss,
        IReadOnlySet<string> aKnownMisses,
        MissFoldResult aFold)
    {
        // ---- what was missed. The denominator is records that carry the field, not the miss count.
        var vClassCounts = CountBy(aMisses, aMiss => aMiss.MissClass);
        var vClassN = vClassCounts.Sum(aEntry => aEntry.Value);

        // ---- which practice failed (BRD-119), read against the eligibility floor (REQ-FN-076).
        var vEligibility = LateGateCoverageCalculator.EligibilityFor(
            MissAmendFolder.WhyMissedField,
            aMisses,
            aMiss => aMiss.Ts,
            aMiss => aMiss.WhyMissed);

        var vWhyCounts = CountBy(aMisses, aMiss => aMiss.WhyMissed);
        var vWhyN = vWhyCounts.Sum(aEntry => aEntry.Value);

        var vFoundByCounts = CountBy(aMisses, aMiss => aMiss.FoundBy);

        // ---- whose gap it was (BRD-170, BRD-172). Same shape as why_missed and the same floor: the
        // denominator is the misses ELIGIBLE to carry the field, and the records that predate it are
        // reported apart rather than pooled in as unsorted.
        var vSortEligibility = LateGateCoverageCalculator.EligibilityFor(
            MissSorts.Field,
            aMisses,
            aMiss => aMiss.Ts,
            aMiss => aMiss.Sort);

        var vSortCounts = CountBy(aMisses, aMiss => aMiss.Sort);
        var vSortN = vSortCounts.Sum(aEntry => aEntry.Value);

        var vDesignMisses = aMisses.Count(aMiss =>
            string.Equals(aMiss.MissClass, DesignMissClass, StringComparison.Ordinal));
        var vEscapes = aMisses.Count(IsEscape);

        return new MissSegmentFigures
        {
            Misses = aMisses.Count,
            MissFixes = aFixes.Count,
            OrphanFixes = aFixes.Count(aFix => !aKnownMisses.Contains(LinkKey(aFix.Repo, aFix.MissId))),
            OpenMisses = aMisses.Count(aMiss => IsOpen(aMiss, aFixesByMiss)),
            WontFix = aMisses.Count(aMiss => IsWontFix(aMiss, aFixesByMiss)),
            ResolvedMisses = aMisses.Count(aMiss => IsResolved(aMiss, aFixesByMiss)),
            ClassDistribution = Rows(vClassCounts, vClassN),
            ClassDistributionN = vClassN,
            ClassDistributionNote = Note(vClassN),
            ClassNotRecorded = aMisses.Count - vClassN,
            FailedPracticeDistribution = Rows(vWhyCounts, vWhyN),
            WhyMissedN = vWhyN,
            WhyMissedEligibility = vEligibility,
            FailedPracticeNote = Note(vWhyN),
            FoundBy = Rows(vFoundByCounts, vFoundByCounts.Sum(aEntry => aEntry.Value)),
            FoundByNotRecorded = aMisses.Count - vFoundByCounts.Sum(aEntry => aEntry.Value),
            DesignMissShare = Share(vDesignMisses, aMisses.Count),
            EscapeShare = Share(vEscapes, aMisses.Count),
            MedianTimeToCloseHours = MedianTimeToClose(aMisses, aFixesByMiss),
            Attribution = AttributionFor(aMisses, aRuns),
            Cost = MoneyFor(aFixes),
            SortDistribution = SortRows(vSortCounts, vSortN),
            SortN = vSortN,
            SortEligibility = vSortEligibility,
            SortUnrecognised = aMisses.Count(aMiss =>
                !string.IsNullOrWhiteSpace(aMiss.Sort) && !MissSorts.IsRecognised(aMiss.Sort)),
            SortDistributionNote = Note(vSortN),
            AmendedValues = AmendedValuesFor(aMisses, aFold)
        };
    }

    /// <summary>
    /// Renders the <c>sort</c> distribution, marking any value outside the four (BRD-170).
    /// </summary>
    /// <remarks>
    /// <b>An unrecognised value gets its own row, under its own name.</b> It is never mapped to the
    /// nearest of the four and never filtered out of the distribution: coercion would file a miss under
    /// a remedy nobody chose, and filtering would leave totals that look entirely normal while records
    /// went missing. Both failures are invisible to a reader downstream, which is why the flag travels
    /// with the row rather than being recomputed by whoever renders it.
    /// </remarks>
    /// <param name="aCounts">The counts from <see cref="CountBy{T}"/>.</param>
    /// <param name="aDenominator">Records carrying a <c>sort</c> — never the miss count.</param>
    /// <returns>One row per stored value observed, ordinally ordered.</returns>
    private static IReadOnlyList<MissCategoryCount> SortRows(
        IReadOnlyDictionary<string, int> aCounts,
        int aDenominator) =>
        aCounts
            .Select(aEntry => new MissCategoryCount(
                aEntry.Key,
                aEntry.Value,
                MetricsConstants.Pct(aEntry.Value, aDenominator),
                MissSorts.IsRecognised(aEntry.Key)))
            .ToList();

    /// <summary>
    /// How many values in this segment each distribution's field owes to a <c>miss-amend</c> (BRD-176).
    /// </summary>
    /// <remarks>
    /// Counted over the segment's own misses rather than read off the fold's total, because a total
    /// spanning every project type would be attached to a distribution that spans one. A field no
    /// amendment may complete is present with a <c>0</c>: a distribution with no count beside it is
    /// exactly the silent fold this clause exists to end.
    /// </remarks>
    /// <param name="aMisses">The segment's live misses, amendments already folded.</param>
    /// <param name="aFold">The read-time fold, carrying every completion it applied.</param>
    /// <returns>One entry per name in <see cref="DistributionFields"/>.</returns>
    private static IReadOnlyDictionary<string, int> AmendedValuesFor(
        IReadOnlyList<MissRecord> aMisses,
        MissFoldResult aFold)
    {
        var vHere = aMisses
            .Select(aMiss => LinkKey(aMiss.Repo, aMiss.MissId))
            .ToHashSet(StringComparer.Ordinal);

        var vCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var vField in DistributionFields)
        {
            vCounts[vField] = aFold.Completions.Count(aCompletion =>
                string.Equals(aCompletion.Field, vField, StringComparison.Ordinal)
                && vHere.Contains(LinkKey(aCompletion.Repo, aCompletion.MissId)));
        }

        return vCounts;
    }

    /// <summary>
    /// The <c>linked</c>-only per-origin figures, and the exclusion that produced them (REQ-FN-078).
    /// </summary>
    /// <param name="aMisses">The segment's live misses.</param>
    /// <param name="aRuns">The segment's live runs.</param>
    /// <returns>The attribution block, carrying its own excluded count and reason.</returns>
    private static MissAttributionFigures AttributionFor(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<RunRecord> aRuns)
    {
        var vSet = MissAttributionTaint.Partition(aMisses);

        var vPhaseCounts = CountBy(vSet.Linked, aMiss => aMiss.OriginPhase);
        var vPhaseN = vPhaseCounts.Sum(aEntry => aEntry.Value);
        var vModelCounts = CountBy(vSet.Linked, aMiss => aMiss.OriginModel);
        var vAgentCounts = CountBy(vSet.Linked, aMiss => aMiss.OriginAgent);

        var vRates = new List<MissPhaseRate>();
        foreach (var vPhase in vPhaseCounts)
        {
            var vRuns = aRuns.Count(aRun => string.Equals(aRun.Cmd, vPhase.Key, StringComparison.Ordinal));
            vRates.Add(new MissPhaseRate(vPhase.Key, vPhase.Value, vRuns, Share(vPhase.Value, vRuns)));
        }

        // REQ-UI-038 — the model card's denominator is the runs that model actually did, read off the
        // run's observed `model` exactly as the per-phase rate reads the run's `cmd`. The numerator is
        // the same linked-only count as ByOriginModel, so the two can never disagree.
        var vModelRates = vModelCounts
            .Select(aModel =>
            {
                var vRuns = aRuns.Count(aRun => string.Equals(aRun.Model, aModel.Key, StringComparison.Ordinal));
                return new MissModelRate(aModel.Key, aModel.Value, vRuns, PerHundredRuns(aModel.Value, vRuns));
            })
            .ToList();

        return new MissAttributionFigures
        {
            AttributedN = vSet.AttributedN,
            AttributionExcluded = vSet.AttributionExcluded,
            ExclusionReason = vSet.Reason,
            ExcludedByConfidence = vSet.ExcludedByConfidence,
            ByOriginPhase = Rows(vPhaseCounts, vPhaseN),
            ByOriginModel = Rows(vModelCounts, vModelCounts.Sum(aEntry => aEntry.Value)),
            ByOriginAgent = Rows(vAgentCounts, vAgentCounts.Sum(aEntry => aEntry.Value)),
            MissRatePerOriginPhase = vRates,
            MissRatePerOriginModel = vModelRates
        };
    }

    /// <summary>
    /// Misses per 100 runs, to one decimal place, refusing below the minimum-n floor on either side.
    /// </summary>
    /// <remarks>
    /// A per-model rate stands on two counts and is only as good as the smaller: a model with two misses
    /// is not a rate however many runs it did, and a model with two runs is not a rate however many misses
    /// named it (REQ-UI-038 — "a row under the minimum n renders insufficient data and never a rate").
    /// No live run at all is <i>not applicable</i>, never a zero and never an infinity.
    /// </remarks>
    /// <param name="aMisses">Linked misses naming the model.</param>
    /// <param name="aRuns">Live runs of that model in the segment.</param>
    /// <returns>The rate, <c>insufficient data (n=…)</c>, or not applicable.</returns>
    private static Figure PerHundredRuns(int aMisses, int aRuns)
    {
        if (aRuns == 0)
        {
            return Figure.NotApplicable();
        }

        if (aMisses < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aMisses);
        }

        if (aRuns < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aRuns, "runs");
        }

        var vRate = 100.0 * aMisses / aRuns;
        return Figure.Value(vRate, aRuns, vRate.ToString("F1", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The rework money block: tokens split by attribution, dollars split by harness (REQ-FN-079).
    /// </summary>
    /// <remarks>
    /// The headline column counts <c>sole</c> records only, so a <c>shared:3</c> record can never reach
    /// it. Apportioning divides that record's window by its own <c>n</c>, which is arithmetic and is
    /// reported as its own column rather than added to the other. Measured dollars are summed for
    /// OpenCode alone and never across harnesses.
    /// </remarks>
    /// <param name="aFixes">The segment's live fix records.</param>
    /// <returns>The money block.</returns>
    private static MissMoney MoneyFor(IReadOnlyList<MissFixRecord> aFixes)
    {
        // RECOMPUTE the share per fix run before bucketing; do NOT trust the stored
        // `cost_attribution` (SCHEMA.md §8 — a derived metric is computed at report time).
        //
        // This used to read the stored string, and BRD §13 caught it on 2026-08-29 when the
        // reference implementation started recomputing. Two reasons the stored value cannot be
        // trusted, both of which the stream can prove about itself:
        //
        //   1. It is written one record at a time. A run that closed four misses stamped
        //      shared:1, shared:2, shared:3, shared:4 — only the last is right, and the stream is
        //      append-only so none of the first three can be corrected in place.
        //   2. Records written before 2026-08-28 carry "none" from the empty-`reqs_touched` bug:
        //      a `framework` or `docs` repo has no REQs, so the divisor collapsed and every
        //      measured window in those repos was discarded as unattributable.
        //
        // Counting the miss_ids actually closed against each fix_run_id recovers both cases from
        // data already on the stream, which is why RecoveredRecords is reported beside the split:
        // a jump in the cost figures should read as a fixed derivation, not as work getting dearer.
        var vClosedPerRun = ClosedPerRun(aFixes);

        var vSole = new List<MissFixRecord>();
        var vShared = new List<(MissFixRecord Fix, int Across)>();
        var vNone = 0;
        var vRecovered = 0;

        foreach (var vFix in aFixes)
        {
            var vComputed = ComputedAttribution(vFix, vClosedPerRun);

            if (vComputed is null)
            {
                vNone++;
                continue;
            }

            if (string.Equals(vFix.CostAttribution, NoneAttribution, StringComparison.Ordinal))
            {
                // The stream had written this window off; the recomputed divisor gets it back.
                vRecovered++;
            }

            if (vComputed.Value == 1)
            {
                vSole.Add(vFix);
            }
            else
            {
                vShared.Add((vFix, vComputed.Value));
            }
        }

        // THE TOKEN DIVISOR IS THE RECORDS THAT CARRY A COUNT, NEVER ALL OF THEM — and the divisor
        // is published beside the figure.
        //
        // This was TfLens TF-005 / DECISIONS.md D-012, raised here as a deliberate divergence from a
        // reference that computed `sum(tokens_out or 0) / len(sole)`. `or 0` cannot tell an absent
        // field from a recorded zero, so averaging an unpriced repair in counts it as a FREE repair
        // and understates rework — in the direction that flatters the framework, which is the exact
        // failure this product exists to expose (BRD-31..36: absent renders as an absence, never
        // as 0). The reference adopted the same divisor on 2026-08-31 and the divergence is closed.
        //
        // What is new here is the SECOND half of that fix, which is what made agreement possible at
        // all: the denominator leaves the engine as data (`MeasuredTokenRecords`,
        // `ApportionedTokenRecords`) alongside how many records had to be left out of it
        // (`SoleTokensUnrecorded`, `SharedTokensUnrecorded`). SoleRecords/SharedRecords still carry
        // the RECORD counts separately, so excluding an unpriced record from the divisor loses no
        // information — and a consumer can reproduce either figure exactly rather than having to
        // choose between agreeing with us and being right (BRD-146/149: a denominator sits beside
        // its figure).
        var vSoleTokens = vSole
            .Where(aFix => aFix.TokensOut.HasValue)
            .Select(aFix => (double)aFix.TokensOut!.Value)
            .ToList();

        var vApportionedTokens = vShared
            .Where(aEntry => aEntry.Fix.TokensOut.HasValue)
            .Select(aEntry => (double)aEntry.Fix.TokensOut!.Value / aEntry.Across)
            .ToList();

        return new MissMoney
        {
            TokensPerMissFixed = new MissCost(
                MeanPerRecord(vSoleTokens),
                MeanPerRecord(vApportionedTokens),
                vNone),
            SoleRecords = vSole.Count,
            SharedRecords = vShared.Count,
            MeasuredTokenRecords = vSoleTokens.Count,
            SoleTokensUnrecorded = vSole.Count - vSoleTokens.Count,
            ApportionedTokenRecords = vApportionedTokens.Count,
            SharedTokensUnrecorded = vShared.Count - vApportionedTokens.Count,
            RecoveredRecords = vRecovered,
            AttributionMissing = 0,
            ByHarness = ExtraMetrics.HarnessOrder.Select(aHarness => HarnessRow(aHarness, aFixes)).ToList()
        };
    }

    /// <summary>
    /// The fix records whose run closed exactly one miss, recomputed from the stream (REQ-FN-079).
    /// </summary>
    /// <remarks>
    /// Exposed because the <c>cost_usd_*</c> keys are bounded by the same <c>sole</c> set the token
    /// columns are, and the boundary has to be drawn the same way in both places. Reading the stored
    /// <c>cost_attribution</c> to draw it would reintroduce, in the money column, precisely the defect
    /// <see cref="ComputedAttribution"/> exists to correct: see the remarks there.
    /// </remarks>
    /// <param name="aFixes">The fix records to bound.</param>
    /// <returns>The subset whose recomputed attribution is <c>sole</c>, in input order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aFixes"/> is <c>null</c>.</exception>
    public static IReadOnlyList<MissFixRecord> SoleFixes(IReadOnlyList<MissFixRecord> aFixes)
    {
        ArgumentNullException.ThrowIfNull(aFixes);

        var vClosedPerRun = ClosedPerRun(aFixes);
        return aFixes.Where(aFix => ComputedAttribution(aFix, vClosedPerRun) == 1).ToList();
    }

    /// <summary>
    /// Indexes the distinct miss ids each fix run closed — the recomputed cost divisor's only input.
    /// </summary>
    /// <param name="aFixes">The fix records to index.</param>
    /// <returns>Miss ids closed, keyed by <c>fix_run_id</c>.</returns>
    private static Dictionary<string, HashSet<string>> ClosedPerRun(IEnumerable<MissFixRecord> aFixes)
    {
        var vClosedPerRun = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var vFix in aFixes)
        {
            if (string.IsNullOrEmpty(vFix.FixRunId) || string.IsNullOrEmpty(vFix.MissId))
            {
                continue;
            }

            if (!vClosedPerRun.TryGetValue(vFix.FixRunId, out var vClosed))
            {
                vClosed = new HashSet<string>(StringComparer.Ordinal);
                vClosedPerRun[vFix.FixRunId] = vClosed;
            }

            vClosed.Add(vFix.MissId);
        }

        return vClosedPerRun;
    }

    /// <summary>
    /// One harness's money row — measured dollars for OpenCode, tokens for everyone else (BRD-123).
    /// </summary>
    /// <param name="aHarness">The harness the row is for.</param>
    /// <param name="aFixes">The segment's live fix records.</param>
    /// <returns>The row; a harness with no records still gets one, rendered as em dashes.</returns>
    private static MissHarnessCost HarnessRow(string aHarness, IReadOnlyList<MissFixRecord> aFixes)
    {
        var vHere = aFixes
            .Where(aFix => string.Equals(aFix.Harness, aHarness, StringComparison.Ordinal))
            .ToList();

        var vIsMeasured = string.Equals(aHarness, OpenCodeHarness, StringComparison.Ordinal);

        // The rule Pooled.cs and ExtraMetrics.cs already own: cost_usd is a measurement on OpenCode and
        // on nothing else, and it is never summed across harnesses. A cost_usd on another harness's
        // record is not read here at all rather than being quietly totalled into a money figure.
        var vMeasured = vIsMeasured
            ? vHere.Where(aFix => aFix.CostUsd.HasValue).Select(aFix => aFix.CostUsd!.Value).ToList()
            : [];

        return new MissHarnessCost(
            aHarness,
            vHere.Count,
            vHere.Count(aFix => aFix.TokensIn.HasValue
                || aFix.TokensOut.HasValue
                || aFix.TokensCacheRead.HasValue
                || aFix.TokensCacheWrite.HasValue),
            vHere.Sum(aFix => (long)(aFix.TokensIn ?? 0)),
            vHere.Sum(aFix => (long)(aFix.TokensOut ?? 0)),
            vHere.Sum(aFix => (long)(aFix.TokensCacheRead ?? 0)),
            vHere.Sum(aFix => (long)(aFix.TokensCacheWrite ?? 0)),
            MeanUsdPerRecord(vMeasured),
            vMeasured.Count == 0 ? null : vMeasured.Sum(),
            vMeasured.Count,
            vIsMeasured ? null : RateCard.EstimateLabel);
    }

    /// <summary>
    /// Median hours from a miss to the fix that verified it, to two decimal places.
    /// </summary>
    /// <remarks>
    /// Only misses whose latest verdict is <c>Verified</c> are timed. A <c>wont-fix</c> is a decision
    /// rather than a close, and a <c>deferred</c> miss has not closed at all; timing either would report
    /// outstanding work as finished work.
    /// </remarks>
    /// <param name="aMisses">The segment's live misses.</param>
    /// <param name="aFixesByMiss">Every live fix, indexed by the miss it names.</param>
    /// <returns>The median, or an honest refusal below <see cref="MetricsConstants.MinN"/> closed misses.</returns>
    private static Figure MedianTimeToClose(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyDictionary<string, List<MissFixRecord>> aFixesByMiss)
    {
        var vHours = new List<double>();
        foreach (var vMiss in aMisses)
        {
            if (!IsResolved(vMiss, aFixesByMiss) || LatestFix(vMiss, aFixesByMiss) is not { } vFix)
            {
                continue;
            }

            if (Instant(vMiss.Ts) is { } vOpened && Instant(vFix.Ts) is { } vClosed && vClosed >= vOpened)
            {
                vHours.Add((vClosed - vOpened).TotalHours);
            }
        }

        if (vHours.Count < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(vHours.Count);
        }

        var vMedian = Math.Round(MetricsConstants.Median(vHours)!.Value, 2, MidpointRounding.ToEven);
        return Figure.Value(vMedian, vHours.Count, vMedian.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Whether a miss is still outstanding work (BRD-120).
    /// </summary>
    /// <remarks>
    /// Latest <c>VerdictAfter</c> outside <c>{Verified, wont-fix}</c>, and a miss no fix has touched is
    /// open by the same reading. <c>deferred</c> stays open. This predicate is deliberately <b>not</b>
    /// reconciled with the producer's collapse check, which keeps a <c>wont-fix</c> live because the next
    /// failure on that REQ is the same defect — the two ask different questions and agreeing would break
    /// one of them.
    /// </remarks>
    /// <param name="aMiss">The miss.</param>
    /// <param name="aFixesByMiss">Every live fix, indexed by the miss it names.</param>
    /// <returns><c>true</c> when the miss belongs in the backlog.</returns>
    private static bool IsOpen(MissRecord aMiss, IReadOnlyDictionary<string, List<MissFixRecord>> aFixesByMiss)
    {
        var vVerdict = LatestFix(aMiss, aFixesByMiss)?.VerdictAfter;
        return !string.Equals(vVerdict, VerifiedVerdict, StringComparison.Ordinal)
            && !string.Equals(vVerdict, WontFixVerdict, StringComparison.Ordinal);
    }

    /// <summary>Whether a miss was deliberately declined — its own figure, never part of open.</summary>
    /// <param name="aMiss">The miss.</param>
    /// <param name="aFixesByMiss">Every live fix, indexed by the miss it names.</param>
    /// <returns><c>true</c> when the latest verdict is <c>wont-fix</c>.</returns>
    private static bool IsWontFix(MissRecord aMiss, IReadOnlyDictionary<string, List<MissFixRecord>> aFixesByMiss) =>
        string.Equals(LatestFix(aMiss, aFixesByMiss)?.VerdictAfter, WontFixVerdict, StringComparison.Ordinal);

    /// <summary>Whether a miss was repaired and verified.</summary>
    /// <param name="aMiss">The miss.</param>
    /// <param name="aFixesByMiss">Every live fix, indexed by the miss it names.</param>
    /// <returns><c>true</c> when the latest verdict is <c>Verified</c>.</returns>
    private static bool IsResolved(MissRecord aMiss, IReadOnlyDictionary<string, List<MissFixRecord>> aFixesByMiss) =>
        string.Equals(LatestFix(aMiss, aFixesByMiss)?.VerdictAfter, VerifiedVerdict, StringComparison.Ordinal);

    /// <summary>Whether the miss reached a human before any gate caught it.</summary>
    /// <param name="aMiss">The miss.</param>
    /// <returns><c>true</c> when <c>found_by</c> is <c>owner</c> or <c>production</c>.</returns>
    private static bool IsEscape(MissRecord aMiss) =>
        aMiss.FoundBy is not null && EscapeFoundBy.Contains(aMiss.FoundBy, StringComparer.Ordinal);

    /// <summary>The latest fix record for a miss, by timestamp then by fix attempt.</summary>
    /// <param name="aMiss">The miss.</param>
    /// <param name="aFixesByMiss">Every live fix, indexed by the miss it names.</param>
    /// <returns>The latest fix, or <c>null</c> when no fix has touched the miss.</returns>
    private static MissFixRecord? LatestFix(
        MissRecord aMiss,
        IReadOnlyDictionary<string, List<MissFixRecord>> aFixesByMiss) =>
        aFixesByMiss.TryGetValue(LinkKey(aMiss.Repo, aMiss.MissId), out var vFixes)
            ? vFixes
                .OrderBy(aFix => aFix.Ts, StringComparer.Ordinal)
                .ThenBy(aFix => aFix.FixAttempt ?? 0)
                .LastOrDefault()
            : null;

    /// <summary>Indexes the live fix records by the miss they name.</summary>
    /// <param name="aFixes">The live fix records.</param>
    /// <returns>The index, keyed by repository and miss id.</returns>
    private static Dictionary<string, List<MissFixRecord>> FixesByMiss(IEnumerable<MissFixRecord> aFixes)
    {
        var vIndex = new Dictionary<string, List<MissFixRecord>>(StringComparer.Ordinal);
        foreach (var vFix in aFixes)
        {
            var vKey = LinkKey(vFix.Repo, vFix.MissId);
            if (!vIndex.TryGetValue(vKey, out var vBucket))
            {
                vBucket = [];
                vIndex[vKey] = vBucket;
            }

            vBucket.Add(vFix);
        }

        return vIndex;
    }

    /// <summary>Maps each miss's link key to the segment it was counted in.</summary>
    /// <param name="aMisses">The live misses.</param>
    /// <returns>The map, so a fix can be counted where its parent is.</returns>
    private static Dictionary<string, string> SegmentOfMiss(IEnumerable<MissRecord> aMisses)
    {
        var vMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var vMiss in aMisses)
        {
            vMap.TryAdd(
                LinkKey(vMiss.Repo, vMiss.MissId),
                Segment.KeyFor(vMiss.ProjectType, vMiss.ProjectTypeInferred));
        }

        return vMap;
    }

    /// <summary>
    /// The segment a fix record is counted in — its parent miss's, or its own when it is an orphan.
    /// </summary>
    /// <remarks>
    /// A fix belongs with the miss it repaired, or the cost of repairing an <c>app</c> miss could land in
    /// the <c>library</c> column because the repairing run happened to carry a different classification
    /// (SCHEMA.md §0.5 — records keep the type they were written with).
    /// </remarks>
    /// <param name="aFix">The fix record.</param>
    /// <param name="aSegmentOf">Where each miss was counted.</param>
    /// <returns>The segment key.</returns>
    private static string SegmentOfFix(MissFixRecord aFix, IReadOnlyDictionary<string, string> aSegmentOf) =>
        aSegmentOf.TryGetValue(LinkKey(aFix.Repo, aFix.MissId), out var vSegment)
            ? vSegment
            : Segment.KeyFor(aFix.ProjectType, aFix.ProjectTypeInferred);

    /// <summary>
    /// The link key, scoped to the repository as <see cref="MissAmendFolder"/> scopes it.
    /// </summary>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <param name="aMissId">The miss id, unique only within the app that minted it.</param>
    /// <returns>The composite key.</returns>
    private static string LinkKey(string aRepo, string aMissId) => aRepo + " " + aMissId;

    /// <summary>
    /// Counts records by an optional field, leaving the records that do not carry it out entirely.
    /// </summary>
    /// <remarks>
    /// <b>This is where the honest denominator comes from.</b> A <c>null</c> is not assessed — it is not a
    /// bucket, not an <c>other</c> and not a zero — so it is neither counted nor allowed to inflate the
    /// denominator every share is read against (BRD-119).
    /// </remarks>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="aRecords">The records to count.</param>
    /// <param name="aValueOf">Reads the optional field.</param>
    /// <returns>The counts, ordinally keyed so the report order is stable.</returns>
    private static SortedDictionary<string, int> CountBy<T>(IEnumerable<T> aRecords, Func<T, string?> aValueOf)
    {
        var vCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var vRecord in aRecords)
        {
            var vValue = aValueOf(vRecord);
            if (!string.IsNullOrEmpty(vValue))
            {
                vCounts[vValue] = vCounts.GetValueOrDefault(vValue) + 1;
            }
        }

        return vCounts;
    }

    /// <summary>Renders counts as distribution rows against their own denominator.</summary>
    /// <param name="aCounts">The counts from <see cref="CountBy{T}"/>.</param>
    /// <param name="aDenominator">The records that carried the field — never the record total.</param>
    /// <returns>One row per category observed, ordinally ordered.</returns>
    private static IReadOnlyList<MissCategoryCount> Rows(
        IReadOnlyDictionary<string, int> aCounts,
        int aDenominator) =>
        aCounts
            .Select(aEntry => new MissCategoryCount(
                aEntry.Key,
                aEntry.Value,
                MetricsConstants.Pct(aEntry.Value, aDenominator)))
            .ToList();

    /// <summary>The note a distribution carries when its shares cannot be read honestly.</summary>
    /// <param name="aDenominator">The distribution's own denominator.</param>
    /// <returns><c>insufficient data (n=…)</c> below the minimum, otherwise <c>null</c>.</returns>
    private static string? Note(int aDenominator) =>
        aDenominator < MetricsConstants.MinN
            ? Figure.InsufficientData(aDenominator).Display()
            : null;

    /// <summary>A percentage share, or an honest refusal.</summary>
    /// <param name="aNumerator">The numerator.</param>
    /// <param name="aDenominator">The denominator — the figure's supporting records.</param>
    /// <returns><c>NotApplicable</c> on a zero denominator, <c>InsufficientData</c> below the minimum, else the share.</returns>
    private static Figure Share(int aNumerator, int aDenominator)
    {
        if (aDenominator == 0)
        {
            return Figure.NotApplicable();
        }

        return aDenominator < MetricsConstants.MinN
            ? Figure.InsufficientData(aDenominator)
            : Figure.Value(
                100.0 * aNumerator / aDenominator,
                aDenominator,
                MetricsConstants.Pct(aNumerator, aDenominator));
    }

    /// <summary>The mean of a per-record quantity, to one decimal place.</summary>
    /// <param name="aValues">One value per record that carried the quantity; a record that did not is absent.</param>
    /// <returns><c>NotApplicable</c> when nothing carried it, <c>InsufficientData</c> below the minimum, else the mean.</returns>
    private static Figure MeanPerRecord(IReadOnlyList<double> aValues) =>
        MeanOverRecords(aValues.Sum(), aValues.Count);

    /// <summary>
    /// A mean over a stated record count, refusing below the minimum-n floor.
    /// </summary>
    /// <remarks>
    /// Takes the divisor explicitly because the token figures divide by every attributed fix record,
    /// including those carrying no token count — see the note at the call site. Passing the count in
    /// rather than deriving it from the values is what keeps the two implementations comparable
    /// key for key under BRD §13.
    /// </remarks>
    /// <param name="aTotal">The summed value.</param>
    /// <param name="aRecords">The number of records the total is spread over.</param>
    /// <returns>The mean, or a refusal that can never be read as a number.</returns>
    private static Figure MeanOverRecords(double aTotal, int aRecords)
    {
        if (aRecords == 0)
        {
            return Figure.NotApplicable();
        }

        if (aRecords < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aRecords);
        }

        var vMean = Math.Round(aTotal / aRecords, 1, MidpointRounding.ToEven);
        return Figure.Value(vMean, aRecords, vMean.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Measured dollars per record, to four decimal places.</summary>
    /// <param name="aValues">The measurements; empty for every harness that measures none.</param>
    /// <returns><c>NotApplicable</c> when nothing was measured, <c>InsufficientData</c> below the minimum, else the mean.</returns>
    private static Figure MeanUsdPerRecord(IReadOnlyList<decimal> aValues)
    {
        if (aValues.Count == 0)
        {
            return Figure.NotApplicable();
        }

        if (aValues.Count < MetricsConstants.MinN)
        {
            return Figure.InsufficientData(aValues.Count);
        }

        var vMean = Math.Round(aValues.Sum() / aValues.Count, 4, MidpointRounding.ToEven);
        return Figure.Value(
            (double)vMean,
            aValues.Count,
            vMean.ToString("0.####", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// How many misses a <c>shared:n</c> fix run repaired.
    /// </summary>
    /// <param name="aAttribution">The <c>cost_attribution</c> value.</param>
    /// <returns>The count when the value is a well-formed <c>shared:n</c> with <c>n</c> at least 1, else <c>null</c>.</returns>
    /// <summary>
    /// How many ways one fix run's token window splits, recomputed from the stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>null</c> means genuinely unattributable — there is nothing to divide, because no run
    /// matched or the window itself could not be computed. Anything with a real window IS a share,
    /// and how many ways it splits is countable from the miss_ids that run closed.
    /// </para>
    /// <para>
    /// <b>The recount wins over the stored value — including over a stored <c>sole</c>.</b> That
    /// short-circuit used to sit here, and BRD §13 caught it on 2026-09-02 against this repo's own
    /// live data. The emitter stamps attribution one record at a time, so a run that closed nine
    /// misses wrote <c>sole, shared:2 … shared:9</c>: the FIRST record reads <c>sole</c> only
    /// because at that instant it was the only miss the run had closed. Honouring it therefore
    /// preserved exactly the value the recompute exists to correct, and did so in the worst
    /// possible column — <c>sole</c> is the HEADLINE measured-cost figure, so one whole multi-miss
    /// window was reported as the measured cost of a single repair, once per multi-miss run,
    /// silently and upward. <c>sole</c> now means what it says: the run closed exactly one miss,
    /// which is a fact about the finished stream rather than about the order records were written
    /// in.
    /// </para>
    /// </remarks>
    /// <param name="aFix">The fix record.</param>
    /// <param name="aClosedPerRun">Miss ids closed by each fix run.</param>
    /// <returns>The divisor, or <c>null</c> when the record is unattributable.</returns>
    private static int? ComputedAttribution(
        MissFixRecord aFix,
        IReadOnlyDictionary<string, HashSet<string>> aClosedPerRun)
    {
        if (string.IsNullOrEmpty(aFix.TokensScope)
            || string.Equals(aFix.TokensScope, NoneAttribution, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.IsNullOrEmpty(aFix.FixRunId))
        {
            return null;
        }

        return aClosedPerRun.TryGetValue(aFix.FixRunId, out var vClosed) && vClosed.Count > 0
            ? vClosed.Count
            : 1;
    }

    private static int? SharedAcross(string? aAttribution)
    {
        if (aAttribution is null || !aAttribution.StartsWith(SharedAttributionPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(
            aAttribution[SharedAttributionPrefix.Length..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var vAcross) && vAcross >= 1
            ? vAcross
            : null;
    }

    /// <summary>Reads an ISO-8601 timestamp as an instant.</summary>
    /// <param name="aTimestamp">The stored timestamp text.</param>
    /// <returns>The instant, or <c>null</c> when the text is not a timestamp.</returns>
    private static DateTimeOffset? Instant(string? aTimestamp) =>
        DateTimeOffset.TryParse(
            aTimestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var vInstant)
            ? vInstant
            : null;
}
