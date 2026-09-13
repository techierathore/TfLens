using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The counts behind Coverage's <b>Data quality</b> section (REQ-UI-039, BRD-127 as amended 2026-09-08).
/// </summary>
/// <remarks>
/// <para>
/// This class counts stored records and nothing else. It computes no rate, no share and no grade, and it
/// derives no value — it only says <i>how many records a derivation would have to answer for</i>, which
/// is the figure BRD-179 and BRD-180 require to be stated beside every figure built on them. The
/// derivations themselves are <c>RunDuration</c> and <c>GateAttempt</c> (REQ-FN-112, REQ-FN-113) and live
/// with the engine that consumes them, not here.
/// </para>
/// <para>
/// <b>Misses are read folded.</b> Amendments are applied through <see cref="MissAmendFolder.Fold"/>
/// before a field is counted, so a <c>sort</c> supplied only by a later <c>miss-amend</c> reads as
/// carried rather than as missing — and the count of amendments that did so is published beside the
/// figures (BRD-176), because a reader who cannot tell a value that was answered from one answered later
/// is reading a distribution whose provenance is invisible.
/// </para>
/// <para>
/// <b>Predating a field and lacking it are two facts, never one.</b> The completeness rows carry both
/// <see cref="MissFieldCompleteness.MissingSort"/> and <see cref="MissFieldCompleteness.Eligible"/> so
/// the page can state them side by side. Pooling them would report a record from before the question was
/// asked as a record that declined to answer it (BRD-172).
/// </para>
/// </remarks>
public static class DataQualityFigures
{
    /// <summary>The <c>runs.cmd</c> vocabulary of SCHEMA.md §2, in the order it lists them.</summary>
    /// <remarks>
    /// The five framework-maintenance commands were added to the schema on 2026-09-07, after 27 records
    /// already carried them — which is why a value outside this list is kept and counted rather than
    /// dropped: the reader is not the thing that is wrong (BRD-181).
    /// </remarks>
    private static readonly HashSet<string> RunCommands = new(StringComparer.Ordinal)
    {
        "day1-brownfield", "day1-greenfield", "split-brd", "mockups", "build-phase", "verify-phase",
        "fix-issues", "triage-issues", "log-miss", "devguide", "productguide", "handoff-phase",
        "refresh-status", "amend-docs", "deploy-checklist", "metrics-report", "generate-html",
        "render-workflow-docs", "triage-and-fix", "framework-reset"
    };

    /// <summary>The <c>gates.verdict</c> vocabulary of SCHEMA.md §3 — the checklist's own words.</summary>
    private static readonly HashSet<string> GateVerdicts = new(StringComparer.Ordinal)
    {
        "Verified", "Needs re-verify", "FAIL", "Blocked", "Implemented", "Done (pre-existing)"
    };

    /// <summary>The <c>harness</c> vocabulary of SCHEMA.md §1; <c>codex</c> is retired but stays readable.</summary>
    private static readonly HashSet<string> Harnesses = new(StringComparer.Ordinal)
    {
        "claude-code", "opencode", "codex"
    };

    /// <summary>The requirement class the framework grades <i>itself</i> under (BRD-177, SCHEMA.md §6).</summary>
    /// <remarks>
    /// Aliased to <see cref="MetricsConstants.FrameworkReqClass"/> rather than spelled again here
    /// (REQ-FN-110): the same string decides the segment key in <see cref="Segment.KeyFor(Contracts.GateRecord)"/>,
    /// and a second copy is how the count on Coverage and the segregation on <c>/gate-outcomes</c> would
    /// eventually disagree about which records are the framework's own.
    /// </remarks>
    private const string FrameworkReqClass = MetricsConstants.FrameworkReqClass;

    /// <summary>The row that totals the field-completeness table.</summary>
    public const string AllReposRow = "All repos";

    /// <summary>
    /// Counts every data-quality fact Coverage states, from the records already stored.
    /// </summary>
    /// <param name="aMisses">Stored <c>miss</c> records for the framework being shown.</param>
    /// <param name="aAmends">Stored <c>miss-amend</c> records, folded before any field is counted.</param>
    /// <param name="aReviews">Stored <c>review</c> records — a fourth kind, never counted as misses.</param>
    /// <param name="aRuns">Stored <c>runs</c> records for the framework being shown.</param>
    /// <param name="aGates">Stored <c>gates</c> records for the framework being shown.</param>
    /// <param name="aAnalysis">The miss engine's own figures, read for its link-integrity counts.</param>
    /// <returns>The counts the Data quality band renders.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static CoverageDataQuality Compute(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissAmendRecord> aAmends,
        IReadOnlyList<MissReviewRecord> aReviews,
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyList<GateRecord> aGates,
        MissAnalysis aAnalysis)
    {
        ArgumentNullException.ThrowIfNull(aMisses);
        ArgumentNullException.ThrowIfNull(aAmends);
        ArgumentNullException.ThrowIfNull(aReviews);
        ArgumentNullException.ThrowIfNull(aRuns);
        ArgumentNullException.ThrowIfNull(aGates);
        ArgumentNullException.ThrowIfNull(aAnalysis);

        // BRD-116 — the fold is a read-time operation and happens before anything is counted, so a field
        // completed by a later `miss-amend` reads as carried. Counting the unfolded record would report a
        // field that WAS answered as one that never was.
        var vFolded = MissAmendFolder.Fold(aMisses, aAmends).Misses;

        return new CoverageDataQuality(
            BuildFieldCompleteness(vFolded),
            BuildDerived(aRuns, aGates),
            BuildUnrecognisedValues(aRuns, aGates),
            BuildDiagnostics(aReviews, aGates, aAnalysis));
    }

    /// <summary>
    /// Counts each repository's misses, and how many of them carry no <c>sort</c> and no <c>what</c>.
    /// </summary>
    /// <remarks>
    /// The eligible count comes from <see cref="LateGateCoverageCalculator.IsEligibleForField"/> so the
    /// 2026-09-07 floor is read from <see cref="MetricsConstants.FieldSince"/> — the one table, the one
    /// code path — rather than re-derived here. Both fields share that floor, so one eligible column
    /// serves both, exactly as the approved design shows it.
    /// </remarks>
    /// <param name="aMisses">Stored <c>miss</c> records, already folded.</param>
    /// <returns>One row per repository in ordinal order, then the <c>All repos</c> total.</returns>
    private static IReadOnlyList<MissFieldCompleteness> BuildFieldCompleteness(
        IReadOnlyList<MissRecord> aMisses)
    {
        if (aMisses.Count == 0)
        {
            return [];
        }

        var vRows = aMisses
            .GroupBy(aMiss => aMiss.Repo, StringComparer.OrdinalIgnoreCase)
            .OrderBy(aGroup => aGroup.Key, StringComparer.Ordinal)
            .Select(aGroup => RowFor(aGroup.Key, aGroup.ToList()))
            .ToList();

        vRows.Add(RowFor(AllReposRow, aMisses));

        return vRows;
    }

    /// <summary>Builds one field-completeness row over a set of misses.</summary>
    /// <param name="aLabel">The repository name, or the total row's label.</param>
    /// <param name="aMisses">The misses the row counts.</param>
    /// <returns>The row.</returns>
    private static MissFieldCompleteness RowFor(string aLabel, IReadOnlyList<MissRecord> aMisses) =>
        new(
            aLabel,
            aMisses.Count,
            aMisses.Count(aMiss => string.IsNullOrWhiteSpace(aMiss.Sort)),
            aMisses.Count(aMiss => string.IsNullOrWhiteSpace(aMiss.What)),
            aMisses.Count(aMiss =>
                LateGateCoverageCalculator.IsEligibleForField(MissAmendFolder.SortField, aMiss.Ts)));

    /// <summary>
    /// Counts the records a read-time derivation would have to answer for (BRD-179, BRD-180).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run is derivable when it carries no <c>duration_s</c> and does carry a <c>started</c> with an
    /// <c>ended</c> — or, per SCHEMA.md's own definition, a <c>ts</c> standing in for an absent
    /// <c>ended</c>. Every such run is answerable and none needs a guess.
    /// </para>
    /// <para>
    /// A gate is derivable when it carries no <c>attempt</c> at all: <c>attempt</c> is <i>defined</i> as
    /// one plus the prior live records for the same <c>(project, req_id)</c>, a count over the stream in
    /// order and not a judgement. A record that carries one is never altered.
    /// </para>
    /// </remarks>
    /// <param name="aRuns">Stored <c>runs</c> records.</param>
    /// <param name="aGates">Stored <c>gates</c> records.</param>
    /// <returns>One row per derived field, in the order the page prints them.</returns>
    private static IReadOnlyList<DerivedValueCount> BuildDerived(
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyList<GateRecord> aGates) =>
        [
            new DerivedValueCount(
                "duration_s",
                "ended − started, with ts standing in for an absent ended",
                aRuns.Count(IsDurationDerivable)),
            new DerivedValueCount(
                "attempt",
                "1 + prior live records for the same (project, req_id), in stream order",
                aGates.Count(aGate => aGate.Attempt is null))
        ];

    /// <summary>True when this read had to close a run's window from the record's own timestamps.</summary>
    /// <remarks>
    /// The same predicate the engine's derive pass uses (<c>RunDuration.Derive</c>), so this card and
    /// the effort page cannot disagree about the same record. Since the timestamps now win outright
    /// (BRD-179 as amended 2026-09-10), a record whose <c>started</c> and <c>ended</c> both parse is
    /// simply <i>read</i> rather than derived — deriving is what is left for a run that carries no
    /// usable duration at all, which in practice means one with no <c>ended</c> (where <c>ts</c> stands
    /// in) or one that recorded no elapsed time. Counting every timestamped record as "derived" would
    /// over-report the card by most of the stream.
    /// </remarks>
    /// <param name="aRun">The run record.</param>
    /// <returns>Whether this read worked the duration out rather than reading it.</returns>
    private static bool IsDurationDerivable(RunRecord aRun) =>
        RunDuration.UsableSeconds(aRun) is null
        && !string.IsNullOrWhiteSpace(aRun.Started)
        && (!string.IsNullOrWhiteSpace(aRun.Ended) || !string.IsNullOrWhiteSpace(aRun.Ts));

    /// <summary>
    /// Counts stored values that no vocabulary in <c>SCHEMA.md</c> lists (BRD-181).
    /// </summary>
    /// <remarks>
    /// Shown and counted, never filtered away — the totals of a reader that dropped them would look
    /// entirely normal, which is the whole hazard. A <c>null</c> is <b>not</b> unrecognised: a field that
    /// was never written is absence, and absence is reported by the completeness counts instead.
    /// </remarks>
    /// <param name="aRuns">Stored <c>runs</c> records.</param>
    /// <param name="aGates">Stored <c>gates</c> records.</param>
    /// <returns>One row per (field, value) pair, heaviest first.</returns>
    private static IReadOnlyList<UnrecognisedValueFact> BuildUnrecognisedValues(
        IReadOnlyList<RunRecord> aRuns,
        IReadOnlyList<GateRecord> aGates)
    {
        var vObserved = new List<(string Where, string? Value, HashSet<string> Vocabulary)>();

        vObserved.AddRange(aRuns.Select(aRun => ("runs.cmd", aRun.Cmd, RunCommands)));
        vObserved.AddRange(aRuns.Select(aRun => ("runs.harness", aRun.Harness, Harnesses)));
        vObserved.AddRange(aGates.Select(aGate => ("gates.verdict", aGate.Verdict, GateVerdicts)));
        vObserved.AddRange(aGates.Select(aGate => ("gates.harness", aGate.Harness, Harnesses)));

        return vObserved
            .Where(aItem => !string.IsNullOrWhiteSpace(aItem.Value)
                && !aItem.Vocabulary.Contains(aItem.Value!))
            .GroupBy(aItem => (aItem.Where, Value: aItem.Value!))
            .Select(aGroup => new UnrecognisedValueFact(aGroup.Key.Where, aGroup.Key.Value, aGroup.Count()))
            .OrderByDescending(aFact => aFact.Records)
            .ThenBy(aFact => aFact.Where, StringComparer.Ordinal)
            .ThenBy(aFact => aFact.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Reads the miss stream's link-integrity counts and the framework's own verdict count.
    /// </summary>
    /// <remarks>
    /// The link-integrity counts are read from the miss engine rather than recomputed, so Coverage cannot
    /// report a different number from <c>/misses</c> — only a different <i>selection</i> of numbers. The
    /// review count is stated here precisely because it is <b>not</b> a miss count: a record about a
    /// phase's output sits beside the miss figures and never inside them (BRD-174).
    /// </remarks>
    /// <param name="aReviews">Stored <c>review</c> records.</param>
    /// <param name="aGates">Stored <c>gates</c> records.</param>
    /// <param name="aAnalysis">The miss engine's figures.</param>
    /// <returns>The diagnostics block.</returns>
    private static MissStreamDiagnostics BuildDiagnostics(
        IReadOnlyList<MissReviewRecord> aReviews,
        IReadOnlyList<GateRecord> aGates,
        MissAnalysis aAnalysis) =>
        new(
            aAnalysis.EscapesMissingWhy,
            aAnalysis.OrphanFixes,
            aAnalysis.OrphanAmends,
            aAnalysis.AmendmentsIgnored,
            aAnalysis.AmendmentsApplied,
            aAnalysis.BackfilledMissesExcluded,
            aAnalysis.BackfilledMissFixesExcluded,
            aReviews.Count,
            aGates.Count(aGate =>
                string.Equals(aGate.ReqClass, FrameworkReqClass, StringComparison.Ordinal)));
}
