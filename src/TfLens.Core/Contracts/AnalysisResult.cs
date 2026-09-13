namespace TfLens.Core.Contracts;

/// <summary>
/// The whole output of the metrics engine for one user and one framework.
/// </summary>
/// <remarks>
/// ADR-007 — this type has no member that could hold a cross-<c>project_type</c> or cross-provenance
/// rate. <see cref="Live"/> and <see cref="Backfilled"/> are keyed by project type and never merged;
/// <see cref="Pooled"/> holds only the metrics the reference explicitly exempts from both separations
/// (run counts, cadence, tokens), and its <see cref="PooledMetrics.CostUsd"/> is always <c>null</c>.
/// The key layout mirrors <c>tf-metrics.sh --rollup --json</c> so the parity compare reads key-for-key.
/// </remarks>
public sealed record AnalysisResult
{
    /// <summary>The user the figures belong to; every read is scoped by it (ADR-013).</summary>
    public required int UserId { get; init; }

    /// <summary>The framework axis the figures belong to — never pooled across (ADR-016).</summary>
    public required string Framework { get; init; }

    /// <summary>One line per connected repository the figures were computed from.</summary>
    public required IReadOnlyList<PerRepoFacts> PerRepo { get; init; }

    /// <summary>
    /// Requirements with any backfilled gate record, excluded from the live first-pass rate, spelled
    /// <c>app:req_id</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Their live <c>attempt</c> numbering restarts at 1 (SCHEMA.md §3.1), so a live first-pass rate
    /// that included them would be flattering and wrong. The list is shown on screen rather than
    /// silently applied.
    /// </para>
    /// <para>
    /// <b>Each entry names its project (REQ-FN-111, BRD-178).</b> The set is keyed on
    /// <c>(project, req_id)</c>, so one project's backfilled <c>REQ-UI-001</c> no longer excludes every
    /// other project's live one — and the rendering says which project's requirement was dropped, which
    /// a bare id cannot.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<string> TaintedReqs { get; init; }

    /// <summary>Live figures, keyed by project type. Never merged with <see cref="Backfilled"/>.</summary>
    public required IReadOnlyDictionary<string, SegmentFigures> Live { get; init; }

    /// <summary>Backfilled figures, keyed by project type. Never merged with <see cref="Live"/>.</summary>
    public required IReadOnlyDictionary<string, SegmentFigures> Backfilled { get; init; }

    /// <summary>The metrics the reference exempts from both separations.</summary>
    public required PooledMetrics Pooled { get; init; }

    /// <summary>
    /// The miss and rework block — live-only, segmented by project type (REQ-FN-077, BRD-118..BRD-123).
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="MissAnalysis.Empty"/> so a repository that emits no <c>misses.jsonl</c>
    /// reports zero rather than absent. Nothing in this block is ever merged into the gate-derived
    /// figures above: the miss escape share sits <b>beside</b> <see cref="SegmentFigures.EscapeRate"/>
    /// and never inside it (REQ-NFR-013 clause 6).
    /// </remarks>
    public MissAnalysis Misses { get; init; } = MissAnalysis.Empty;

    /// <summary>
    /// The TechieFlow phase-effort block — live-only, grouped by <c>cmd</c>
    /// (REQ-FN-089..REQ-FN-093, BRD-146..BRD-152).
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="PhaseEffortAnalysis.Empty"/> so a framework with no live run reports zero
    /// rather than absent. Every figure inside it arrives wrapped in the count it rests on — a token total
    /// as a <see cref="TokenWindow"/>, a spawn count as a <see cref="FanoutObservation"/> — so a page
    /// binding this block cannot render a number without also holding its denominator (ADR-026).
    /// </remarks>
    public PhaseEffortAnalysis Phases { get; init; } = PhaseEffortAnalysis.Empty;

    /// <summary>
    /// Runs a <c>run-void</c> record took out of every figure (SCHEMA.md §2.7, REQ-FN-135).
    /// </summary>
    /// <remarks>
    /// The stream is append-only, so a run recorded with a wrong figure is corrected by a second record
    /// naming it rather than by an edit. Both records stay; this is the count of what stopped being
    /// counted, published because a total offered without its exclusions is just a different wrong
    /// number.
    /// </remarks>
    public int RunsVoidedN { get; init; }

    /// <summary>One line per voided run — which run, and the reason the void gave for it.</summary>
    /// <remarks>
    /// The reason is the protection: it is on the record and a reader can check it, which is what stops
    /// a void being used to remove a run whose figures are merely unflattering.
    /// </remarks>
    public IReadOnlyList<string> RunsVoided { get; init; } = [];

    /// <summary>Voids naming a run this stream does not hold; counted and shown, never dropped.</summary>
    public int RunVoidsOrphanedN { get; init; }

    /// <summary>Parser version stamped into every export, so a figure can be traced to the code that made it.</summary>
    public required string ParserVersion { get; init; }

    /// <summary>
    /// Every project type present across <see cref="Live"/> and <see cref="Backfilled"/>, sorted.
    /// </summary>
    /// <remarks>The Three-questions page renders one tab per entry, and deliberately has no "all" tab.</remarks>
    public IReadOnlyList<string> ProjectTypes =>
        Live.Keys.Concat(Backfilled.Keys).Distinct().OrderBy(aK => aK, StringComparer.Ordinal).ToList();
}

/// <summary>Per-repository facts shown on the Coverage page and carried into the export.</summary>
/// <param name="Repo"><c>owner/name</c> of the repository.</param>
/// <param name="App">Application name the records carry.</param>
/// <param name="ProjectType">The repository's declared project type.</param>
/// <param name="Framework">The provenance axis the repository belongs to.</param>
/// <param name="Gates">Gate records stored.</param>
/// <param name="GatesBackfilled">How many of those are backfilled.</param>
/// <param name="Runs">Run records stored.</param>
/// <param name="Sessions">Session records stored, after dedupe.</param>
/// <param name="Commits">Commit records stored, after dedupe.</param>
/// <param name="Events">Playbook event records stored.</param>
public sealed record PerRepoFacts(
    string Repo,
    string? App,
    string? ProjectType,
    string Framework,
    int Gates,
    int GatesBackfilled,
    int Runs,
    int Sessions,
    int Commits,
    int Events);

/// <summary>
/// The figures for one (provenance, project type) segment — the only shape a rate is ever computed in.
/// </summary>
/// <remarks>
/// <b>Segments never merge, and <c>req_class: "FR"</c> is one of them (REQ-FN-110, BRD-177).</b>
/// Verdicts on the framework's own requirement lines carry the segment key
/// <see cref="MetricsConstants.FrameworkRequirements"/> and therefore reach no application segment's
/// figures at all — a framework rule and an application screen are not the same unit, and this type has
/// no shape in which one figure could span both.
/// </remarks>
public sealed record SegmentFigures
{
    /// <summary>Gate records in this segment.</summary>
    public required int Records { get; init; }

    /// <summary>Distinct requirements eligible to be scored in this segment, keyed <c>(project, req_id)</c>.</summary>
    /// <remarks>
    /// REQ-FN-111 / BRD-178 — two projects each carrying a <c>REQ-UI-001</c> count as <b>two</b>
    /// requirements here, not one, in this denominator and in every set behind it.
    /// </remarks>
    public required int ReqsScored { get; init; }

    /// <summary>REQs dropped from the live segment because they carry a backfilled record; zero on backfilled segments.</summary>
    public required int ReqsExcludedBackfillTaint { get; init; }

    /// <summary>
    /// Records in this segment whose <c>attempt</c> this read derived (REQ-FN-113, BRD-180).
    /// </summary>
    /// <remarks>
    /// <b>Stated beside the rate, never folded into it.</b> <c>attempt</c> is <i>defined</i> as one plus
    /// the prior live records for the same <c>(project, req_id)</c>, so deriving it is a count over the
    /// stream rather than a judgement — but a first-pass rate resting partly on derived attempts is a
    /// different claim from one read straight off the stream, and BRD-180 requires the reader to be told
    /// which they are looking at. Zero on a backfilled segment: a backfilled record's attempt is assumed
    /// rather than observed, so nothing derives one for it.
    /// </remarks>
    public required int AttemptsDerived { get; init; }

    /// <summary>Distinct REQs that passed on their first attempt.</summary>
    public required int FirstPassN { get; init; }

    /// <summary>First-pass rate — first-attempt <c>Verified</c> REQs over eligible REQs.</summary>
    public required Figure FirstPassRate { get; init; }

    /// <summary>Failure counts per gate, in the reference's gate order, omitting gates with no failures.</summary>
    public required IReadOnlyList<GateCount> GateDistribution { get; init; }

    /// <summary>Failure records the distribution is computed over — the honest denominator for its shares.</summary>
    public required int GateDistributionN { get; init; }

    /// <summary><c>insufficient data (n=…)</c> when there are too few failures to read shares off; otherwise <c>null</c>.</summary>
    public required string? GateDistributionNote { get; init; }

    /// <summary>For each late-added gate, how many records ran it beside how many it caught.</summary>
    public required IReadOnlyList<LateGateCoverage> LateGateCoverage { get; init; }

    /// <summary>Escape rate — REQs no gate caught, over REQs with any failure.</summary>
    public required Figure EscapeRate { get; init; }
}

/// <summary>One row of the gate catch distribution.</summary>
/// <param name="Gate">Gate name, or <c>unattributed</c> when the failure named none.</param>
/// <param name="Count">Failures attributed to it.</param>
/// <param name="Share">Its share of <see cref="SegmentFigures.GateDistributionN"/>, as the reference prints it.</param>
public sealed record GateCount(string Gate, int Count, string Share);

/// <summary>Coverage of a gate that entered the enum after collection started.</summary>
/// <param name="Gate">The gate name.</param>
/// <param name="Ran">Records whose <c>gates_run</c> includes it — the honest denominator.</param>
/// <param name="Caught">Failures it caught.</param>
/// <param name="Since">The date the gate was added.</param>
/// <param name="CatchRate">Caught over ran, or <c>insufficient data</c> when too few records ran it.</param>
public sealed record LateGateCoverage(string Gate, int Ran, int Caught, string Since, Figure CatchRate);

/// <summary>
/// What an optional field's denominator is allowed to be, once the records that predate the field are
/// taken out of it (REQ-FN-076, BRD-117).
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="LateGateCoverage"/>, and the same rule: a mid-stream addition is read
/// against what could have been observed, never against the total. <see cref="Assessed"/> over
/// <see cref="Eligible"/> is the <c>n of N assessed</c> a distribution must print on its face;
/// <see cref="PredatesField"/> is what the export reports as <c>why_missed_predates_field</c>, and
/// <see cref="Eligible"/> as <c>why_missed_eligible</c>.
/// </para>
/// <para>
/// The type carries no rate. Dividing <see cref="Assessed"/> by the record total is exactly the
/// plausible wrong number this rule exists to prevent, so the denominator that would produce it is not
/// a property here.
/// </para>
/// </remarks>
/// <param name="Field">The wire field name, e.g. <c>why_missed</c>.</param>
/// <param name="Since">The date the field entered the stream, or <c>null</c> when it has no floor.</param>
/// <param name="Eligible">Records written on or after <paramref name="Since"/> — the denominator.</param>
/// <param name="PredatesField">Records written before it; excluded, and reported rather than dropped.</param>
/// <param name="Assessed">Eligible records that actually carry the field — the numerator.</param>
public sealed record FieldEligibility(
    string Field,
    string? Since,
    int Eligible,
    int PredatesField,
    int Assessed)
{
    /// <summary>
    /// The <c>n of N</c> a distribution over this field must print on its face (BRD-172).
    /// </summary>
    /// <remarks>
    /// The denominator is <see cref="Eligible"/> and never the record total, so the phrase cannot be
    /// built out of the wrong one by a caller. <see cref="PredatesField"/> is deliberately <b>not</b>
    /// in this sentence: it is a separate fact, stated in its own words beside it, and folding the two
    /// into one phrase is exactly the pooling the rule forbids.
    /// </remarks>
    /// <param name="aParticiple">What the assessed records did, e.g. <c>sorted</c> or <c>assessed</c>.</param>
    /// <returns>The phrase, e.g. <c>73 of 126 sorted</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aParticiple"/> is <c>null</c>.</exception>
    public string NOfN(string aParticiple)
    {
        ArgumentNullException.ThrowIfNull(aParticiple);

        return $"{Assessed} of {Eligible} {aParticiple}";
    }

    /// <summary>
    /// The floor for a field over no records at all — the shape an empty block reports.
    /// </summary>
    /// <remarks>
    /// It carries the field's real <c>since</c> date from <see cref="MetricsConstants.FieldSince"/>, so
    /// an empty segment still says <i>when</i> the field arrived rather than implying it always existed.
    /// </remarks>
    /// <param name="aField">The wire field name, e.g. <c>sort</c>.</param>
    /// <returns>An all-zero eligibility for that field.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aField"/> is <c>null</c>.</exception>
    public static FieldEligibility NoneFor(string aField)
    {
        ArgumentNullException.ThrowIfNull(aField);

        return new FieldEligibility(aField, MetricsConstants.FieldSince.GetValueOrDefault(aField), 0, 0, 0);
    }
}

/// <summary>
/// The metrics the reference exempts from the provenance separations.
/// </summary>
/// <remarks>
/// These count events (runs, commits, tokens) rather than scoring requirements, so pooling them does
/// not manufacture a misleading rate. <see cref="CostUsd"/> is always <c>null</c>: estimated dollars
/// are never presented as a measurement (SCHEMA.md §4, BRD-35).
/// </remarks>
public sealed record PooledMetrics
{
    /// <summary>All run records.</summary>
    public required int RunsTotal { get; init; }

    /// <summary>Run counts per command, sorted by command name.</summary>
    public required IReadOnlyList<KeyValuePair<string, int>> RunsByCmd { get; init; }

    /// <summary>Fix-mode runs over build-phase runs — how much of the work is rework.</summary>
    public required Figure ReworkRatio { get; init; }

    /// <summary>Median REQs per hour across runs that carry both a duration and a REQ count.</summary>
    public required Figure ThroughputMedianReqsPerHour { get; init; }

    /// <summary>Median REQ count of a build-phase run.</summary>
    public required Figure BatchSizeMedian { get; init; }

    /// <summary>Session records after dedupe.</summary>
    public required int Sessions { get; init; }

    /// <summary>Input plus output tokens across every session.</summary>
    public required long TokensTotal { get; init; }

    /// <summary>Tokens per <c>Verified</c> verdict, to one decimal place.</summary>
    public required Figure TokensPerVerifiedReq { get; init; }

    /// <summary>
    /// What every run's measured tokens would cost at the published rate — a price, never a bill
    /// (SCHEMA.md §2.5b, REQ-FN-136).
    /// </summary>
    /// <remarks>
    /// Pooled across harnesses deliberately, and legitimately: unlike <c>cost_usd</c>, a list price is
    /// the same question asked of every run — what would these tokens cost, bought at the published
    /// rate — so it is the one money figure that may be totalled over a mixed estate.
    /// </remarks>
    public decimal ListUsdTotal { get; init; }

    /// <summary>Runs that produced a list price; the total's own denominator.</summary>
    public int ListUsdRecords { get; init; }

    /// <summary>
    /// List price per Verified verdict, or <c>null</c> below the minimum-n floor.
    /// </summary>
    /// <remarks>
    /// <c>null</c> rather than a number when either side of the ratio is too thin to divide — the
    /// reference refuses it on the same terms, and printing a figure it refuses is a parity mismatch in
    /// exactly the same way as refusing one it prints.
    /// </remarks>
    public decimal? ListUsdPerVerifiedReq { get; init; }

    /// <summary>
    /// Always <c>null</c>. Measured dollars exist only per-harness for OpenCode and are never totalled.
    /// </summary>
    /// <remarks>
    /// The property exists so the export's key layout matches <c>--rollup --json</c> exactly; there is
    /// no code path that assigns it a value.
    /// </remarks>
    public decimal? CostUsd => null;

    /// <summary>Commit records after dedupe.</summary>
    public required int Commits { get; init; }

    /// <summary>Duplicate commit records collapsed on <c>sha</c> — expected, not corruption.</summary>
    public required int CommitDuplicatesCollapsed { get; init; }

    /// <summary>
    /// Duplicate session records collapsed on <c>session_id</c> — expected, not corruption.
    /// </summary>
    /// <remarks>
    /// The sibling above is computed here, at read time, because commits are deduped here. Sessions are
    /// not: the store's <c>UcSessionUserRepoId</c> index collapses them on the way in, so by the time
    /// this block is computed the duplicates no longer exist and a read-time count would always be zero.
    /// The figure is therefore carried in from <c>"SyncState"</c>, where ingest recorded it
    /// (REQ-FN-063), and summed over the repositories on this framework only — the same scoping every
    /// other pooled figure uses.
    /// </remarks>
    public required int SessionDuplicatesCollapsed { get; init; }

    /// <summary>Distinct days that carry at least one commit.</summary>
    public required int ActiveDays { get; init; }

    /// <summary>Commits per active day, to two decimal places.</summary>
    public required Figure CommitsPerActiveDay { get; init; }
}

/// <summary>
/// The whole miss and rework block for one user and one framework (REQ-FN-077, BRD-118..BRD-123).
/// </summary>
/// <remarks>
/// <para>
/// <b>Live only.</b> A backfilled miss is counted here — as
/// <see cref="BackfilledMissesExcluded"/> — and reaches no figure, the same separation the gate figures
/// draw between <see cref="AnalysisResult.Live"/> and <see cref="AnalysisResult.Backfilled"/>. Counts
/// total across segments because counting events is not scoring requirements; <b>no rate does</b>, which
/// is why every rate lives inside <see cref="Live"/> and there is no "all types" entry.
/// </para>
/// <para>
/// Amendments are folded at read time before any figure here is computed, so a <c>why_missed</c> supplied
/// only by an amendment reaches the failed-practice distribution (REQ-FN-075, ADR-020).
/// </para>
/// </remarks>
public sealed record MissAnalysis
{
    /// <summary>Live <c>miss</c> records — parity key <c>misses_total</c>.</summary>
    public required int MissesTotal { get; init; }

    /// <summary>Live <c>miss-fix</c> records — parity key <c>miss_fixes_total</c>.</summary>
    public required int MissFixesTotal { get; init; }

    /// <summary>Fix records whose <c>miss_id</c> names no stored miss — parity key <c>orphan_fixes</c>.</summary>
    /// <remarks>Counted and surfaced, never dropped: a dropped orphan is a fact nobody can see.</remarks>
    public required int OrphanFixes { get; init; }

    /// <summary>
    /// Misses whose latest <c>VerdictAfter</c> is outside <c>{Verified, wont-fix}</c>, plus misses no fix
    /// has touched — parity key <c>open_misses</c>.
    /// </summary>
    /// <remarks>
    /// <c>deferred</c> is outstanding work and stays open. <see cref="WontFix"/> is <b>never</b> folded in
    /// here, and this predicate is deliberately <b>not</b> reconciled with the producer's collapse check —
    /// they ask different questions and agreeing would break one of them (BRD-120, REQ-NFR-013 clause 4).
    /// </remarks>
    public required int OpenMisses { get; init; }

    /// <summary>Misses whose latest <c>VerdictAfter</c> is <c>wont-fix</c> — parity key <c>wont_fix</c>.</summary>
    /// <remarks>A decision, not a backlog item; its own figure, never part of <see cref="OpenMisses"/>.</remarks>
    public required int WontFix { get; init; }

    /// <summary>Misses whose latest <c>VerdictAfter</c> is <c>Verified</c> — parity key <c>resolved_misses</c>.</summary>
    public required int ResolvedMisses { get; init; }

    /// <summary>
    /// Escapes that arrived with no <c>why_missed</c> — parity key <c>escapes_missing_why</c>.
    /// </summary>
    /// <remarks>
    /// A <b>data-quality</b> figure, not a quality one: it counts the most valuable records in the stream
    /// arriving incomplete. It belongs on Coverage rather than on the KPI row.
    /// </remarks>
    public required int EscapesMissingWhy { get; init; }

    /// <summary>Amendments that filled a <c>null</c> — parity key <c>amendments_applied</c>.</summary>
    public required int AmendmentsApplied { get; init; }

    /// <summary>Well-formed amendments that arrived at a field already carrying a value.</summary>
    public required int AmendmentsIgnored { get; init; }

    /// <summary>Amendments that could never be applied — parity key <c>orphan_amends</c>.</summary>
    public required int OrphanAmends { get; init; }

    /// <summary>Backfilled misses held out of every figure here, reported rather than dropped.</summary>
    public required int BackfilledMissesExcluded { get; init; }

    /// <summary>Backfilled fix records held out of every figure here, reported rather than dropped.</summary>
    public required int BackfilledMissFixesExcluded { get; init; }

    /// <summary>Live miss figures, keyed by project type. There is deliberately no "all types" entry.</summary>
    public required IReadOnlyDictionary<string, MissSegmentFigures> Live { get; init; }

    /// <summary>Every project type the live misses fall into, sorted.</summary>
    public IReadOnlyList<string> ProjectTypes =>
        Live.Keys.OrderBy(aK => aK, StringComparer.Ordinal).ToList();

    /// <summary>What a framework with no miss stream reports — zeros, never absence.</summary>
    public static MissAnalysis Empty { get; } = new()
    {
        MissesTotal = 0,
        MissFixesTotal = 0,
        OrphanFixes = 0,
        OpenMisses = 0,
        WontFix = 0,
        ResolvedMisses = 0,
        EscapesMissingWhy = 0,
        AmendmentsApplied = 0,
        AmendmentsIgnored = 0,
        OrphanAmends = 0,
        BackfilledMissesExcluded = 0,
        BackfilledMissFixesExcluded = 0,
        Live = new Dictionary<string, MissSegmentFigures>(StringComparer.Ordinal)
    };
}

/// <summary>
/// The miss figures for one project type — the only shape a miss rate is ever computed in (REQ-FN-077).
/// </summary>
public sealed record MissSegmentFigures
{
    /// <summary>Live misses in this segment.</summary>
    public required int Misses { get; init; }

    /// <summary>Live fix records attributed to this segment.</summary>
    public required int MissFixes { get; init; }

    /// <summary>Fix records here whose <c>miss_id</c> names no stored miss.</summary>
    public required int OrphanFixes { get; init; }

    /// <summary>Open misses in this segment; <c>deferred</c> stays open, <c>wont-fix</c> never enters.</summary>
    public required int OpenMisses { get; init; }

    /// <summary>Deliberately declined misses; its own figure, never folded into <see cref="OpenMisses"/>.</summary>
    public required int WontFix { get; init; }

    /// <summary>Misses whose latest verdict is <c>Verified</c>.</summary>
    public required int ResolvedMisses { get; init; }

    /// <summary>What was missed — parity key <c>class_distribution</c>, ordinally keyed.</summary>
    public required IReadOnlyList<MissCategoryCount> ClassDistribution { get; init; }

    /// <summary>Misses carrying a <c>miss_class</c> — the honest denominator for those shares.</summary>
    public required int ClassDistributionN { get; init; }

    /// <summary><c>insufficient data (n=…)</c> when the class shares cannot be read honestly; else <c>null</c>.</summary>
    public required string? ClassDistributionNote { get; init; }

    /// <summary>Misses carrying no <c>miss_class</c> at all; not assessed, never a bucket.</summary>
    public required int ClassNotRecorded { get; init; }

    /// <summary>
    /// Which <i>practice</i> failed — parity key <c>why_missed</c> (BRD-119).
    /// </summary>
    /// <remarks>
    /// The denominator is <see cref="WhyMissedN"/> — records that carry the field — and never the miss
    /// count. A distribution rendered over all misses understates every category, which is the plausible
    /// wrong number this whole shape exists to refuse (REQ-NFR-013 clause 3).
    /// </remarks>
    public required IReadOnlyList<MissCategoryCount> FailedPracticeDistribution { get; init; }

    /// <summary>Records carrying <c>why_missed</c> — parity key <c>why_missed_n</c>, and the denominator.</summary>
    public required int WhyMissedN { get; init; }

    /// <summary>
    /// The eligibility floor behind <see cref="WhyMissedN"/> — parity keys <c>why_missed_eligible</c> and
    /// <c>why_missed_predates_field</c> (REQ-FN-076).
    /// </summary>
    public required FieldEligibility WhyMissedEligibility { get; init; }

    /// <summary><c>insufficient data (n=…)</c> when too few records carry the field; else <c>null</c>.</summary>
    public required string? FailedPracticeNote { get; init; }

    /// <summary>Who found each miss — parity key <c>found_by</c>, ordinally keyed.</summary>
    public required IReadOnlyList<MissCategoryCount> FoundBy { get; init; }

    /// <summary>Misses carrying no <c>found_by</c>; not assessed, never a bucket.</summary>
    public required int FoundByNotRecorded { get; init; }

    /// <summary><c>miss_class == "unspecified-gap"</c> over every miss — parity key <c>design_miss_share</c>.</summary>
    public required Figure DesignMissShare { get; init; }

    /// <summary>
    /// <c>found_by ∈ {owner, production}</c> over every miss — parity key <c>escape_share</c>.
    /// </summary>
    /// <remarks>
    /// <b>A second, adjacent figure.</b> It is rendered beside <see cref="SegmentFigures.EscapeRate"/> and
    /// never merged into it: that one is REQs no gate caught over REQs with any failure, computed from the
    /// <c>gates</c> stream, and it keeps its definition and its source untouched (BRD-118,
    /// REQ-NFR-013 clause 6).
    /// </remarks>
    public required Figure EscapeShare { get; init; }

    /// <summary>
    /// Median hours from a miss to the fix that verified it, to two decimal places.
    /// </summary>
    /// <remarks>
    /// Computed over misses whose latest verdict is <c>Verified</c> only. A <c>wont-fix</c> is a decision
    /// rather than a close, and folding it in here would report a declined defect as a repaired one.
    /// </remarks>
    public required Figure MedianTimeToCloseHours { get; init; }

    /// <summary>The <c>linked</c>-only per-origin figures, and the exclusion that produced them.</summary>
    public required MissAttributionFigures Attribution { get; init; }

    /// <summary>The rework token and cost figures, carrying the attribution split by construction.</summary>
    public required MissMoney Cost { get; init; }

    /// <summary>
    /// <b>Whose gap it was</b> — parity key <c>sort</c>, ordinally keyed (BRD-170, BRD-172).
    /// </summary>
    /// <remarks>
    /// Every stored value gets a row, including one outside the four
    /// <see cref="MissSorts.All"/> names: such a row carries
    /// <see cref="MissCategoryCount.IsRecognised"/> <c>false</c> and is <b>never</b> merged into the
    /// nearest legal category. The denominator is <see cref="SortN"/> — records that carry the field —
    /// read against <see cref="SortEligibility"/>, and never the miss count.
    /// </remarks>
    public IReadOnlyList<MissCategoryCount> SortDistribution { get; init; } = [];

    /// <summary>Misses carrying a <c>sort</c> — parity key <c>sort_n</c>, and the numerator.</summary>
    public int SortN { get; init; }

    /// <summary>
    /// The eligibility floor behind <see cref="SortN"/> — parity keys <c>sort_eligible</c> and
    /// <c>sort_predates_field</c> (REQ-FN-076, BRD-117, BRD-172).
    /// </summary>
    /// <remarks>
    /// <see cref="FieldEligibility.Eligible"/> is the denominator every <c>sort</c> figure is read
    /// against and <see cref="FieldEligibility.PredatesField"/> is stated <b>apart</b>, in those words.
    /// The two are never pooled: a record from before the question was asked is not a record that
    /// declined to answer it, and no <c>sort</c> figure is ever a percentage of all misses.
    /// </remarks>
    public FieldEligibility SortEligibility { get; init; } = FieldEligibility.NoneFor(MissSorts.Field);

    /// <summary>Stored <c>sort</c> values outside the four — counted and surfaced, never coerced.</summary>
    public int SortUnrecognised { get; init; }

    /// <summary><c>insufficient data (n=…)</c> when too few records carry a <c>sort</c>; else <c>null</c>.</summary>
    public string? SortDistributionNote { get; init; }

    /// <summary>
    /// How many values in each distribution above were completed by a <c>miss-amend</c> (BRD-176).
    /// </summary>
    /// <remarks>
    /// Keyed by wire field name — <c>miss_class</c>, <c>why_missed</c>, <c>sort</c>, <c>found_by</c> —
    /// so every distribution computed over folded records has a count of its own beside it. A field no
    /// amendment may complete reports <c>0</c> rather than being absent: "no amendment touched this" is
    /// an answer, and a missing key is not. A reader who folds amendments silently sees nothing false
    /// but cannot tell a field that was answered from one that was answered later.
    /// </remarks>
    public IReadOnlyDictionary<string, int> AmendedValues { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

/// <summary>One row of a miss distribution — the sibling of <see cref="GateCount"/>.</summary>
/// <remarks>
/// <see cref="IsRecognised"/> exists so a value outside a closed vocabulary can be <b>shown as its own
/// row, in its own words</b> rather than coerced into the nearest legal category or filtered away
/// (BRD-170, BRD-181). It defaults to <c>true</c> because most distributions here are over open
/// vocabularies, where there is nothing for a value to be outside of.
/// </remarks>
/// <param name="Key">The category, e.g. a <c>miss_class</c> or a <c>why_missed</c> value.</param>
/// <param name="Count">Records in it.</param>
/// <param name="Share">Its share of the distribution's own denominator, as the reference prints it.</param>
/// <param name="IsRecognised">Whether the key is inside the field's closed vocabulary.</param>
public sealed record MissCategoryCount(string Key, int Count, string Share, bool IsRecognised = true);

/// <summary>
/// The per-origin figures, computed from <c>linked</c> records only (REQ-FN-078, BRD-121).
/// </summary>
/// <remarks>
/// The exclusion is returned here, not merely rendered by a page: <see cref="AttributionExcluded"/> and
/// <see cref="ExclusionReason"/> leave the engine as data. An exclusion the reader cannot see is
/// indistinguishable from a bug.
/// </remarks>
public sealed record MissAttributionFigures
{
    /// <summary>Records every figure here was computed from — parity key <c>attributed_n</c>.</summary>
    public required int AttributedN { get; init; }

    /// <summary>Records held out of every figure here — parity key <c>attribution_excluded</c>.</summary>
    public required int AttributionExcluded { get; init; }

    /// <summary>Why they were held out — <c>MissAttributionTaint.ExclusionReason</c>.</summary>
    public required string ExclusionReason { get; init; }

    /// <summary>The excluded records broken down by their <c>origin_confidence</c> value.</summary>
    public required IReadOnlyList<MissAttributionExclusion> ExcludedByConfidence { get; init; }

    /// <summary>Misses by the phase that should have produced the artifact — parity key <c>by_origin_phase</c>.</summary>
    public required IReadOnlyList<MissCategoryCount> ByOriginPhase { get; init; }

    /// <summary>Misses by originating model — parity key <c>by_origin_model</c>. Which model to route to.</summary>
    /// <remarks>Observational: miss counts per model are confounded by which model gets the hard work.</remarks>
    public required IReadOnlyList<MissCategoryCount> ByOriginModel { get; init; }

    /// <summary>
    /// Misses by originating agent persona — parity key <c>by_origin_agent</c>. Which instructions to tighten.
    /// </summary>
    /// <remarks>
    /// Computed alongside <see cref="ByOriginModel"/> and under the same <c>linked</c>-only constraint,
    /// because the two answer different questions and neither substitutes for the other (BRD §0.6).
    /// </remarks>
    public required IReadOnlyList<MissCategoryCount> ByOriginAgent { get; init; }

    /// <summary>Misses per run of the originating phase, one row per phase observed.</summary>
    public required IReadOnlyList<MissPhaseRate> MissRatePerOriginPhase { get; init; }

    /// <summary>
    /// Misses per 100 runs of the originating model, one row per model in <see cref="ByOriginModel"/>
    /// (REQ-UI-038, BRD-121, BRD-124).
    /// </summary>
    /// <remarks>
    /// Computed from the same <c>linked</c> records as <see cref="ByOriginModel"/>, so a guessed
    /// attribution cannot reach it. Observational only: miss counts per model are confounded by which
    /// model gets the hard work. Not a parity key — the reference ships the per-model counts and no
    /// per-model rate — so it defaults to empty rather than being required of every producer.
    /// </remarks>
    public IReadOnlyList<MissModelRate> MissRatePerOriginModel { get; init; } = [];

    /// <summary>What an attribution split over nothing returns.</summary>
    public static MissAttributionFigures Empty { get; } = new()
    {
        AttributedN = 0,
        AttributionExcluded = 0,
        ExclusionReason = string.Empty,
        ExcludedByConfidence = [],
        ByOriginPhase = [],
        ByOriginModel = [],
        ByOriginAgent = [],
        MissRatePerOriginPhase = []
    };
}

/// <summary>How many misses one non-<c>linked</c> confidence value kept out of the per-origin figures.</summary>
/// <param name="Confidence">The <c>origin_confidence</c> value, or <c>not-recorded</c> when the field was absent.</param>
/// <param name="Records">How many misses carried it.</param>
public sealed record MissAttributionExclusion(string Confidence, int Records);

/// <summary>
/// Misses attributed to one origin phase, over the runs of that command — the "did we specify badly or
/// build badly" row.
/// </summary>
/// <param name="Phase">The <c>cmd</c> that should have produced the artifact correctly.</param>
/// <param name="Misses"><c>linked</c> misses naming it.</param>
/// <param name="Runs">Live runs of that command in the same segment — the denominator.</param>
/// <param name="Rate">Misses over runs, or an honest refusal when too few runs support it.</param>
public sealed record MissPhaseRate(string Phase, int Misses, int Runs, Figure Rate);

/// <summary>
/// Misses attributed to one origin model, over the live runs that model did — the "which model to
/// route to" row (REQ-UI-038).
/// </summary>
/// <remarks>
/// <see cref="PerHundredRuns"/> refuses to be a number when the row stands on fewer than
/// <see cref="MetricsConstants.MinN"/> misses or fewer than <see cref="MetricsConstants.MinN"/> runs, and
/// is not applicable when the model did no live run at all — a rate over nothing is not a zero.
/// </remarks>
/// <param name="Model">The <c>origin_model</c> the emitter derived.</param>
/// <param name="Misses"><c>linked</c> misses naming it.</param>
/// <param name="Runs">Live runs in the same segment whose observed <c>model</c> is this one — the denominator.</param>
/// <param name="PerHundredRuns">Misses per 100 runs to one decimal place, or an honest refusal.</param>
public sealed record MissModelRate(string Model, int Misses, int Runs, Figure PerHundredRuns);

/// <summary>
/// Rework token cost, carrying the attribution split so a blended number is unrepresentable
/// (REQ-FN-079, BRD-122, ADR-019).
/// </summary>
/// <remarks>
/// <para>
/// A fix run that repaired three misses has one token window; dividing by three is arithmetic, not
/// measurement, and the two must never be summed. This type has <b>no</b> property that could hold a
/// blend — not a <c>Total</c>, not a <c>Combined</c>, and no <c>IsApportioned</c> flag beside one
/// <c>Cost</c> — so the page, the export and parity all carry the split by construction. Same technique
/// as <see cref="Figure"/> itself: make the wrong number unrepresentable rather than forbidden.
/// </para>
/// <para>
/// <see cref="NoneCount"/> is a <b>count, never a divisor</b>, and it is correct data rather than missing
/// data: the deliberate <c>log-miss --fixed</c> path omits <c>fix_run_id</c>, which is exactly what makes
/// the record cost <c>none</c>. <c>Sole.SupportingRecords</c> and <c>Apportioned.SupportingRecords</c>
/// carry each column's own <c>n</c>, so no third count is needed to read either honestly.
/// </para>
/// </remarks>
/// <param name="Sole">The headline figure, over <c>cost_attribution == "sole"</c> records only.</param>
/// <param name="Apportioned">The <c>shared:n</c> figure — an apportionment, and labelled as one.</param>
/// <param name="NoneCount">Fix records that can carry no cost at all — parity key <c>cost_unattributable_n</c>.</param>
public sealed record MissCost(Figure Sole, Figure Apportioned, int NoneCount)
{
    /// <summary>What a cost computed over nothing returns.</summary>
    public static MissCost Empty { get; } = new(Figure.NotApplicable(), Figure.NotApplicable(), 0);
}

/// <summary>
/// The money answer for one segment: tokens split by attribution, and dollars split by harness
/// (REQ-FN-079, BRD-122, BRD-123).
/// </summary>
public sealed record MissMoney
{
    /// <summary>Output tokens per miss fixed, as a <see cref="MissCost"/> — never one blended number.</summary>
    public required MissCost TokensPerMissFixed { get; init; }

    /// <summary>Fix records attributed <c>sole</c> — parity key <c>cost_sole_n</c>.</summary>
    public required int SoleRecords { get; init; }

    /// <summary>Fix records attributed <c>shared:n</c> — parity key <c>cost_shared_n</c>.</summary>
    public required int SharedRecords { get; init; }

    /// <summary>
    /// The divisor behind <see cref="MissCost.Sole"/> — parity key <c>tokens_per_miss_measured_n</c>.
    /// </summary>
    /// <remarks>
    /// <b>Not <see cref="SoleRecords"/>.</b> A repair whose <c>tokens_out</c> was never recorded is not a
    /// free repair, and averaging it in as a zero would understate rework in the direction that flatters
    /// the framework (SCHEMA.md §2.5, BRD-31..36). It is therefore excluded from the mean — and the
    /// divisor that results is published here, beside the figure, so the number can be reproduced rather
    /// than merely trusted (BRD-146/149).
    /// </remarks>
    public required int MeasuredTokenRecords { get; init; }

    /// <summary>
    /// <c>sole</c> fix records that carried no <c>tokens_out</c> — parity key <c>tokens_unrecorded_sole_n</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="SoleRecords"/> minus <see cref="MeasuredTokenRecords"/>, carried explicitly so the gap
    /// between the record count and the divisor is a stated figure rather than a subtraction a reader has
    /// to notice. It is unmeasured work, never costless work.
    /// </remarks>
    public required int SoleTokensUnrecorded { get; init; }

    /// <summary>
    /// The divisor behind <see cref="MissCost.Apportioned"/> — parity key <c>tokens_per_miss_apportioned_n</c>.
    /// </summary>
    public required int ApportionedTokenRecords { get; init; }

    /// <summary>
    /// <c>shared:n</c> fix records that carried no <c>tokens_out</c> — parity key <c>tokens_unrecorded_shared_n</c>.
    /// </summary>
    public required int SharedTokensUnrecorded { get; init; }

    /// <summary>
    /// Fix records whose window the stream had written off as unattributable and the recomputed
    /// divisor recovers (<c>cost_recovered_n</c>).
    /// </summary>
    /// <remarks>
    /// Reported beside the split rather than folded into it, so a jump in the cost figures reads as
    /// a corrected derivation rather than as the work having become more expensive. See
    /// <c>MissFigures.MoneyFor</c> for the two ways a stored attribution goes stale.
    /// </remarks>
    public int RecoveredRecords { get; init; }

    /// <summary>
    /// Fix records whose <c>cost_attribution</c> is absent or unrecognised.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> folded into <see cref="MissCost.NoneCount"/>: <c>none</c> is a value the
    /// emitter wrote and means "this fix can carry no cost", while an absent field means nobody said. One
    /// is correct data, the other is missing data, and coercing them together would lose the difference.
    /// </remarks>
    public required int AttributionMissing { get; init; }

    /// <summary>One row per harness, in the fixed harness order; dollars are never summed across them.</summary>
    public required IReadOnlyList<MissHarnessCost> ByHarness { get; init; }

    /// <summary>What money computed over nothing returns.</summary>
    public static MissMoney Empty { get; } = new()
    {
        TokensPerMissFixed = MissCost.Empty,
        SoleRecords = 0,
        SharedRecords = 0,
        MeasuredTokenRecords = 0,
        SoleTokensUnrecorded = 0,
        ApportionedTokenRecords = 0,
        SharedTokensUnrecorded = 0,
        AttributionMissing = 0,
        ByHarness = []
    };
}

/// <summary>
/// One harness's rework money row — measured dollars and estimable tokens in different properties
/// (BRD-123, REQ-NFR-013 clause 5).
/// </summary>
/// <remarks>
/// <para>
/// Measured USD exists in exactly one place in the product: <c>cost_usd</c> on OpenCode records. Every
/// other harness reports <b>tokens</b> as its primary figure, and any dollar figure derived from them can
/// only be a rate-card estimate — which is why <see cref="MeasuredUsdTotal"/> is <c>null</c> and
/// <see cref="EstimateLabel"/> is non-<c>null</c> on exactly those rows. The engine computes no rate-card
/// dollars itself; a caller that prices <see cref="TokensOut"/> must carry
/// <see cref="EstimateLabel"/> on the figure and end the export key in <c>_usd_estimate</c>.
/// </para>
/// <para>
/// Nothing here is ever totalled across rows. <c>cost_usd</c> means different things on different
/// harnesses, and a sum of them would be a number nobody was billed.
/// </para>
/// </remarks>
/// <param name="Harness">The detected harness — <c>claude-code</c>, <c>opencode</c> or <c>codex</c>.</param>
/// <param name="Records">Fix records this harness emitted in the segment.</param>
/// <param name="TokenRecords">
/// How many of those carried any token count at all. <b>Read this before reading the sums.</b> A sum over
/// records that all carried <c>null</c> is <c>0</c>, and <c>0</c> tokens spent and <i>no counts recorded</i>
/// are different facts — this is what tells them apart (SCHEMA.md §2.5).
/// </param>
/// <param name="TokensIn">Input tokens summed over the records that carried a count.</param>
/// <param name="TokensOut">Output tokens summed over the records that carried a count.</param>
/// <param name="TokensCacheRead">Cache-read tokens summed over the records that carried a count.</param>
/// <param name="TokensCacheWrite">Cache-write tokens summed over the records that carried a count.</param>
/// <param name="MeasuredUsdPerMiss">Measured dollars per fix — <c>NotApplicable</c> on every harness but OpenCode.</param>
/// <param name="MeasuredUsdTotal">Measured dollars, or <c>null</c> when this harness measures none.</param>
/// <param name="MeasuredUsdRecords">Records carrying a measurement — parity key <c>cost_usd_records</c>.</param>
/// <param name="EstimateLabel">The wording any dollar figure derived from these tokens must carry, or <c>null</c> when the row is measured.</param>
public sealed record MissHarnessCost(
    string Harness,
    int Records,
    int TokenRecords,
    long TokensIn,
    long TokensOut,
    long TokensCacheRead,
    long TokensCacheWrite,
    Figure MeasuredUsdPerMiss,
    decimal? MeasuredUsdTotal,
    int MeasuredUsdRecords,
    string? EstimateLabel);

/// <summary>
/// What the owner's reviews cost, and what a repair was worth at list price
/// (SCHEMA.md §5.5.9 and §2.5b; REQ-FN-138).
/// </summary>
/// <remarks>
/// <para>
/// <b>A review record is the only record in any stream that prices a specification defect.</b> A report
/// that shows build and verify costs but not review costs tells its reader that documents are free —
/// and the owner's own review time is the scarcest input the whole framework consumes.
/// </para>
/// <para>
/// Every mean divides by the records that <b>carry</b> the figure, never by all of them, and the count
/// travels with the mean so a consumer can reproduce it rather than choosing between agreeing and being
/// right. Nothing here is computed or apportioned: the emitter copied these token counts from the two
/// runs each record names, and TfLens stores and reports them exactly as they arrived.
/// </para>
/// </remarks>
public sealed record MissReviewBlock
{
    /// <summary>The block for a framework whose stream holds no review record.</summary>
    public static MissReviewBlock Empty { get; } = new()
    {
        ByPhase = [],
        TokensToProduce = Figure.InsufficientData(0),
        TokensToCorrect = Figure.InsufficientData(0),
        ListUsdPerMiss = null
    };

    /// <summary>Review records read — stated because they are <b>not</b> misses and never counted as any.</summary>
    public int ReviewsN { get; init; }

    /// <summary>Corrections the owner gave across every review.</summary>
    public int Corrections { get; init; }

    /// <summary>Reviews per phase — <c>day1-review</c>, <c>build-review</c> and the rest.</summary>
    public required IReadOnlyList<KeyValuePair<string, int>> ByPhase { get; init; }

    /// <summary>Mean output tokens spent producing the reviewed output.</summary>
    public required Figure TokensToProduce { get; init; }

    /// <summary>Records carrying that figure — the mean's own divisor.</summary>
    public int TokensToProduceN { get; init; }

    /// <summary>Mean output tokens spent applying the owner's corrections.</summary>
    public required Figure TokensToCorrect { get; init; }

    /// <summary>Records carrying that figure.</summary>
    public int TokensToCorrectN { get; init; }

    /// <summary>
    /// Repairs whose dollars were measured and are <b>not money</b> — a subscription's zero, a local
    /// model's absent bill.
    /// </summary>
    /// <remarks>
    /// Printed beside the measured-dollars figure and never inside it. A flat monthly fee bills nothing
    /// extra, so its zero is true of the marginal cost and false of the money; averaging it in would
    /// drag dollars-per-miss toward zero and make repairs look almost free.
    /// </remarks>
    public int CostExcludedNotMoneyN { get; init; }

    /// <summary>
    /// What the average repair's tokens would have cost at the published rate, or <c>null</c> below the
    /// minimum-n floor.
    /// </summary>
    /// <remarks>
    /// The figure that works on <b>every</b> harness. A Claude Code repair can have no measured dollars
    /// at all, because a subscription bills nothing per token — so this is the only money-shaped answer
    /// it can give, and it is a price rather than a bill.
    /// </remarks>
    public decimal? ListUsdPerMiss { get; init; }

    /// <summary>Repairs that could be priced; the denominator of <see cref="ListUsdPerMiss"/>.</summary>
    public int ListUsdRecords { get; init; }
}
