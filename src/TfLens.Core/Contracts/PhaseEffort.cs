namespace TfLens.Core.Contracts;

/// <summary>
/// A token figure together with the runs it could and could not be computed over (REQ-FN-089, BRD-146).
/// </summary>
/// <remarks>
/// <para>
/// <b>The denominator travels with the figure.</b> A run whose token window could not be computed
/// carries <c>tokens_scope: "none"</c> (or no scope at all) and no token numbers. It is excluded from
/// every token figure and counted in <see cref="UnmeasuredN"/> — it is <b>never averaged in as zero</b>.
/// That coercion is <c>TF-005</c>, the defect TfLens itself reported upstream: <c>or 0</c> cannot tell an
/// absent field from a measured zero, and the resulting error always runs in the direction that flatters
/// the framework.
/// </para>
/// <para>
/// This type exists so the mistake is <b>unrepresentable rather than forbidden</b>. A page binding it
/// receives <see cref="Tokens"/> and <see cref="MeasuredN"/> in one value and cannot render the number
/// without also holding the count it rests on — the same technique as <see cref="Figure"/> (ADR-007) and
/// <see cref="MissCost"/> (ADR-019), applied a third time (ADR-026). The UI rule that follows from it is
/// that every token tile shows <c>measured on n of N runs</c> as visible text, and that
/// <see cref="Tokens"/> reads <c>insufficient data (n=…)</c> — never <c>0</c> — below
/// <see cref="MetricsConstants.MinN"/>.
/// </para>
/// <para>
/// <see cref="MeasuredN"/> and <see cref="UnmeasuredN"/> are deliberately <b>never added together</b>
/// anywhere in the product. The phase's own run count is the denominator a reader wants
/// (<see cref="PhaseEffortRow.Runs"/>); measured and unmeasured are the two halves it partitions into,
/// and their sum is a number nothing needs.
/// </para>
/// </remarks>
/// <param name="Tokens">
/// The figure itself — a mean, a total or a share — or an honest refusal below the minimum-n floor.
/// </param>
/// <param name="MeasuredN">Runs with a usable token window; <b>the divisor</b>.</param>
/// <param name="UnmeasuredN">
/// Runs excluded because no window could be computed. Displayed, never summed into the figure.
/// </param>
public sealed record TokenWindow(Figure Tokens, int MeasuredN, int UnmeasuredN)
{
    /// <summary>The window for a phase in which nothing could be measured at all.</summary>
    public static TokenWindow None { get; } = new(Figure.NotApplicable(), 0, 0);

    /// <summary>True when every run in the phase carried a usable window.</summary>
    /// <remarks>
    /// The table column BRD-146 asks for is <c>4/4</c> in green and <c>5/9</c> in amber: a phase whose
    /// token figures rest on half its runs is a different claim from one that rests on all of them, and
    /// that difference belongs on screen rather than in a tooltip.
    /// </remarks>
    public bool IsFullyMeasured => UnmeasuredN == 0;
}

/// <summary>
/// A fan-out figure together with the runs that could and could not be observed (REQ-FN-090, BRD-147).
/// </summary>
/// <remarks>
/// <para>
/// <b>Fan-out observation is a predicate, never a coalesce (ADR-026).</b> A run qualifies only when its
/// <c>tokens_scope</c> is <c>tree</c> <b>and</b> its <c>subagent_runs</c> is not <c>null</c>. A
/// <c>main</c>-scope window never read the sub-agent transcripts at all, so such a run did not report
/// "zero sub-agents" — it reported nothing. <c>?? 0</c> would turn <i>we did not look</i> into a
/// measurement, and the resulting fan-out average would be confidently composed largely of runs that
/// could not have seen a sub-agent. Nothing about the number would look wrong, which is the whole hazard.
/// </para>
/// <para>
/// The exclusion is published <b>two ways because it is two different facts</b>, and they have different
/// futures: <see cref="UnobservedNotTree"/> (<i>we did not look</i>) could change tomorrow, whereas
/// <see cref="UnobservedPredatesField"/> (<i>we could not have looked</i> — written before
/// <c>subagent_runs</c> existed on 2026-08-31) never will. Collapsing them into one count would lose the
/// only information a reader needs to decide whether the coverage is worth waiting for.
/// </para>
/// <para>
/// <b>The declared list and the measured count are both reported and are never reconciled (BRD-149).</b>
/// <see cref="PhaseEffortRow.SubagentsDeclared"/> is typed by the agent into its own emit and says which
/// <i>kinds</i> were invoked; <see cref="SpawnsTotal"/> here is counted from the harness's own store and
/// says how many <i>actually ran</i>. <b>Where they disagree the measured figure is authoritative</b>
/// (SCHEMA.md §2.6) — and the gap between them is itself a finding about how accurately tasks self-report,
/// not an error to reconcile away.
/// </para>
/// <para>
/// The UI rule that follows: state <c>observed_n of runs</c> <b>first</b> and the numbers second, and
/// render <b>"not observed"</b> where <see cref="ObservedN"/> is zero — never <c>0 subagents</c>.
/// </para>
/// <para>
/// <b>Why <see cref="Spawns"/> and <see cref="SpawnsMax"/> are nullable numbers rather than
/// <see cref="Figure"/>s.</b> The oracle's <c>analyse_phases</c> applies <b>no</b>
/// <see cref="MetricsConstants.MinN"/> floor to <c>spawns_median</c> or <c>spawns_max</c> — unlike its
/// pooled block, which floors <c>batch_size_median</c> and <c>throughput_median_reqs_per_hour</c>
/// explicitly. They are <c>null</c> when and only when nothing was observed. A <see cref="Figure"/> here
/// would refuse a number the oracle prints, which BRD §13 treats as a mismatch in exactly the same way
/// as printing one it refuses. The guarantee this type makes is not a floor: it is that
/// <see cref="ObservedN"/> travels with the number, so a single-run median can never be read as a
/// population figure. <c>tokens_out_per_run</c> keeps its <see cref="Figure"/> because the oracle
/// genuinely floors that one.
/// </para>
/// </remarks>
/// <param name="Spawns">
/// Median sub-agent invocations per observed run, or <c>null</c> when nothing could be observed.
/// </param>
/// <param name="ObservedN">
/// Runs that satisfied the predicate — <b>read this first, it is the denominator</b>.
/// </param>
/// <param name="UnobservedNotTree">
/// Runs whose window was <c>main</c>, <c>conversation</c> or <c>none</c>: <i>we did not look</i>.
/// </param>
/// <param name="UnobservedPredatesField">
/// Tree-scope runs written before 2026-08-31: <i>we could not have looked</i>. A permanent exclusion.
/// </param>
public sealed record FanoutObservation(
    double? Spawns,
    int ObservedN,
    int UnobservedNotTree,
    int UnobservedPredatesField)
{
    /// <summary>The observation for a phase in which no run could be observed at all.</summary>
    public static FanoutObservation NotObserved { get; } =
        new(null, 0, 0, 0) { SubagentShareOfTokensOut = PhaseShare.NotApplicable };

    /// <summary>
    /// Every excluded run, the oracle's <c>unobserved_n</c>.
    /// </summary>
    /// <remarks>
    /// The two components stay separate properties and this convenience never replaces them. It is a sum
    /// of two <i>exclusions</i>, which is a coverage fact; it is not a sum of a measured and an unmeasured
    /// quantity, which is the thing the product refuses to compute anywhere.
    /// </remarks>
    public int UnobservedN => UnobservedNotTree + UnobservedPredatesField;

    /// <summary>True when at least one run could be observed; false means <b>"not observed"</b>.</summary>
    public bool IsObserved => ObservedN > 0;

    /// <summary>Sub-agent invocations summed over the observed runs only.</summary>
    public int SpawnsTotal { get; init; }

    /// <summary>The busiest observed run's spawn count, or <c>null</c> when nothing was observed.</summary>
    public int? SpawnsMax { get; init; }

    /// <summary>Observed runs that spawned at least one sub-agent.</summary>
    public int RunsWithFanout { get; init; }

    /// <summary>
    /// Output tokens the sub-agents consumed, summed over observed runs that reported the field.
    /// </summary>
    /// <remarks>
    /// A count, never a divisor: which sub-agent spent what is not carried by the producer, so no
    /// per-sub-agent figure — and emphatically no per-sub-agent dollar — can be derived from it.
    /// </remarks>
    public long TokensOutSubagents { get; init; }

    /// <summary>
    /// The sub-agents' share of the observed runs' output, as the oracle's own <c>"50%"</c> / <c>"—"</c>
    /// string.
    /// </summary>
    /// <remarks>
    /// The denominator is the <b>observed</b> runs' output tokens, not the phase's, because a share read
    /// against runs whose transcripts were never opened would understate it by exactly the coverage gap.
    /// </remarks>
    public string SubagentShareOfTokensOut { get; init; } = PhaseShare.NotApplicable;
}

/// <summary>
/// The four token totals over a phase's measured runs (SCHEMA.md §2.5).
/// </summary>
/// <remarks>
/// These are sums over the runs that carried a usable window, so they need no minimum-n floor — a total
/// of one run is that run. The counts they rest on are <see cref="PhaseEffortRow.TokensMeasuredN"/> and
/// <see cref="PhaseEffortRow.TokensUnmeasuredN"/>, which sit beside them on the row for exactly that
/// reason.
/// </remarks>
/// <param name="In">Input tokens.</param>
/// <param name="Out">Output tokens — the numerator of <c>share_of_tokens_out</c>.</param>
/// <param name="CacheRead">Cache-read tokens.</param>
/// <param name="CacheWrite">Cache-write tokens.</param>
public sealed record PhaseTokens(long In, long Out, long CacheRead, long CacheWrite)
{
    /// <summary>The totals for a phase whose runs carried no window at all.</summary>
    public static PhaseTokens Zero { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// A phase's wall-clock block — total, median, max, the count of runs that were timed, and how many of
/// those durations this read derived (REQ-FN-112, BRD-179).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="DerivedN"/> is why this block can be trusted against the token block beside it.</b>
/// <c>duration_s</c> entered the stream after some records were written; those records carry
/// <c>started</c> and <c>ended</c> but no duration, and a reader that simply sums the field counts them
/// as <b>zero time while still counting their tokens</b> — so a phase's time covers one set of runs and
/// its tokens another. The framework's own reset reported <b>16h49m</b> under that bug against a true
/// <b>55h57m</b>.
/// </para>
/// <para>
/// The derivation is arithmetic on two recorded facts, done at <b>read time</b> and written back to
/// nothing (<c>RunDuration</c>). The count rides on the block rather than being computed and discarded,
/// so a surface cannot render the total without also holding the number BRD-179 requires beside it —
/// the same technique as <see cref="TokenWindow"/> and <see cref="FanoutObservation"/>.
/// </para>
/// </remarks>
/// <param name="TotalSeconds">Summed duration over the timed runs.</param>
/// <param name="MedianSeconds">The median, or <c>null</c> when no run was timed.</param>
/// <param name="MaxSeconds">The longest timed run, or <c>null</c> when no run was timed.</param>
/// <param name="TimedN">Runs carrying a non-zero <c>duration_s</c>; the block's own denominator.</param>
/// <param name="DerivedN">
/// The oracle's <c>duration_s.derived_n</c>, exported key-for-key: runs whose window this read closed
/// from their own <c>started</c> and <c>ended</c> — or <c>ts</c>, SCHEMA.md's stand-in for an absent
/// <c>ended</c> — because they carried no usable duration. <b>Not a subset of
/// <paramref name="TimedN"/></b>: a run that starts and ends in the same second is closed from its
/// timestamps and still contributes nothing, which is why a page states <see cref="DerivedTimedN"/>
/// beside its time figure instead of this (REQ-FN-112).
/// </param>
public sealed record PhaseDuration(
    long TotalSeconds,
    double? MedianSeconds,
    long? MaxSeconds,
    int TimedN,
    int DerivedN)
{
    /// <summary>The block for a phase in which nothing was timed.</summary>
    public static PhaseDuration None { get; } = new(0, null, null, 0, 0);

    /// <summary>
    /// Timed runs whose duration was derived from the record's own timestamps — the subset of
    /// <see cref="TimedN"/> a page states beside the time figure (REQ-FN-112, BRD-179).
    /// </summary>
    /// <remarks>
    /// Always at most <see cref="TimedN"/>, so a surface can say "<i>d</i> of those <i>n</i> durations
    /// were derived" and have it be true. <see cref="DerivedN"/> can exceed <see cref="TimedN"/> — a
    /// phase of same-second runs is all derived and nothing timed — and printed as a share of the timed
    /// runs it read "62 of 36", which is no count at all.
    /// </remarks>
    public int DerivedTimedN { get; init; }

    /// <summary>
    /// Timed runs whose timestamps overrode a stored <c>duration_s</c> that disagreed with them by more
    /// than a second (BRD-192) — the phase's share of <c>duration_recomputed_n</c>.
    /// </summary>
    public int RecomputedN { get; init; }

    /// <summary>
    /// Runs whose <c>ended</c> precedes their <c>started</c>, excluded from every duration figure of the
    /// phase (BRD-190) — the phase's share of <c>duration_impossible_n</c>.
    /// </summary>
    public int ImpossibleN { get; init; }

    /// <summary>
    /// Runs that recorded no elapsed time (BRD-191) — never corrupt; the phase's share of
    /// <c>duration_absent_n</c>. With <see cref="TimedN"/> and <see cref="ImpossibleN"/> it partitions
    /// the phase's live runs.
    /// </summary>
    public int AbsentN { get; init; }
}

/// <summary>
/// One model's share of a phase's output, taken from the per-model <b>split</b> (REQ-FN-092, BRD-150).
/// </summary>
/// <remarks>
/// <para>
/// <b>Whenever a run carries <c>model_tokens_out</c>, that split is the only thing read.</b> A run that
/// spent 90% of its output on one model and 10% on another, and a run that split evenly, are different
/// facts about cost and about routing; <c>model</c> and <c>models</c> cannot tell them apart, so reading
/// the label on such a run would attribute the whole window to the winner. That is the misattribution
/// BRD-150 forbids, and it cannot happen here.
/// </para>
/// <para>
/// <b>A run carrying no split at all falls back to its dominant <c>model</c> label</b>, exactly as the
/// oracle's <c>analyse_phases</c> does, because BRD §13 is key-for-key and a divergence here would fail
/// it on every record written before <c>model_tokens_out</c> shipped on 2026-08-31 — which is most of
/// them. The fallback is narrower than it sounds and is not the forbidden case: a record with no split is
/// not a record <i>known</i> to be mixed, and its label is the only observation of a model that exists.
/// It is nonetheless a weaker observation, so it is <b>counted separately</b> in
/// <see cref="RunsFromLabel"/> rather than blended invisibly — a row resting largely on labels is a
/// different claim from one resting on splits, and a surface can say so.
/// </para>
/// <para>
/// The ranking this produces is <b>observational, not causal</b>: which model gets the hard phases is not
/// random, and a surface rendering these rows carries that caveat once on the page.
/// </para>
/// </remarks>
/// <param name="Model">The model id exactly as the producer wrote it.</param>
/// <param name="Runs">Runs that contributed to this row.</param>
/// <param name="TokensOut">Output tokens attributed to it.</param>
public sealed record PhaseModelEffort(string Model, int Runs, long TokensOut)
{
    /// <summary>
    /// What this model's tokens would cost at the published rate, or <c>null</c> where nothing prices
    /// it (SCHEMA.md §2.5b, REQ-FN-136).
    /// </summary>
    /// <remarks>
    /// Attributed only from runs that ran <b>one</b> model. Splitting a mixed window's price across its
    /// models by token share is arithmetic, not measurement — the same line drawn everywhere else in
    /// this file between what was observed and what was apportioned. A price, never a bill.
    /// </remarks>
    public decimal? ListUsd { get; init; }

    /// <summary>Contributing runs that carried a <c>model_tokens_out</c> split — the strong observation.</summary>
    public int RunsFromSplit { get; init; }

    /// <summary>
    /// Contributing runs that carried no split, so their whole window was read off the dominant label.
    /// </summary>
    /// <remarks>
    /// Displayed rather than hidden. It is not in the export, because the oracle emits no such key and an
    /// added key is a parity finding; it exists so the product surface can mark a row whose weight comes
    /// from labels rather than from measured splits.
    /// </remarks>
    public int RunsFromLabel { get; init; }
}

/// <summary>
/// A phase's routing counts — observed, never enforced (SCHEMA.md §2.5).
/// </summary>
/// <remarks>
/// <see cref="Drifted"/> is drift <i>made visible</i>, not an error, and a surface must not style it as a
/// failure. <see cref="Unknown"/> is its own count rather than being folded into either side: a run that
/// carried no <c>routed</c> flag did not route correctly and did not drift — it said nothing.
/// </remarks>
/// <param name="Routed">Runs whose request went through the requested tier.</param>
/// <param name="Drifted">Runs that carried <c>routed: false</c>.</param>
/// <param name="Unknown">Runs carrying no routing flag at all.</param>
public sealed record PhaseRouting(int Routed, int Drifted, int Unknown)
{
    /// <summary>The block for a phase with no routing information at all.</summary>
    public static PhaseRouting None { get; } = new(0, 0, 0);
}

/// <summary>
/// Measured dollars for one harness, on a phase (BRD-148, SCHEMA.md §4).
/// </summary>
/// <remarks>
/// <b>Never pooled across harnesses and never priced from a rate card.</b> Claude Code and Codex carry
/// <c>cost_usd: null</c> permanently and only OpenCode measures real spend, so a cross-harness total
/// would be a number with no referent and a rate-card figure would be an estimate presented as a
/// measurement — the one thing this telemetry design refuses to do. A harness that measured nothing has
/// no row here rather than a zero one.
/// </remarks>
/// <param name="Harness">The detected harness that measured the spend.</param>
/// <param name="Usd">Summed measured spend, in dollars.</param>
/// <param name="Records">Runs that carried a measurement.</param>
public sealed record PhaseHarnessCost(string Harness, decimal Usd, int Records);

/// <summary>
/// One phase's effort row — everything the <c>/effort</c> table and its expanded detail render
/// (REQ-FN-089..REQ-FN-093, BRD-146..BRD-152).
/// </summary>
/// <remarks>
/// <para>
/// The row is keyed by <see cref="Cmd"/>, which is the framework command that ran, because <b>the unit of
/// work is the run, not the ticket</b>. There is deliberately no per-REQ or per-feature member on this
/// type: a <c>*build-phase</c> run touching eight REQs has one duration and one token window, and
/// dividing it eight ways is arithmetic dressed as measurement (SCHEMA.md §0).
/// </para>
/// <para>
/// Effort per phase is a <b>budgeting and capacity</b> view. A phase costing more than another is a fact
/// about what those phases are, not evidence about either; quality is measured elsewhere.
/// </para>
/// </remarks>
public sealed record PhaseEffortRow
{
    /// <summary>The framework command, e.g. <c>build-phase</c>; <c>—</c> when a run named none.</summary>
    public required string Cmd { get; init; }

    /// <summary>Live runs in this phase — the denominator every coverage count is read against.</summary>
    public required int Runs { get; init; }

    /// <summary>The wall-clock block.</summary>
    public required PhaseDuration Duration { get; init; }

    /// <summary>Share of all timed wall clock, as the oracle's own <c>"81%"</c> / <c>"—"</c> string.</summary>
    public required string ShareOfDuration { get; init; }

    /// <summary>
    /// Runs with a usable token window and an output count; <b>the divisor</b> of every token figure.
    /// </summary>
    public required int TokensMeasuredN { get; init; }

    /// <summary>
    /// Runs excluded from every token figure because no window could be computed.
    /// </summary>
    /// <remarks>
    /// Three ways to land here and they are one fact — <b>no window</b>: <c>tokens_scope: "none"</c>, no
    /// scope at all, or a scope with no <c>tokens_out</c> captured. Never counted as zero, and visible on
    /// screen wherever a token figure is rather than in a tooltip (BRD-146).
    /// </remarks>
    public required int TokensUnmeasuredN { get; init; }

    /// <summary>
    /// The money block for one phase — three figures that are never merged (SCHEMA.md §2.5b).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>List price</b> is what these tokens would cost at the published rate: a price, computed at read
    /// time over every record that can be priced, and the only figure that compares a subscription phase
    /// with a metered one. <b>Plan allowance</b> is what a monthly plan's per-model limit was consumed by
    /// — a meter, not an invoice. <b>Money billed</b> is what a metered provider actually charged.
    /// </para>
    /// <para>
    /// A window that ran models paid for in different ways contributes real money that cannot be
    /// separated from the part a subscription had already covered, so it gets its own line rather than
    /// being folded into any of the three.
    /// </para>
    /// </remarks>
    public required PhaseMoney Money { get; init; }

    /// <summary>The four token totals over the measured runs.</summary>
    public required PhaseTokens Tokens { get; init; }

    /// <summary>
    /// Median output tokens per measured run, or <c>null</c> when nothing was measured.
    /// </summary>
    /// <remarks>
    /// Shown <b>beside</b> <see cref="TokensOutPerRun"/> rather than instead of it: a mean far above the
    /// median means one long run dominates the phase, which is the most decision-relevant thing on the
    /// token band. Unlike the mean this carries no minimum-n floor, because the oracle names only
    /// <c>tokens_out_per_run</c>, <c>duration_s.median</c>, <c>spawns_median</c> and <c>spawns_max</c> as
    /// the keys that go <c>null</c> below <c>MIN_N</c>.
    /// </remarks>
    public required double? TokensOutMedian { get; init; }

    /// <summary>
    /// Mean output tokens per measured run, wrapped in the counts it rests on.
    /// </summary>
    /// <remarks>
    /// <see cref="TokenWindow.Tokens"/> is <see cref="FigureKind.InsufficientData"/> below
    /// <see cref="MetricsConstants.MinN"/> measured runs, rendered <i>insufficient data (n=…)</i> and
    /// never <c>0</c>.
    /// </remarks>
    public required TokenWindow TokensOutPerRun { get; init; }

    /// <summary>Share of all measured output, as the oracle's own <c>"87%"</c> / <c>"—"</c> string.</summary>
    public required string ShareOfTokensOut { get; init; }

    /// <summary>The per-model split, heaviest first; from <c>model_tokens_out</c> only (BRD-150).</summary>
    public required IReadOnlyList<PhaseModelEffort> Models { get; init; }

    /// <summary>Runs per detected harness.</summary>
    public required IReadOnlyList<KeyValuePair<string, int>> Harnesses { get; init; }

    /// <summary>Runs per <c>mode</c> (<c>build</c> / <c>fix</c>).</summary>
    public required IReadOnlyList<KeyValuePair<string, int>> Modes { get; init; }

    /// <summary>Runs per <c>build_result</c>.</summary>
    public required IReadOnlyList<KeyValuePair<string, int>> BuildResults { get; init; }

    /// <summary>Summed <c>reqs_count</c> — which REQs were touched, never how the minutes divided.</summary>
    public required int ReqsTouchedTotal { get; init; }

    /// <summary>Summed <c>files_written</c>.</summary>
    public required int FilesWrittenTotal { get; init; }

    /// <summary>
    /// The sub-agent <i>kinds</i> the runs declared, and how often each was named (BRD-149).
    /// </summary>
    /// <remarks>
    /// A <b>self-report</b>: an agent types this list into its own emit, and it carries no count when the
    /// same kind is spawned four times. It is shown beside <see cref="Fanout"/>, which is the harness's
    /// own count, and <b>where the two disagree the measured one is authoritative</b>. The gap is a
    /// finding about self-report accuracy, not a discrepancy to reconcile away.
    /// </remarks>
    public required IReadOnlyList<KeyValuePair<string, int>> SubagentsDeclared { get; init; }

    /// <summary>The measured fan-out, wrapped in the count of runs that could be observed.</summary>
    public required FanoutObservation Fanout { get; init; }

    /// <summary>Routing counts; drift is made visible, never styled as a failure.</summary>
    public required PhaseRouting Routing { get; init; }

    /// <summary>Measured dollars per harness; never summed across them, never estimated.</summary>
    public required IReadOnlyList<PhaseHarnessCost> CostUsdByHarness { get; init; }

    /// <summary>
    /// True when the declared kinds and the measured spawns disagree, so a surface can say so (BRD-149).
    /// </summary>
    /// <remarks>
    /// Only meaningful while <see cref="FanoutObservation.IsObserved"/> holds: with nothing observed there
    /// is no measurement to disagree with, and claiming a discrepancy would be the same error as claiming
    /// a zero.
    /// </remarks>
    public bool DeclaredDiffersFromMeasured =>
        Fanout.IsObserved && SubagentsDeclared.Sum(aKind => aKind.Value) != Fanout.SpawnsTotal;
}

/// <summary>
/// The whole TechieFlow phase-effort block for one user and framework (REQ-FN-093, BRD-152).
/// </summary>
/// <remarks>
/// The key layout mirrors the oracle's <c>phases</c> object, which rides inside
/// <c>tf-metrics.sh --report --json</c> and <c>--rollup --json</c>, so the BRD §13 compare walks it
/// key-for-key with no mapping layer and needs no new invocation.
/// </remarks>
public sealed record PhaseEffortAnalysis
{
    /// <summary>The standing caveat the block carries into every rendering of it.</summary>
    public const string StandingNote =
        "Token figures exclude runs whose window could not be computed and count them as "
        + "tokens_unmeasured_n; they are never averaged in as zero. Fan-out figures cover "
        + "tokens_scope == \"tree\" runs carrying subagent_runs only, and the two exclusions are "
        + "reported separately: unobserved_not_tree means the window never read the sub-agent "
        + "transcripts, unobserved_predates_field means the run was written before the field existed on "
        + "2026-08-31. Wherever a run carries model_tokens_out that split is what the per-model band "
        + "reads, so a mixed-model window is never filed whole under its dominant label; a run carrying "
        + "no split falls back to that label and is counted apart. Dollars are measured per harness and "
        + "are never pooled across harnesses or priced from a rate card. The unit is the RUN, never the "
        + "feature or the REQ.";

    /// <summary>The block a framework with no live run reports.</summary>
    public static PhaseEffortAnalysis Empty { get; } = new()
    {
        RunsLive = 0,
        ScopeCoverage = [],
        TokensOutTotal = 0,
        DurationSecondsTotal = 0,
        DurationMeasuredN = 0,
        DurationImpossibleN = 0,
        DurationAbsentN = 0,
        DurationRecomputedN = 0,
        Phases = []
    };

    /// <summary>Live (non-backfilled) run records the block was computed over.</summary>
    public required int RunsLive { get; init; }

    /// <summary>
    /// Live runs per <c>tokens_scope</c> — <c>tree</c>, <c>main</c>, <c>conversation</c>, <c>none</c>.
    /// </summary>
    /// <remarks>
    /// This is the page's coverage headline and is deliberately not buried: fan-out measurement started on
    /// 2026-08-31, so a small <c>tree</c> count is the honest first reading rather than a defect.
    /// </remarks>
    public required IReadOnlyList<KeyValuePair<string, int>> ScopeCoverage { get; init; }

    /// <summary>Measured output tokens across every phase — the denominator of <c>share_of_tokens_out</c>.</summary>
    public required long TokensOutTotal { get; init; }

    /// <summary>Timed wall clock across every phase — the denominator of <c>share_of_duration</c>.</summary>
    public required long DurationSecondsTotal { get; init; }

    /// <summary>
    /// Live records that produced a usable duration — the denominator of
    /// <see cref="DurationSecondsTotal"/> (BRD-189).
    /// </summary>
    /// <remarks>
    /// Published beside the total rather than left to be inferred: a total offered without it invites
    /// the reader to take it for a figure over every run, which on TechieBlog would be 46 runs where
    /// only 33 are usable. With <see cref="DurationImpossibleN"/> and <see cref="DurationAbsentN"/> it
    /// partitions <see cref="RunsLive"/> exactly, so the three counts can be checked against each other.
    /// </remarks>
    public required int DurationMeasuredN { get; init; }

    /// <summary>
    /// Live records whose <c>ended</c> precedes their <c>started</c>, excluded from every duration
    /// figure (BRD-190).
    /// </summary>
    /// <remarks>
    /// Nothing is substituted, clamped or guessed for these, and the records themselves are never
    /// edited or deleted — the streams are append-only (SCHEMA.md §3). They stay where they are and
    /// this count is what the report publishes in their place.
    /// </remarks>
    public required int DurationImpossibleN { get; init; }

    /// <summary>
    /// Live records that recorded no elapsed time (BRD-191).
    /// </summary>
    /// <remarks>
    /// A run whose start and end fall in the same second, or one whose timestamps cannot be read and
    /// which carries no positive stored duration. This is <b>not</b> corruption and must never be
    /// rendered as such: reporting it as impossible made the framework's own repository look as though
    /// it held thirty corrupt records when it held thirty runs that recorded nothing, and a reader told
    /// the wrong reason chases the wrong thing.
    /// </remarks>
    public required int DurationAbsentN { get; init; }

    /// <summary>
    /// Live records where the timestamps overrode a stored <c>duration_s</c> that disagreed with them
    /// by more than a second (BRD-192).
    /// </summary>
    /// <remarks>
    /// A subset of <see cref="DurationMeasuredN"/>, never an addition to it. It is what makes the
    /// override auditable: a total that moved because stored figures were overridden is a different
    /// claim from one read straight off the stream, and without this count the two are
    /// indistinguishable.
    /// </remarks>
    public required int DurationRecomputedN { get; init; }

    /// <summary>One row per <c>cmd</c>, heaviest measured output first.</summary>
    public required IReadOnlyList<PhaseEffortRow> Phases { get; init; }

    /// <summary>Runs that could be fan-out observed, across every phase — a coverage figure.</summary>
    /// <remarks>
    /// Read as <c>n of <see cref="RunsLive"/></c>. On a framework whose records mostly predate
    /// 2026-08-31 this reads <c>1 of 13</c>, which is the honest headline rather than a reason to hide it:
    /// a page that only looks right once the data is dense is a page nobody trusts in the meantime.
    /// </remarks>
    public int FanoutObservedN => Phases.Sum(aRow => aRow.Fanout.ObservedN);

    /// <summary>
    /// Durations this read derived, across every phase — the count BRD-179 requires beside every time
    /// figure built on them (REQ-FN-112).
    /// </summary>
    /// <remarks>
    /// A page-level restatement of the per-phase <see cref="PhaseDuration.DerivedN"/>, so the wall-clock
    /// headline can carry its own provenance without summing anything the rows do not already hold. It
    /// is a count of <i>derivations</i>, not of seconds, and it is never subtracted from
    /// <see cref="DurationSecondsTotal"/> — the derived minutes are real minutes; what the reader is
    /// being told is how many of them were worked out rather than read.
    /// </remarks>
    public int DurationsDerivedN => Phases.Sum(aRow => aRow.Duration.DerivedN);

    /// <summary>
    /// Timed runs whose duration was derived from their own timestamps, across every phase — the count
    /// the wall-clock headline states beside <see cref="DurationSecondsTotal"/> (REQ-FN-112).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="DurationsDerivedN"/> this is a subset of <see cref="DurationMeasuredN"/>, so it
    /// can be read as "<i>d</i> of the <i>n</i> timed runs" without claiming more derived durations than
    /// there are durations.
    /// </remarks>
    public int DurationsDerivedTimedN => Phases.Sum(aRow => aRow.Duration.DerivedTimedN);
}

/// <summary>
/// The reference's <c>pct()</c> vocabulary, shared by every phase-effort share.
/// </summary>
/// <remarks>
/// The shares are the oracle's own <c>"87%"</c> / <c>"—"</c> <b>strings</b> and BRD-152 requires them to
/// be diffed as strings, never reformatted first. Keeping the em dash in one named place is what stops a
/// second spelling of "no denominator" entering the document.
/// </remarks>
public static class PhaseShare
{
    /// <summary>What a share reads when its denominator is zero.</summary>
    public const string NotApplicable = "—";

    /// <summary>
    /// The share of all measured output a surface may show for one phase (REQ-FN-089, BRD-146).
    /// </summary>
    /// <remarks>
    /// <see cref="PhaseEffortRow.ShareOfTokensOut"/> stays the oracle's own string, because the export is
    /// diffed against the reference key-for-key (BRD-152) — and for a phase measured on no run the
    /// reference writes <c>"0%"</c>, a share of zero taken over tokens nobody counted. Printed on a page
    /// that is the zero standing in for "not measured" BRD-146 forbids. A surface therefore reads the
    /// share through this method: <see cref="NotApplicable"/> where no run carried a token window, the
    /// oracle's string unaltered everywhere else (a measured zero included).
    /// </remarks>
    /// <param name="aRow">The phase.</param>
    /// <returns>The share string, or <see cref="NotApplicable"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRow"/> is <c>null</c>.</exception>
    public static string OfMeasuredOutput(PhaseEffortRow aRow)
    {
        ArgumentNullException.ThrowIfNull(aRow);

        return aRow.TokensMeasuredN == 0 ? NotApplicable : aRow.ShareOfTokensOut;
    }
}

/// <summary>
/// One phase's money block — three figures that mean different things and are never merged
/// (SCHEMA.md §2.5b, REQ-FN-136, REQ-FN-137).
/// </summary>
/// <remarks>
/// <para>
/// The separation is the point. <b>List price</b> is a rate card applied to measured tokens: it exists
/// for every priced run on every harness, and it is the only figure that lets a subscription phase be
/// compared with a metered one. <b>Plan allowance</b> is what a monthly plan's per-model limit was
/// consumed by — a meter, not an invoice. <b>Money billed</b> is what a provider that charges per token
/// actually charged. A flat monthly fee bills nothing extra, so a subscription's marginal cost is a true
/// zero and never appears as spend.
/// </para>
/// <para>
/// <see cref="MixedUsd"/> is real money that cannot be attributed: the window ran models paid for in
/// different ways, so part of the bill is inseparable from the part a subscription had already covered.
/// It gets its own line rather than being folded into <see cref="MoneyUsd"/> or dropped.
/// </para>
/// <para>
/// Every count that a figure rests on travels with it — <see cref="ListUsdRecords"/> and
/// <see cref="ListUsdUnpricedN"/> beside <see cref="ListUsd"/>, and each records count beside its
/// dollars — because a money total offered without what it excluded is just a different wrong number.
/// </para>
/// </remarks>
public sealed record PhaseMoney
{
    /// <summary>The block for a phase in which nothing could be priced and nothing was billed.</summary>
    public static PhaseMoney None { get; } = new()
    {
        BillingModes = [],
        CostUsdByBillingMode = [],
        SpendByModel = [],
        PlanAllowanceByModel = [],
        ByMode = []
    };

    /// <summary>
    /// Priced runs per <c>billing_mode</c>, <c>unrecorded</c> for a run written before the field existed.
    /// </summary>
    /// <remarks>
    /// A record from before 2026-09-10 is admitted on its old terms and counted separately, so adding
    /// the field does not silently rewrite the past.
    /// </remarks>
    public required IReadOnlyList<KeyValuePair<string, int>> BillingModes { get; init; }

    /// <summary>Provider-reported dollars per billing mode; only <c>metered</c> is money.</summary>
    public required IReadOnlyList<KeyValuePair<string, decimal>> CostUsdByBillingMode { get; init; }

    /// <summary>Dollars a metered provider actually billed.</summary>
    public decimal MoneyUsd { get; init; }

    /// <summary>Priced runs whose models were all billed per token — the denominator of <see cref="MoneyUsd"/>.</summary>
    public int MoneyRecords { get; init; }

    /// <summary>Real money from windows that mixed billing modes, reported apart because it cannot be split.</summary>
    public decimal MixedUsd { get; init; }

    /// <summary>Priced runs in that state.</summary>
    public int MixedRecords { get; init; }

    /// <summary>Metered spend per model, attributed only where the window ran <b>one</b> model.</summary>
    /// <remarks>
    /// Splitting one window's dollars across several models by token share is arithmetic, not
    /// measurement — the same line drawn between a miss fixed by one run and one fixed by several.
    /// </remarks>
    public required IReadOnlyList<KeyValuePair<string, PhaseSpend>> SpendByModel { get; init; }

    /// <summary>Metered spend from windows that ran several models, so it belongs to no single one.</summary>
    public PhaseSpend SpendUnattributed { get; init; } = new(0m, 0);

    /// <summary>What every priced run's tokens would cost at the published rate — a price, never a bill.</summary>
    public decimal ListUsd { get; init; }

    /// <summary>Priced runs that produced a list price.</summary>
    public int ListUsdRecords { get; init; }

    /// <summary>
    /// Priced runs no rate card could price, left out rather than counted as free.
    /// </summary>
    /// <remarks>
    /// A model with no rate is the one case where silence is honest and a zero is a lie: counting it as
    /// free would make an unpriced phase look cheap.
    /// </remarks>
    public int ListUsdUnpricedN { get; init; }

    /// <summary>What a monthly plan's allowance was consumed by — a meter against a limit, not an invoice.</summary>
    public decimal PlanAllowanceUsd { get; init; }

    /// <summary>Priced runs on a plan.</summary>
    public int PlanAllowanceRecords { get; init; }

    /// <summary>Plan allowance per model; these are the numbers a per-model limit is read against.</summary>
    public required IReadOnlyList<KeyValuePair<string, decimal>> PlanAllowanceByModel { get; init; }

    /// <summary>
    /// The phase split by <c>mode</c>, in the order the modes first happened.
    /// </summary>
    /// <remarks>
    /// A phase run in named modes — a reset's sessions, a build's fresh and fix passes — is several
    /// different jobs under one <c>cmd</c>, and the split says which. Same denominators as the phase
    /// itself: a run with no computable window contributes no tokens, never a zero.
    /// </remarks>
    public required IReadOnlyList<KeyValuePair<string, PhaseModeSlice>> ByMode { get; init; }
}

/// <summary>Dollars and the number of records they were measured over.</summary>
/// <param name="Usd">The dollars.</param>
/// <param name="Records">Runs that contributed them — never inferred from the total.</param>
public sealed record PhaseSpend(decimal Usd, int Records);

/// <summary>
/// One <c>mode</c>'s slice of a phase (SCHEMA.md §2, REQ-FN-137).
/// </summary>
/// <param name="Runs">Runs in this mode.</param>
/// <param name="DurationSeconds">Their wall clock, read through the one duration rule.</param>
/// <param name="TokensOut">Output tokens over the runs with a usable window only.</param>
/// <param name="TokensUnmeasuredN">Runs excluded from that total because no window could be computed.</param>
/// <param name="FilesWritten">Files the mode's runs wrote.</param>
/// <param name="FirstStarted">When the mode first ran; the empty string when no run recorded a start.</param>
public sealed record PhaseModeSlice(
    int Runs,
    long DurationSeconds,
    long TokensOut,
    int TokensUnmeasuredN,
    long FilesWritten,
    string FirstStarted);

/// <summary>
/// The <c>billing_mode</c> vocabulary (SCHEMA.md §2.5b, added 2026-09-10).
/// </summary>
/// <remarks>
/// Five producer values plus <see cref="Unrecorded"/>, which is <b>not</b> one of them: it is what a
/// record written before the field existed reads as. Keeping it distinct is what stops the arrival of a
/// new field silently rewriting the past into one of its buckets.
/// </remarks>
public static class BillingModes
{
    /// <summary>A flat monthly fee; its marginal cost is a true zero and never appears as spend.</summary>
    public const string Subscription = "subscription";

    /// <summary>Billed per token. The only value whose dollars are money.</summary>
    public const string Metered = "metered";

    /// <summary>A monthly plan whose dollars are allowance consumed against a per-model limit.</summary>
    public const string Plan = "plan";

    /// <summary>A provider running on this machine, which has no bill at all.</summary>
    public const string Local = "local";

    /// <summary>One window that ran models of more than one mode — what a fallback looks like.</summary>
    public const string Mixed = "mixed";

    /// <summary>A record written before the field existed; never assumed into one of the five.</summary>
    public const string Unrecorded = "unrecorded";
}
