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

            vSegments[vBucket.Key] = FiguresFor(vBucket.Value, vFixesHere, vRunsHere, vFixesByMiss, vKnownMisses);
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
    /// <returns>The segment's figures.</returns>
    private static MissSegmentFigures FiguresFor(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes,
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyDictionary<string, List<MissFixRecord>> aFixesByMiss,
        IReadOnlySet<string> aKnownMisses)
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
            Cost = MoneyFor(aFixes)
        };
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

        return new MissAttributionFigures
        {
            AttributedN = vSet.AttributedN,
            AttributionExcluded = vSet.AttributionExcluded,
            ExclusionReason = vSet.Reason,
            ExcludedByConfidence = vSet.ExcludedByConfidence,
            ByOriginPhase = Rows(vPhaseCounts, vPhaseN),
            ByOriginModel = Rows(vModelCounts, vModelCounts.Sum(aEntry => aEntry.Value)),
            ByOriginAgent = Rows(vAgentCounts, vAgentCounts.Sum(aEntry => aEntry.Value)),
            MissRatePerOriginPhase = vRates
        };
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

        // KNOWN DIVERGENCE FROM THE REFERENCE — deliberate, recorded as TF-005 and DECISIONS.md D-012.
        // `analyse_misses` in tf-metrics.sh computes `sum(tokens_out or 0) / len(sole)`, so a repair
        // whose tokens were never recorded is averaged in as a zero and drags the mean down. TfLens
        // divides by the records that actually carry a count, because presenting an unmeasured repair
        // as a costless one is the exact failure this product exists to prevent (BRD-31..36: absent
        // renders as an absence, never as 0). The two agree on every dataset where every `sole`
        // record carries `tokens_out`, which is every dataset seen so far — the divergence is latent,
        // not live, and BRD §13 currently passes. Do NOT "fix" this by matching the reference without
        // reading TF-005 first; parity would go green by adopting the weaker number.
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
            RecoveredRecords = vRecovered,
            AttributionMissing = 0,
            ByHarness = ExtraMetrics.HarnessOrder.Select(aHarness => HarnessRow(aHarness, aFixes)).ToList()
        };
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
    /// <c>null</c> means genuinely unattributable — there is nothing to divide, because no run
    /// matched or the window itself could not be computed. Anything with a real window IS a share,
    /// and how many ways it splits is countable from the miss_ids that run closed. A stored
    /// <c>sole</c> is honoured as written: it is the one value a single record can state correctly
    /// about itself.
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

        if (string.Equals(aFix.CostAttribution, SoleAttribution, StringComparison.Ordinal))
        {
            return 1;
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
