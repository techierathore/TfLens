using System.Globalization;
using System.Text.Json;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The TechieFlow-axis phase-effort engine — <i>what did each phase cost</i>
/// (REQ-FN-089, REQ-FN-090, REQ-FN-092, REQ-FN-093; BRD-146, BRD-147, BRD-149, BRD-150, BRD-152).
/// </summary>
/// <remarks>
/// <para>
/// A field-for-field port of <c>analyse_phases()</c> in <c>.tfcore/telemetry/tf-metrics.sh</c>, the same
/// standing the rest of <see cref="MetricsEngine"/> has: <b>the reference script is the specification</b>
/// and a disagreement is a bug here, never there (BRD §13). It groups live <c>runs</c> records by
/// <c>cmd</c> for one <c>(UserId, Framework)</c> and reproduces the oracle's <c>phases</c> block
/// key-for-key. That block rides inside <c>tf-metrics.sh --report --json</c> / <c>--rollup --json</c>, so
/// the parity gate covers it with no new invocation.
/// </para>
/// <para>
/// <b>Three denominators shape the whole class, and each one is returned with its figure rather than
/// computed and discarded.</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Token figures exclude runs with no window.</b> A run carrying <c>tokens_scope: "none"</c> — or no
/// scope at all, or no <c>tokens_out</c> — has no usable window, so it leaves every token figure and is
/// counted in <c>tokens_unmeasured_n</c>. It is <b>never averaged in as zero</b>: that is <c>TF-005</c>,
/// the defect TfLens reported upstream on the miss stream, where <c>or 0</c> could not tell an absent
/// field from a measured zero and the error always ran in the direction that flattered the framework.
/// Every derived token figure therefore leaves this class inside a <see cref="TokenWindow"/>
/// (REQ-FN-089).
/// </description></item>
/// <item><description>
/// <b>Fan-out is a predicate, not a coalesce (ADR-026).</b> A run is observed only when
/// <c>tokens_scope == "tree"</c> <b>and</b> <c>subagent_runs</c> is not <c>null</c>. Everything else is
/// excluded and classified two ways because they are two different facts —
/// <c>unobserved_not_tree</c> (<i>we did not look</i>) and <c>unobserved_predates_field</c> (<i>we could
/// not have looked</i>). Both counts are published, and the figure leaves inside a
/// <see cref="FanoutObservation"/> so a page cannot bind a spawn count without the denominator
/// (REQ-FN-090).
/// </description></item>
/// <item><description>
/// <b>Dollars are per harness and never pooled.</b> Only OpenCode measures real spend; Claude Code and
/// Codex carry <c>null</c> permanently. Nothing here sums across harnesses and nothing here prices a
/// token from a rate card, because that would present an estimate as a measurement.
/// </description></item>
/// </list>
/// <para>
/// A fourth rule has no denominator but the same shape: <b>wherever a run carries
/// <see cref="RunRecord.ModelTokensOut"/>, the per-model band reads that split and never the dominant
/// <see cref="RunRecord.Model"/> label</b> (REQ-FN-092). A run that spent 90% of its output on one model
/// and a run that split evenly are different facts about cost and routing, and the label cannot tell them
/// apart, so reading it on a split-carrying run would file a mixed-model window whole under its winner.
/// </para>
/// <para>
/// There is deliberately <b>no per-REQ or per-feature figure</b> anywhere in this class. The unit of work
/// is the run: a <c>*build-phase</c> run touching eight REQs has one duration and one token window, and
/// dividing it eight ways is arithmetic dressed as measurement (SCHEMA.md §0). There is nothing to divide
/// from — neither producer emits a per-REQ timing field.
/// </para>
/// </remarks>
public static class PhaseMetrics
{
    /// <summary>The only <c>tokens_scope</c> whose window read the sub-agent transcripts.</summary>
    public const string TreeScope = "tree";

    /// <summary>The <c>tokens_scope</c> that says the window could not be computed.</summary>
    public const string NoneScope = "none";

    /// <summary>
    /// The scope key for a run that carries no <c>tokens_scope</c> at all.
    /// </summary>
    /// <remarks>
    /// The oracle spells this <c>absent</c> and keeps it distinct from <c>none</c> in
    /// <c>scope_coverage</c>, and the distinction is worth the extra key: <c>none</c> is the producer
    /// saying the window could not be computed, <c>absent</c> is a record from before the field existed.
    /// Both are excluded from every token figure and neither is a zero.
    /// </remarks>
    public const string AbsentScope = "absent";

    /// <summary>The <c>subagent_runs</c> wire name, as <see cref="MetricsConstants.FieldSince"/> keys it.</summary>
    public const string SubagentRunsField = "subagent_runs";

    /// <summary>The oracle's bucket for a run naming no <c>cmd</c> or no <c>harness</c>.</summary>
    public const string Unknown = "?";

    /// <summary>The oracle's bucket for a run naming no <c>mode</c> or <c>build_result</c>.</summary>
    public const string NotRecorded = "—";

    /// <summary>Decimal places the mean output-tokens-per-run figure is rounded to.</summary>
    private const int PerRunDigits = 1;

    /// <summary>Decimal places measured dollars are rounded to before they are reported.</summary>
    private const int UsdDigits = 6;

    /// <summary>
    /// Computes the whole phase-effort block for one user and framework.
    /// </summary>
    /// <remarks>
    /// Backfilled records are excluded before anything is grouped: <c>runs_live</c> is the oracle's own
    /// denominator, and a reconstructed duration is a guess — an effort report built on guesses is the
    /// kind of number that cannot be defended when someone asks how it was measured (SCHEMA.md §6).
    /// </remarks>
    /// <param name="aRuns">Every stored run record for the framework, live and backfilled.</param>
    /// <returns>The block; a framework with no live run returns <see cref="PhaseEffortAnalysis.Empty"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRuns"/> is <c>null</c>.</exception>
    public static PhaseEffortAnalysis Compute(IReadOnlyList<RunRecord> aRuns)
    {
        ArgumentNullException.ThrowIfNull(aRuns);

        var vLive = aRuns.Where(aRun => aRun.Backfilled != true).ToList();

        if (vLive.Count == 0)
        {
            return PhaseEffortAnalysis.Empty;
        }

        // The two page-level denominators. Each is read over the records that could support it — the
        // token total over runs with a usable window, the duration total over runs that were timed — so a
        // share can never be read against a denominator including records its numerator excluded.
        var vTokensOutTotal = vLive.Where(IsPriced).Sum(aRun => (long)aRun.TokensOut!.Value);
        var vDurationTotal = vLive.Where(IsTimed).Sum(aRun => (long)aRun.DurationS!.Value);

        var vRows = vLive
            .GroupBy(CmdOf, StringComparer.Ordinal)
            .Select(aGroup => RowFor(aGroup.Key, aGroup.ToList(), vTokensOutTotal, vDurationTotal))
            .OrderByDescending(aRow => aRow.Tokens.Out)
            .ThenBy(aRow => aRow.Cmd, StringComparer.Ordinal)
            .ToList();

        return new PhaseEffortAnalysis
        {
            RunsLive = vLive.Count,
            ScopeCoverage = ScopeCoverageOf(vLive),
            TokensOutTotal = vTokensOutTotal,
            DurationSecondsTotal = vDurationTotal,
            Phases = vRows
        };
    }

    /// <summary>
    /// Builds one phase's row.
    /// </summary>
    /// <param name="aCmd">The framework command the rows are grouped by.</param>
    /// <param name="aRuns">This phase's live runs.</param>
    /// <param name="aTokensOutTotal">Measured output tokens across every phase.</param>
    /// <param name="aDurationTotal">Timed wall clock across every phase.</param>
    /// <returns>The row.</returns>
    private static PhaseEffortRow RowFor(
        string aCmd,
        IReadOnlyList<RunRecord> aRuns,
        long aTokensOutTotal,
        long aDurationTotal)
    {
        // ---- the token window (REQ-FN-089). The partition is the whole point: `vPriced` is the divisor
        // of every token figure below, and the runs outside it are COUNTED rather than summed in as zeros.
        var vPriced = aRuns.Where(IsPriced).ToList();
        var vUnpriced = aRuns.Count - vPriced.Count;

        var vTokens = new PhaseTokens(
            vPriced.Sum(aRun => (long)(aRun.TokensIn ?? 0)),
            vPriced.Sum(aRun => (long)aRun.TokensOut!.Value),
            vPriced.Sum(aRun => (long)(aRun.TokensCacheRead ?? 0)),
            vPriced.Sum(aRun => (long)(aRun.TokensCacheWrite ?? 0)));

        var vTimed = aRuns.Where(IsTimed).ToList();
        var vSeconds = vTimed.Select(aRun => (double)aRun.DurationS!.Value).ToList();

        return new PhaseEffortRow
        {
            Cmd = aCmd,
            Runs = aRuns.Count,
            Duration = new PhaseDuration(
                vTimed.Sum(aRun => (long)aRun.DurationS!.Value),
                MetricsConstants.Median(vSeconds),
                vTimed.Count == 0 ? null : vTimed.Max(aRun => (long)aRun.DurationS!.Value),
                vTimed.Count),
            ShareOfDuration = MetricsConstants.Pct(
                vTimed.Sum(aRun => (long)aRun.DurationS!.Value),
                aDurationTotal),
            TokensMeasuredN = vPriced.Count,
            TokensUnmeasuredN = vUnpriced,
            Tokens = vTokens,
            TokensOutMedian = MetricsConstants.Median(
                vPriced.Select(aRun => (double)aRun.TokensOut!.Value)),
            TokensOutPerRun = PerRunOf(vTokens.Out, vPriced.Count, vUnpriced),
            ShareOfTokensOut = MetricsConstants.Pct(vTokens.Out, aTokensOutTotal),
            Models = ModelsOf(vPriced),
            Harnesses = ByKey(aRuns, aRun => aRun.Harness, Unknown),
            Modes = ByKey(aRuns, aRun => aRun.Mode, NotRecorded),
            BuildResults = ByKey(aRuns, aRun => aRun.BuildResult, NotRecorded),
            ReqsTouchedTotal = aRuns.Sum(aRun => aRun.ReqsCount ?? 0),
            FilesWrittenTotal = aRuns.Sum(aRun => aRun.FilesWritten ?? 0),
            SubagentsDeclared = DeclaredKindsOf(aRuns),
            Fanout = FanoutOf(aRuns),
            Routing = RoutingOf(aRuns),
            CostUsdByHarness = CostByHarnessOf(vPriced)
        };
    }

    /// <summary>
    /// Mean output tokens per <b>measured</b> run, wrapped in the counts it rests on (REQ-FN-089).
    /// </summary>
    /// <remarks>
    /// The divisor is the runs that carried a window, never the phase's run count. Dividing by the latter
    /// would report <c>*log-miss</c> costing half what it does on a repository where six of sixteen runs
    /// happen to be unmeasured — which is the exact shape today's framework data has. This is the one
    /// figure in the block the oracle floors at <see cref="MetricsConstants.MinN"/>, and it returns a real
    /// <c>null</c> there, so TfLens must refuse it in the same place.
    /// </remarks>
    /// <param name="aTokensOut">Output tokens summed over the measured runs.</param>
    /// <param name="aMeasuredN">Runs with a usable window.</param>
    /// <param name="aUnmeasuredN">Runs excluded because no window could be computed.</param>
    /// <returns>The figure and both counts; <c>insufficient data (n=…)</c> below the floor, never <c>0</c>.</returns>
    private static TokenWindow PerRunOf(long aTokensOut, int aMeasuredN, int aUnmeasuredN)
    {
        if (aMeasuredN < MetricsConstants.MinN)
        {
            return new TokenWindow(Figure.InsufficientData(aMeasuredN), aMeasuredN, aUnmeasuredN);
        }

        var vMean = Math.Round((double)aTokensOut / aMeasuredN, PerRunDigits, MidpointRounding.ToEven);

        return new TokenWindow(
            Figure.Value(vMean, aMeasuredN, vMean.ToString(CultureInfo.InvariantCulture)),
            aMeasuredN,
            aUnmeasuredN);
    }

    /// <summary>
    /// The fan-out block — the predicate, both exclusions, and the figures the observed runs support
    /// (REQ-FN-090, BRD-147, ADR-026).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run is observed only when its window was <c>tree</c> scope <b>and</b> it carries
    /// <c>subagent_runs</c>. Every other run is excluded and lands in exactly one of two counts, so the
    /// three always add up to the phase's run count:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>unobserved_not_tree</c> — the window was <c>main</c>, <c>conversation</c>, <c>none</c> or
    /// absent, so it never read the sub-agent transcripts: <b>we did not look</b>. This exclusion could be
    /// different tomorrow.
    /// </description></item>
    /// <item><description>
    /// <c>unobserved_predates_field</c> — tree scope, and still no count, which can only mean the record
    /// was written before the producer emitted the field on 2026-08-31
    /// (<see cref="MetricsConstants.FieldSince"/>): <b>we could not have looked</b>. This exclusion is
    /// permanent — no later sync can fill a field that did not exist — which is precisely why it is not
    /// pooled with the first.
    /// </description></item>
    /// </list>
    /// <para>
    /// Nothing in this method coalesces an absent <c>subagent_runs</c>, and nothing divides by the phase's
    /// run count: the divisor is <c>observed_n</c> and it leaves the method attached to the figure.
    /// </para>
    /// </remarks>
    /// <param name="aRuns">The phase's live runs.</param>
    /// <returns>The block; <see cref="FanoutObservation.NotObserved"/>'s shape when no run qualified.</returns>
    private static FanoutObservation FanoutOf(IReadOnlyList<RunRecord> aRuns)
    {
        var vObserved = aRuns.Where(aRun => IsTreeScope(aRun) && aRun.SubagentRuns is not null).ToList();
        var vNotTree = aRuns.Count(aRun => !IsTreeScope(aRun));
        var vPredates = aRuns.Count(aRun => IsTreeScope(aRun) && aRun.SubagentRuns is null);

        if (vObserved.Count == 0)
        {
            return FanoutObservation.NotObserved with
            {
                UnobservedNotTree = vNotTree,
                UnobservedPredatesField = vPredates
            };
        }

        var vSpawns = vObserved.Select(aRun => aRun.SubagentRuns!.Value).ToList();

        // The share's denominator is the OBSERVED runs' output, not the phase's. Reading it against the
        // phase total would understate it by exactly the coverage gap — a run whose transcripts were never
        // opened contributed output but could not have contributed sub-agent output.
        var vObservedOut = vObserved.Where(IsPriced).Sum(aRun => (long)aRun.TokensOut!.Value);
        var vSubagentOut = vObserved
            .Where(aRun => aRun.TokensOutSubagents.HasValue)
            .Sum(aRun => (long)aRun.TokensOutSubagents!.Value);

        return new FanoutObservation(
            MetricsConstants.Median(vSpawns.Select(aSpawn => (double)aSpawn)),
            vObserved.Count,
            vNotTree,
            vPredates)
        {
            SpawnsTotal = vSpawns.Sum(),
            SpawnsMax = vSpawns.Max(),
            RunsWithFanout = vSpawns.Count(aSpawn => aSpawn > 0),
            TokensOutSubagents = vSubagentOut,
            SubagentShareOfTokensOut = MetricsConstants.Pct(vSubagentOut, vObservedOut)
        };
    }

    /// <summary>
    /// The per-model split (REQ-FN-092, BRD-150).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Computed over the <b>measured</b> runs only, because a run with no token window has no output to
    /// attribute to anything. Where a run carries <c>model_tokens_out</c> that split is the only thing
    /// read, so a mixed-model window is never filed whole under its dominant label.
    /// </para>
    /// <para>
    /// A run carrying no split falls back to that label, as the oracle does, and the fallback is counted
    /// in <see cref="PhaseModelEffort.RunsFromLabel"/> so it is visible rather than blended away. On a
    /// stream written before <c>model_tokens_out</c> shipped on 2026-08-31 that is every run, which is
    /// exactly why the two provenances are counted apart.
    /// </para>
    /// </remarks>
    /// <param name="aPriced">The phase's runs that carry a usable token window.</param>
    /// <returns>One row per model observed, heaviest first.</returns>
    private static IReadOnlyList<PhaseModelEffort> ModelsOf(IReadOnlyList<RunRecord> aPriced)
    {
        var vTokens = new Dictionary<string, long>(StringComparer.Ordinal);
        var vFromSplit = new Dictionary<string, int>(StringComparer.Ordinal);
        var vFromLabel = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var vRun in aPriced)
        {
            if (vRun.ModelTokensOut is { Count: > 0 } vSplit)
            {
                foreach (var vEntry in vSplit)
                {
                    vTokens[vEntry.Key] = vTokens.GetValueOrDefault(vEntry.Key) + vEntry.Value;
                    vFromSplit[vEntry.Key] = vFromSplit.GetValueOrDefault(vEntry.Key) + 1;
                }
            }
            else if (!string.IsNullOrEmpty(vRun.Model))
            {
                vTokens[vRun.Model] = vTokens.GetValueOrDefault(vRun.Model) + vRun.TokensOut!.Value;
                vFromLabel[vRun.Model] = vFromLabel.GetValueOrDefault(vRun.Model) + 1;
            }
        }

        return vTokens
            .Select(aEntry => new PhaseModelEffort(
                aEntry.Key,
                vFromSplit.GetValueOrDefault(aEntry.Key) + vFromLabel.GetValueOrDefault(aEntry.Key),
                aEntry.Value)
            {
                RunsFromSplit = vFromSplit.GetValueOrDefault(aEntry.Key),
                RunsFromLabel = vFromLabel.GetValueOrDefault(aEntry.Key)
            })
            .OrderByDescending(aRow => aRow.TokensOut)
            .ThenBy(aRow => aRow.Model, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The sub-agent <i>kinds</i> the runs declared, counted per kind (BRD-149).
    /// </summary>
    /// <remarks>
    /// <c>subagents</c> is a JSON array an agent types into its own emit. It is a <b>self-report</b> of
    /// which kinds were invoked and carries no count when the same kind is spawned four times, which is
    /// exactly why the measured <c>subagent_runs</c> exists beside it and is authoritative where the two
    /// disagree. A malformed array is ignored rather than throwing: the stream is append-only and one bad
    /// row must not take the phase's other figures with it.
    /// </remarks>
    /// <param name="aRuns">The phase's live runs.</param>
    /// <returns>One entry per declared kind, most frequently named first.</returns>
    private static IReadOnlyList<KeyValuePair<string, int>> DeclaredKindsOf(IReadOnlyList<RunRecord> aRuns)
    {
        var vCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var vRun in aRuns)
        {
            if (string.IsNullOrWhiteSpace(vRun.Subagents))
            {
                continue;
            }

            try
            {
                using var vDocument = JsonDocument.Parse(vRun.Subagents);

                if (vDocument.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var vElement in vDocument.RootElement.EnumerateArray())
                {
                    if (vElement.ValueKind == JsonValueKind.String && vElement.GetString() is { } vKind
                        && !string.IsNullOrWhiteSpace(vKind))
                    {
                        vCounts[vKind] = vCounts.GetValueOrDefault(vKind) + 1;
                    }
                }
            }
            catch (JsonException)
            {
                // A row whose declared list will not parse has declared nothing readable. It is not an
                // error to report here — Coverage owns malformed-line reporting — and it must not become
                // a count.
            }
        }

        return vCounts
            .OrderByDescending(aEntry => aEntry.Value)
            .ThenBy(aEntry => aEntry.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Routing counts — observed, never enforced (SCHEMA.md §2.5).
    /// </summary>
    /// <remarks>
    /// <c>unknown</c> is its own count and is never folded into either side. A run that carried no
    /// <c>routed</c> flag did not route correctly and did not drift; it said nothing, and the page must
    /// not turn that silence into either verdict.
    /// </remarks>
    /// <param name="aRuns">The phase's live runs.</param>
    /// <returns>The counts.</returns>
    private static PhaseRouting RoutingOf(IReadOnlyList<RunRecord> aRuns) => new(
        aRuns.Count(aRun => aRun.Routed == true),
        aRuns.Count(aRun => aRun.Routed == false),
        aRuns.Count(aRun => aRun.Routed is null));

    /// <summary>
    /// Measured dollars per harness (SCHEMA.md §4).
    /// </summary>
    /// <remarks>
    /// Only measured runs that actually carry a <c>cost_usd</c> are counted, and the rows are never summed
    /// across harnesses: <c>cost_usd</c> is a measurement on OpenCode and is <c>null</c> on Claude Code
    /// and Codex permanently, so a cross-harness total would be a number describing nothing. No rate card
    /// is consulted anywhere in this class.
    /// </remarks>
    /// <param name="aPriced">The phase's runs that carry a usable token window.</param>
    /// <returns>One row per harness that measured something; a harness that measured nothing has no row.</returns>
    private static IReadOnlyList<PhaseHarnessCost> CostByHarnessOf(IReadOnlyList<RunRecord> aPriced) =>
        aPriced
            .Where(aRun => aRun.CostUsd.HasValue)
            .GroupBy(aRun => aRun.Harness ?? Unknown, StringComparer.Ordinal)
            .Select(aGroup => new PhaseHarnessCost(
                aGroup.Key,
                Math.Round(aGroup.Sum(aRun => aRun.CostUsd!.Value), UsdDigits, MidpointRounding.ToEven),
                aGroup.Count()))
            .OrderBy(aRow => aRow.Harness, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Live runs per <c>tokens_scope</c>, over the scopes actually observed.
    /// </summary>
    /// <remarks>
    /// A run carrying no scope is counted under <see cref="AbsentScope"/> rather than folded into
    /// <c>none</c>: the producer saying "the window could not be computed" and a record predating the
    /// field are two facts, and neither is a measurement of zero tokens. A scope nothing carried gets no
    /// key at all — a zero-filled row would read as a measured absence.
    /// </remarks>
    /// <param name="aRuns">Every live run.</param>
    /// <returns>The coverage counts, ordinally ordered by scope so the report order is stable.</returns>
    private static IReadOnlyList<KeyValuePair<string, int>> ScopeCoverageOf(IReadOnlyList<RunRecord> aRuns)
    {
        var vCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var vRun in aRuns)
        {
            var vScope = ScopeOf(vRun);
            vCounts[vScope] = vCounts.GetValueOrDefault(vScope) + 1;
        }

        return vCounts.OrderBy(aEntry => aEntry.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>The run's token scope, with the oracle's name for a record that carries none.</summary>
    /// <param name="aRun">The run.</param>
    /// <returns>The scope, or <see cref="AbsentScope"/>.</returns>
    private static string ScopeOf(RunRecord aRun) =>
        string.IsNullOrWhiteSpace(aRun.TokensScope) ? AbsentScope : aRun.TokensScope;

    /// <summary>
    /// Whether the run carries a token window a figure may be computed over (REQ-FN-089).
    /// </summary>
    /// <remarks>
    /// Three ways to fail and they are all the same fact — <b>no window</b>: the scope says <c>none</c>,
    /// there is no scope at all, or the scope is fine but no <c>tokens_out</c> was captured. Every one of
    /// them is excluded from the token figures and counted in <c>tokens_unmeasured_n</c>, and none of them
    /// is a zero.
    /// </remarks>
    /// <param name="aRun">The run.</param>
    /// <returns><c>true</c> when the run may enter a token figure.</returns>
    private static bool IsPriced(RunRecord aRun) =>
        !string.Equals(ScopeOf(aRun), NoneScope, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(ScopeOf(aRun), AbsentScope, StringComparison.Ordinal)
        && aRun.TokensOut.HasValue;

    /// <summary>
    /// Whether the run was timed.
    /// </summary>
    /// <remarks>
    /// A <c>duration_s</c> of zero leaves the duration figures exactly as the oracle leaves it: a run the
    /// clock could not separate is not a run that took no time, and admitting it would pull every median
    /// toward zero for the same reason an unmeasured token window would.
    /// </remarks>
    /// <param name="aRun">The run.</param>
    /// <returns><c>true</c> when the run carries a non-zero duration.</returns>
    private static bool IsTimed(RunRecord aRun) => aRun.DurationS is > 0;

    /// <summary>Whether the run's window read the sub-agent transcripts (ADR-026).</summary>
    /// <param name="aRun">The run.</param>
    /// <returns><c>true</c> when <c>tokens_scope</c> is <c>tree</c>.</returns>
    private static bool IsTreeScope(RunRecord aRun) =>
        string.Equals(aRun.TokensScope, TreeScope, StringComparison.OrdinalIgnoreCase);

    /// <summary>The phase a run belongs to; a run naming no command is its own honest bucket.</summary>
    /// <param name="aRun">The run.</param>
    /// <returns>The <c>cmd</c>, or <see cref="Unknown"/>.</returns>
    private static string CmdOf(RunRecord aRun) =>
        string.IsNullOrWhiteSpace(aRun.Cmd) ? Unknown : aRun.Cmd;

    /// <summary>
    /// Counts runs by an optional categorical field, bucketing an absent value under a stated name.
    /// </summary>
    /// <remarks>
    /// Unlike a share's denominator, these are <i>counts of runs</i> and every run belongs somewhere: a
    /// run that named no harness is still a run, and dropping it would make the column fail to add up to
    /// the phase's run count. The bucket name says so on its face, and it differs by field only because
    /// the oracle's does.
    /// </remarks>
    /// <param name="aRuns">The phase's live runs.</param>
    /// <param name="aValueOf">Reads the field.</param>
    /// <param name="aAbsent">The bucket a run carrying no value falls into.</param>
    /// <returns>One entry per observed value, ordinally ordered by key as the oracle orders them.</returns>
    private static IReadOnlyList<KeyValuePair<string, int>> ByKey(
        IReadOnlyList<RunRecord> aRuns,
        Func<RunRecord, string?> aValueOf,
        string aAbsent)
    {
        var vCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var vRun in aRuns)
        {
            var vValue = aValueOf(vRun);
            var vKey = string.IsNullOrWhiteSpace(vValue) ? aAbsent : vValue;
            vCounts[vKey] = vCounts.GetValueOrDefault(vKey) + 1;
        }

        return vCounts.OrderBy(aEntry => aEntry.Key, StringComparer.Ordinal).ToList();
    }
}
