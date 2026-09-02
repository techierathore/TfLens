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
/// A phase's wall-clock block — total, median, max and the count of runs that were timed.
/// </summary>
/// <param name="TotalSeconds">Summed duration over the timed runs.</param>
/// <param name="MedianSeconds">The median, or <c>null</c> when no run was timed.</param>
/// <param name="MaxSeconds">The longest timed run, or <c>null</c> when no run was timed.</param>
/// <param name="TimedN">Runs carrying a non-zero <c>duration_s</c>; the block's own denominator.</param>
public sealed record PhaseDuration(long TotalSeconds, double? MedianSeconds, long? MaxSeconds, int TimedN)
{
    /// <summary>The block for a phase in which nothing was timed.</summary>
    public static PhaseDuration None { get; } = new(0, null, null, 0);
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

    /// <summary>One row per <c>cmd</c>, heaviest measured output first.</summary>
    public required IReadOnlyList<PhaseEffortRow> Phases { get; init; }

    /// <summary>Runs that could be fan-out observed, across every phase — a coverage figure.</summary>
    /// <remarks>
    /// Read as <c>n of <see cref="RunsLive"/></c>. On a framework whose records mostly predate
    /// 2026-08-31 this reads <c>1 of 13</c>, which is the honest headline rather than a reason to hide it:
    /// a page that only looks right once the data is dense is a page nobody trusts in the meantime.
    /// </remarks>
    public int FanoutObservedN => Phases.Sum(aRow => aRow.Fanout.ObservedN);
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
}
