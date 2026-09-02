using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The phase-effort engine and its three denominators
/// (REQ-FN-089, REQ-FN-090, REQ-FN-092, REQ-FN-093; BRD-146, BRD-147, BRD-149, BRD-150, BRD-152).
/// </summary>
/// <remarks>
/// <para>
/// Every test here is written for a defect that produces a <b>plausible</b> number rather than an
/// obviously wrong one, which is what makes the class worth testing at all:
/// </para>
/// <list type="bullet">
/// <item><description>
/// An unmeasured token window averaged in as zero halves a phase's apparent cost and nothing looks
/// broken. That is <c>TF-005</c>, and the error always runs in the direction that flatters the framework.
/// </description></item>
/// <item><description>
/// An absent <c>subagent_runs</c> coalesced to <c>0</c> reports "this phase spawns no sub-agents" when
/// the truth is "the window never read the transcripts" (ADR-026).
/// </description></item>
/// <item><description>
/// A per-model band built on the dominant <c>model</c> label files a mixed-model window whole under its
/// winner, and the resulting ranking drives a routing decision on a fact that was never measured
/// (BRD-150).
/// </description></item>
/// </list>
/// </remarks>
public sealed class PhaseMetricsTests
{
    /// <summary>A timestamp after the three SCHEMA §2.6 fields entered the stream.</summary>
    private const string AfterFieldSince = "2026-09-01T10:00:00Z";

    /// <summary>A timestamp before they did, so the run could not have carried them.</summary>
    private const string BeforeFieldSince = "2026-08-30T10:00:00Z";

    /// <summary>
    /// A run with no token window leaves every token figure, is counted, and never drags the mean down.
    /// </summary>
    /// <remarks>
    /// Three measured runs of 100, 200 and 300 output tokens mean 200. Averaging the fourth,
    /// <c>tokens_scope: "none"</c> run in as a zero would give 150 — a number that looks perfectly
    /// reasonable and is 25% low. That is the whole defect.
    /// </remarks>
    [Fact]
    public void ARunWithNoWindowIsExcludedAndCountedRatherThanAveragedAsZero()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 100),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 200),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 300),
            Run(aCmd: "build-phase", aScope: "none")
        ]);

        var vRow = vResult.Phases.Single();

        vRow.Runs.Should().Be(4);
        vRow.TokensMeasuredN.Should().Be(3, "three of the four runs carried a usable window");
        vRow.TokensUnmeasuredN.Should().Be(1, "the excluded run is counted, never dropped silently");
        vRow.Tokens.Out.Should().Be(600);

        vRow.TokensOutPerRun.MeasuredN.Should().Be(3);
        vRow.TokensOutPerRun.UnmeasuredN.Should().Be(1);
        vRow.TokensOutPerRun.IsFullyMeasured.Should().BeFalse();

        vRow.TokensOutPerRun.Tokens.TryGetValue(out var vMean).Should().BeTrue();
        vMean.Should().Be(200d, "the divisor is the three measured runs, not all four");
        vMean.Should().NotBe(150d, "averaging the unmeasured run in as zero is TF-005");

        vResult.TokensOutTotal.Should().Be(600, "the share denominator excludes it too");
    }

    /// <summary>
    /// A run with no <c>tokens_scope</c> at all is the same fact as <c>none</c> — no window.
    /// </summary>
    [Fact]
    public void ARunWithNoScopeAtAllIsAlsoUnmeasured()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "verify-phase", aScope: "tree", aTokensOut: 400),
            Run(aCmd: "verify-phase", aScope: null, aTokensOut: 999)
        ]);

        var vRow = vResult.Phases.Single();

        vRow.TokensMeasuredN.Should().Be(1);
        vRow.TokensUnmeasuredN.Should().Be(1);
        vRow.Tokens.Out.Should().Be(400, "a scopeless run's token numbers are not a measurement");
    }

    /// <summary>
    /// Below <c>MIN_N = 3</c> the mean refuses to be a number, and is never <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The oracle returns a real <c>null</c> here and BRD §13 treats a <c>0</c> on either side as a
    /// mismatch rather than a rounding difference, so the refusal has to survive as a refusal.
    /// </remarks>
    [Fact]
    public void TokensOutPerRunRefusesBelowTheMinimumN()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "log-miss", aScope: "main", aTokensOut: 100),
            Run(aCmd: "log-miss", aScope: "main", aTokensOut: 200)
        ]);

        var vFigure = vResult.Phases.Single().TokensOutPerRun.Tokens;

        vFigure.Kind.Should().Be(FigureKind.InsufficientData);
        vFigure.TryGetValue(out _).Should().BeFalse("there is no number to read out");
        vFigure.Display().Should().Be("insufficient data (n=2)");
        vFigure.Display().Should().NotBe("0");
    }

    /// <summary>
    /// A <c>main</c>-scope run with no <c>subagent_runs</c> is <b>unobserved</b>, not a measured zero.
    /// </summary>
    /// <remarks>
    /// It lands in <c>unobserved_not_tree</c> — <i>we did not look</i> — and it neither joins the spawn
    /// total nor the observed denominator. Coalescing it to <c>0</c> would halve the median and report a
    /// confident fan-out figure largely composed of runs that could not have seen a sub-agent.
    /// </remarks>
    [Fact]
    public void AMainScopeRunLandsInUnobservedNotTreeRatherThanCountingAsZeroSpawns()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "tree", aTokensOut: 1000, aSubagentRuns: 2),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 500),
            Run(aCmd: "build-phase", aScope: "conversation", aTokensOut: 500)
        ]);

        var vFanout = vResult.Phases.Single().Fanout;

        vFanout.ObservedN.Should().Be(1, "only the tree-scope run carrying the field could be observed");
        vFanout.UnobservedNotTree.Should().Be(2, "we did not look at those two windows");
        vFanout.UnobservedPredatesField.Should().Be(0);
        vFanout.UnobservedN.Should().Be(2);
        vFanout.SpawnsTotal.Should().Be(2, "the two unobserved runs contribute no spawns, not zero spawns");
        vFanout.RunsWithFanout.Should().Be(1);
        vFanout.IsObserved.Should().BeTrue();
    }

    /// <summary>
    /// A tree-scope run written before 2026-08-31 lands in <c>unobserved_predates_field</c>.
    /// </summary>
    /// <remarks>
    /// The two exclusions stay two counts because they are two facts with different futures: this one is
    /// permanent — no later sync can fill a field the producer had not shipped — whereas a <c>main</c>
    /// window could be a <c>tree</c> window tomorrow.
    /// </remarks>
    [Fact]
    public void ATreeScopeRunPredatingTheFieldIsItsOwnExclusion()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "tree", aTokensOut: 100, aTs: BeforeFieldSince),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 100, aTs: BeforeFieldSince)
        ]);

        var vFanout = vResult.Phases.Single().Fanout;

        vFanout.ObservedN.Should().Be(0);
        vFanout.UnobservedPredatesField.Should().Be(1, "tree scope, but the field did not exist yet");
        vFanout.UnobservedNotTree.Should().Be(1, "the main-scope run is the other kind of exclusion");
        vFanout.IsObserved.Should().BeFalse("a page must render \"not observed\", never \"0 subagents\"");
        vFanout.Spawns.Should().BeNull("nothing was observed, so there is no median — and it is not 0");
        vFanout.SpawnsMax.Should().BeNull();
        vFanout.SubagentShareOfTokensOut.Should().Be("—");
    }

    /// <summary>
    /// The fan-out share is read against the <b>observed</b> runs' output, not the phase's.
    /// </summary>
    [Fact]
    public void TheSubagentShareIsReadAgainstTheObservedRunsOutput()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "tree", aTokensOut: 20000, aSubagentRuns: 2, aSubagentTokensOut: 10000),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 135700)
        ]);

        var vFanout = vResult.Phases.Single().Fanout;

        vFanout.TokensOutSubagents.Should().Be(10000);
        vFanout.SubagentShareOfTokensOut.Should().Be(
            "50%",
            "the denominator is the observed run's 20000 output tokens, not the phase's 155700");
    }

    /// <summary>
    /// A mixed-model run splits across both models, and neither receives the whole window (BRD-150).
    /// </summary>
    /// <remarks>
    /// The run's dominant <c>model</c> label names one of the two. A band built on that label would file
    /// all 1000 output tokens under it, which is a different — and unmeasured — claim about cost and
    /// routing than the 900/100 split the producer actually emitted.
    /// </remarks>
    [Fact]
    public void PerModelEffortComesFromTheSplitAndNeverFromTheDominantLabel()
    {
        var vResult = PhaseMetrics.Compute([
            Run(
                aCmd: "build-phase",
                aScope: "tree",
                aTokensOut: 1000,
                aModel: "claude-opus-5",
                aModelTokensOut: new Dictionary<string, long>
                {
                    ["claude-opus-5"] = 900,
                    ["gpt-5.6-sol"] = 100
                })
        ]);

        var vModels = vResult.Phases.Single().Models;

        vModels.Should().HaveCount(2);
        vModels[0].Model.Should().Be("claude-opus-5");
        vModels[0].TokensOut.Should().Be(900);
        vModels[0].RunsFromSplit.Should().Be(1);
        vModels[0].RunsFromLabel.Should().Be(0, "the split was read, so the label was never consulted");
        vModels[1].Model.Should().Be("gpt-5.6-sol");
        vModels[1].TokensOut.Should().Be(100);
        vModels.Should().NotContain(
            aRow => aRow.TokensOut == 1000,
            "no model may receive the whole window of a run it only partly produced");
    }

    /// <summary>
    /// A run carrying no split falls back to its label, and the weaker provenance is counted apart.
    /// </summary>
    /// <remarks>
    /// The oracle does exactly this and BRD §13 is key-for-key, so diverging would fail parity on every
    /// record written before <c>model_tokens_out</c> shipped — which is most of them. What BRD-150
    /// forbids is reading the label on a run that <i>does</i> carry a split, and that cannot happen. The
    /// fallback is nonetheless a weaker observation, so it is counted in <c>RunsFromLabel</c> rather than
    /// blended invisibly into the row.
    /// </remarks>
    [Fact]
    public void ARunWithNoSplitFallsBackToItsLabelAndIsCountedApart()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 5000, aModel: "claude-opus-5")
        ]);

        var vRow = vResult.Phases.Single().Models.Single();

        vRow.Model.Should().Be("claude-opus-5");
        vRow.TokensOut.Should().Be(5000);
        vRow.RunsFromSplit.Should().Be(0);
        vRow.RunsFromLabel.Should().Be(1, "the row rests on a label, and a surface can say so");
    }

    /// <summary>
    /// A run with no token window contributes to no model row, whatever it declares.
    /// </summary>
    [Fact]
    public void AnUnmeasuredRunContributesToNoModelRow()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "none", aModel: "claude-opus-5")
        ]);

        vResult.Phases.Single().Models.Should().BeEmpty(
            "a run with no window has no output to attribute to any model");
    }

    /// <summary>
    /// The spawn median carries no minimum-n floor, because the oracle's phase block applies none.
    /// </summary>
    /// <remarks>
    /// This is the one place the phase block deliberately differs from the pooled block, which does floor
    /// its medians. A <see cref="Figure"/> here would refuse a number the oracle prints, and BRD §13
    /// treats that as a mismatch exactly as it treats printing one the oracle refuses. What makes the
    /// single-run median readable is <c>observed_n</c>, which travels with it.
    /// </remarks>
    [Fact]
    public void TheSpawnMedianIsNotFlooredBecauseTheOracleDoesNotFloorIt()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "tree", aTokensOut: 100, aSubagentRuns: 2)
        ]);

        var vFanout = vResult.Phases.Single().Fanout;

        vFanout.ObservedN.Should().Be(1);
        vFanout.Spawns.Should().Be(2d);
        vFanout.SpawnsMax.Should().Be(2);
    }

    /// <summary>
    /// Shares round-trip as the oracle's own <c>pct()</c> strings, em dash included.
    /// </summary>
    /// <remarks>
    /// BRD-152 diffs these as strings and forbids reformatting before the compare, so the engine has to
    /// produce the string rather than a float a renderer later formats.
    /// </remarks>
    [Fact]
    public void SharesAreTheOraclesOwnPercentStrings()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 870, aDurationS: 810),
            Run(aCmd: "verify-phase", aScope: "main", aTokensOut: 130, aDurationS: 190)
        ]);

        var vBuild = vResult.Phases.Single(aRow => aRow.Cmd == "build-phase");
        var vVerify = vResult.Phases.Single(aRow => aRow.Cmd == "verify-phase");

        vBuild.ShareOfTokensOut.Should().Be("87%");
        vVerify.ShareOfTokensOut.Should().Be("13%");
        vBuild.ShareOfDuration.Should().Be("81%");
        vVerify.ShareOfDuration.Should().Be("19%");
    }

    /// <summary>A share with nothing behind it is an em dash, never <c>0%</c>.</summary>
    [Fact]
    public void AShareWithNoDenominatorIsAnEmDash()
    {
        var vResult = PhaseMetrics.Compute([Run(aCmd: "refresh-status", aScope: "none")]);

        var vRow = vResult.Phases.Single();

        vRow.ShareOfTokensOut.Should().Be("—");
        vRow.ShareOfDuration.Should().Be("—");
        vRow.TokensOutMedian.Should().BeNull("nothing was measured, so there is no median");
        vRow.Duration.MedianSeconds.Should().BeNull("no run was timed");
        vRow.Duration.MaxSeconds.Should().BeNull("a max over nothing is null, never 0");
    }

    /// <summary>
    /// The declared sub-agent kinds and the measured spawn count are both reported, never reconciled.
    /// </summary>
    /// <remarks>
    /// <c>subagents</c> is typed by the agent and says which kinds were invoked; <c>subagent_runs</c> is
    /// counted from the harness store and says how many actually ran. The gap is a finding about
    /// self-report accuracy, and the measured figure is the authoritative one (BRD-149).
    /// </remarks>
    [Fact]
    public void DeclaredKindsAndMeasuredSpawnsAreBothReported()
    {
        var vResult = PhaseMetrics.Compute([
            Run(
                aCmd: "build-phase",
                aScope: "tree",
                aTokensOut: 100,
                aSubagentRuns: 2,
                aSubagents: "[\"tf-builder\",\"tf-builder\",\"tf-builder\",\"general-purpose\"]")
        ]);

        var vRow = vResult.Phases.Single();

        vRow.SubagentsDeclared.Should().BeEquivalentTo(new[]
        {
            new KeyValuePair<string, int>("tf-builder", 3),
            new KeyValuePair<string, int>("general-purpose", 1)
        });

        vRow.Fanout.SpawnsTotal.Should().Be(2, "the harness counted two, whatever the emit declared");
        vRow.DeclaredDiffersFromMeasured.Should().BeTrue(
            "the gap is displayed as a finding, not reconciled away");
    }

    /// <summary>Backfilled runs never enter the block; <c>runs_live</c> is the oracle's denominator.</summary>
    [Fact]
    public void BackfilledRunsAreNotCounted()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 100),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 999, aBackfilled: true)
        ]);

        vResult.RunsLive.Should().Be(1);
        vResult.Phases.Single().Tokens.Out.Should().Be(100);
    }

    /// <summary>
    /// Scope coverage reports the scopes observed, and a scopeless run is <c>absent</c>, not <c>none</c>.
    /// </summary>
    /// <remarks>
    /// A scope nothing carried gets no key, because a zero-filled row reads as a measured absence. And
    /// <c>absent</c> is kept apart from <c>none</c>: the producer saying "the window could not be
    /// computed" and a record predating the field are two facts, and neither is zero tokens.
    /// </remarks>
    [Fact]
    public void ScopeCoverageReportsOnlyTheScopesObserved()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "tree", aTokensOut: 10),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 10),
            Run(aCmd: "build-phase", aScope: null)
        ]);

        vResult.ScopeCoverage.Should().BeEquivalentTo(new[]
        {
            new KeyValuePair<string, int>("tree", 1),
            new KeyValuePair<string, int>("main", 1),
            new KeyValuePair<string, int>("absent", 1)
        });
    }

    /// <summary>
    /// Routing counts are three buckets; a run carrying no flag is <c>unknown</c>, not routed and not
    /// drifted.
    /// </summary>
    [Fact]
    public void RoutingKeepsUnknownAsItsOwnCount()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 1, aRouted: true),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 1, aRouted: false),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 1, aRouted: false),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 1)
        ]);

        vResult.Phases.Single().Routing.Should().Be(new PhaseRouting(1, 2, 1));
    }

    /// <summary>
    /// Measured dollars are reported per harness and a harness that measured nothing has no row.
    /// </summary>
    [Fact]
    public void DollarsAreMeasuredPerHarnessAndNeverPooled()
    {
        var vResult = PhaseMetrics.Compute([
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 1, aHarness: "opencode", aCostUsd: 0.230819m),
            Run(aCmd: "build-phase", aScope: "main", aTokensOut: 1, aHarness: "claude-code")
        ]);

        var vCosts = vResult.Phases.Single().CostUsdByHarness;

        vCosts.Should().ContainSingle();
        vCosts[0].Should().Be(new PhaseHarnessCost("opencode", 0.230819m, 1));
    }

    /// <summary>An empty stream returns the empty block rather than an absence.</summary>
    [Fact]
    public void AnEmptyStreamReturnsTheEmptyBlock()
    {
        PhaseMetrics.Compute([]).Should().BeSameAs(PhaseEffortAnalysis.Empty);
    }

    /// <summary>
    /// Builds one run record, every optional defaulting to <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Nothing is filled in with a plausible value: the figures under test all turn on the difference
    /// between an absent field and a measured zero, so a fixture that guessed would hide the defect.
    /// </remarks>
    /// <param name="aCmd">The framework command the row is grouped by.</param>
    /// <param name="aScope">The <c>tokens_scope</c>; <c>null</c> and <c>none</c> both mean no window.</param>
    /// <param name="aTokensOut">Output tokens, or <c>null</c> for not captured.</param>
    /// <param name="aDurationS">Wall-clock seconds, or <c>null</c> for not timed.</param>
    /// <param name="aSubagentRuns">Measured sub-agent invocations; <c>null</c> is <b>not observed</b>.</param>
    /// <param name="aSubagentTokensOut">Output tokens the sub-agents consumed.</param>
    /// <param name="aSubagents">The declared kinds, as the JSON array the emitter writes.</param>
    /// <param name="aModel">The dominant model label — never a source of a per-model figure.</param>
    /// <param name="aModelTokensOut">The per-model output split.</param>
    /// <param name="aHarness">The detected harness.</param>
    /// <param name="aCostUsd">Measured spend; only ever non-null on OpenCode.</param>
    /// <param name="aRouted">The routing flag; <c>null</c> means the run said nothing.</param>
    /// <param name="aBackfilled">Whether the record was reconstructed rather than emitted live.</param>
    /// <param name="aTs">The record timestamp, which the field-since floor is read against.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(
        string? aCmd = null,
        string? aScope = null,
        int? aTokensOut = null,
        int? aDurationS = null,
        int? aSubagentRuns = null,
        int? aSubagentTokensOut = null,
        string? aSubagents = null,
        string? aModel = null,
        IReadOnlyDictionary<string, long>? aModelTokensOut = null,
        string? aHarness = null,
        decimal? aCostUsd = null,
        bool? aRouted = null,
        bool? aBackfilled = null,
        string aTs = AfterFieldSince) => new()
    {
        UserId = 7,
        Repo = "acme/alpha",
        SourceSha = "fixture",
        Ts = aTs,
        Cmd = aCmd,
        TokensScope = aScope,
        TokensOut = aTokensOut,
        DurationS = aDurationS,
        SubagentRuns = aSubagentRuns,
        TokensOutSubagents = aSubagentTokensOut,
        Subagents = aSubagents,
        Model = aModel,
        ModelTokensOut = aModelTokensOut,
        Harness = aHarness,
        CostUsd = aCostUsd,
        Routed = aRouted,
        Backfilled = aBackfilled
    };
}
