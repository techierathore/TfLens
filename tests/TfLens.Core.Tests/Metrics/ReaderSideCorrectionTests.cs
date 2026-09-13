using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The reader-side corrections of BRD-177 to BRD-181 — REQ-FN-110, REQ-FN-111, REQ-FN-112,
/// REQ-FN-113 and REQ-FN-114.
/// </summary>
/// <remarks>
/// Each of these was a real defect in the framework's own report before it was found there, and each
/// ran in the direction that flatters: a combined first-pass rate of 72% that was truly 48%, a reset's
/// total time of 16h49m that was truly 55h57m, and a first-pass rate of 0% on twelve verdicts that all
/// passed. The tests below are written as those three numbers, in miniature.
/// </remarks>
public sealed class ReaderSideCorrectionTests
{
    private const int UserId = 7;
    private const string Framework = "techieflow";
    private const string Alpha = "acme/alpha";

    /// <summary>
    /// Two projects each carrying a <c>REQ-UI-001</c> count as two requirements, not one — the whole of
    /// BRD-178 in one assertion.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-111 — a combined rate across two projects each carrying a REQ-UI-001 counts two requirements, not one")]
    public async Task TwoProjectsSharingAReqIdCountAsTwoRequirements()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-UI-001", aApp: "AlphaApp", aRepo: Alpha),
            GateFixtures.Gate(aReqId: "REQ-UI-001", aApp: "BetaWeb", aRepo: Alpha),
            GateFixtures.Gate(aReqId: "REQ-UI-002", aApp: "AlphaApp", aRepo: Alpha)
        ]);

        Assert.Equal(3, vAnalysis.Live["app"].ReqsScored);
    }

    /// <summary>
    /// One project's backfilled requirement does not taint another project's live requirement of the
    /// same id, and the tainted list names the project whose requirement was dropped.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TaintIsProjectScopedAndNamesItsProject()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-UI-001", aApp: "AlphaApp", aRepo: Alpha, aBackfilled: true),
            GateFixtures.Gate(aReqId: "REQ-UI-001", aApp: "AlphaApp", aRepo: Alpha),
            GateFixtures.Gate(aReqId: "REQ-UI-001", aApp: "BetaWeb", aRepo: Alpha),
            GateFixtures.Gate(aReqId: "REQ-UI-002", aApp: "BetaWeb", aRepo: Alpha),
            GateFixtures.Gate(aReqId: "REQ-UI-003", aApp: "BetaWeb", aRepo: Alpha)
        ]);

        var vLive = vAnalysis.Live["app"];

        Assert.Equal(["AlphaApp:REQ-UI-001"], vAnalysis.TaintedReqs);
        Assert.Equal(3, vLive.ReqsScored);
        Assert.Equal(1, vLive.ReqsExcludedBackfillTaint);
        Assert.Equal("100%", vLive.FirstPassRate.Display());
    }

    /// <summary>
    /// The escape rate keys on <c>(project, req_id)</c> too: two projects' <c>REQ-UI-001</c> are two
    /// failing requirements, one of which escaped.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task EscapeRateKeysOnProjectAndReqId()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-UI-001", aApp: "AlphaApp", aVerdict: "FAIL",
                aGate: MetricsConstants.Escaped),
            GateFixtures.Gate(aReqId: "REQ-UI-001", aApp: "BetaWeb", aVerdict: "FAIL", aGate: "build"),
            GateFixtures.Gate(aReqId: "REQ-UI-002", aApp: "BetaWeb", aVerdict: "FAIL", aGate: "build")
        ]);

        // Keyed on the bare id these would be two failing REQs and the rate would read 50%.
        Assert.Equal("33%", vAnalysis.Live["app"].EscapeRate.Display());
    }

    /// <summary>
    /// Verdicts graded <c>req_class: "FR"</c> land in their own segment, and no application figure
    /// counts a single one of them.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-110 — verdicts graded req_class FR take their own segment and no rate or total spans them and the application requirements together")]
    public async Task FrameworkRequirementsSegregateFromEveryApplicationFigure()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-UI-001", aReqClass: "UI"),
            GateFixtures.Gate(aReqId: "REQ-UI-002", aReqClass: "UI", aVerdict: "FAIL", aGate: "build"),
            GateFixtures.Gate(aReqId: "BRD-177", aReqClass: "FR"),
            GateFixtures.Gate(aReqId: "BRD-178", aReqClass: "FR"),
            GateFixtures.Gate(aReqId: "BRD-179", aReqClass: "FR")
        ]);

        var vApp = vAnalysis.Live["app"];
        var vFramework = vAnalysis.Live[MetricsConstants.FrameworkRequirements];

        Assert.Equal(2, vApp.Records);
        Assert.Equal(2, vApp.ReqsScored);
        Assert.Equal(3, vFramework.Records);
        Assert.Equal(3, vFramework.ReqsScored);
        Assert.Equal("100%", vFramework.FirstPassRate.Display());

        // There is no shape in which one figure spans both: the segment map has no "all" key.
        Assert.DoesNotContain("all", vAnalysis.Live.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", vAnalysis.Live.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gate records that omit <c>attempt</c> get one derived in stream order, so a set that all passed
    /// reports 100% rather than 0% — and the derived count is published beside the rate.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-113 — attempts derive in stream order for records that omit one, and a set that all passed reports 100% beside the derived count")]
    public async Task DerivedAttemptsTurnAZeroPercentRateIntoOneHundred()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-FN-001", aAttempt: null, aTs: "2026-08-01T01:00:00Z"),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aAttempt: null, aTs: "2026-08-01T02:00:00Z"),
            GateFixtures.Gate(aReqId: "REQ-FN-003", aAttempt: null, aTs: "2026-08-01T03:00:00Z")
        ]);

        var vLive = vAnalysis.Live["app"];

        Assert.Equal(3, vLive.AttemptsDerived);
        Assert.Equal(3, vLive.FirstPassN);
        Assert.Equal("100%", vLive.FirstPassRate.Display());
    }

    /// <summary>
    /// The second live verdict on the same requirement derives attempt 2, so it is not a first pass —
    /// and the same id in another project starts again at 1.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task DerivedAttemptsCountPriorRecordsPerProject()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-FN-001", aApp: "AlphaApp", aAttempt: null,
                aVerdict: "FAIL", aGate: "build", aTs: "2026-08-01T01:00:00Z"),
            GateFixtures.Gate(aReqId: "REQ-FN-001", aApp: "AlphaApp", aAttempt: null,
                aTs: "2026-08-01T02:00:00Z"),
            GateFixtures.Gate(aReqId: "REQ-FN-001", aApp: "BetaWeb", aAttempt: null,
                aTs: "2026-08-01T03:00:00Z"),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aApp: "BetaWeb", aAttempt: null,
                aTs: "2026-08-01T04:00:00Z")
        ]);

        var vLive = vAnalysis.Live["app"];

        // AlphaApp:REQ-FN-001 is on attempt 2 and failed its first, so only the two BetaWeb
        // requirements are first passes.
        Assert.Equal(3, vLive.ReqsScored);
        Assert.Equal(2, vLive.FirstPassN);
        Assert.Equal(4, vLive.AttemptsDerived);
    }

    /// <summary>A record that carries its own <c>attempt</c> is never altered, and never counted as derived.</summary>
    [Fact]
    public void CarriedAttemptIsNeverAltered()
    {
        var vDerived = GateAttempt.Derive([
            GateFixtures.Gate(aReqId: "REQ-FN-001", aAttempt: 4, aTs: "2026-08-01T01:00:00Z"),
            GateFixtures.Gate(aReqId: "REQ-FN-001", aAttempt: null, aTs: "2026-08-01T02:00:00Z")
        ]);

        Assert.Equal(1, vDerived.DerivedN);
        Assert.Equal(4, vDerived.Records[0].Attempt);
        Assert.False(vDerived.Records[0].AttemptDerived);
        Assert.Equal(2, vDerived.Records[1].Attempt);
        Assert.True(vDerived.Records[1].AttemptDerived);
    }

    /// <summary>A backfilled record neither receives a derived attempt nor advances the live numbering.</summary>
    [Fact]
    public void BackfilledRecordsAreOutsideTheAttemptDerivation()
    {
        var vDerived = GateAttempt.Derive([
            GateFixtures.Gate(aReqId: "REQ-FN-001", aAttempt: null, aBackfilled: true,
                aTs: "2026-08-01T01:00:00Z"),
            GateFixtures.Gate(aReqId: "REQ-FN-001", aAttempt: null, aTs: "2026-08-01T02:00:00Z")
        ]);

        Assert.Equal(1, vDerived.DerivedN);
        Assert.Null(vDerived.Records[0].Attempt);
        Assert.Equal(1, vDerived.Records[1].Attempt);
    }

    /// <summary>
    /// A run carrying <c>started</c> and <c>ended</c> but no <c>duration_s</c> is timed from its own two
    /// timestamps, and the phase says how many of its minutes were worked out.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-112 — a run that omits duration_s is timed from started and ended, with the derived count beside the time figure")]
    public async Task DurationIsDerivedFromStartedAndEnded()
    {
        var vAnalysis = await AnalyseRunsAsync([
            GateFixtures.Run(aDurationS: null, aStarted: "2026-08-01T09:00:00Z",
                aEnded: "2026-08-01T11:00:00Z"),
            GateFixtures.Run(aDurationS: 3600)
        ]);

        var vRow = Assert.Single(vAnalysis.Phases.Phases);

        Assert.Equal(2, vRow.Duration.TimedN);

        // 7200 from the record's own clocks + 3600 from a record whose clocks are absent. Under the bug
        // the first run was worth zero time while its tokens still counted — the phase's time would have
        // read 3600.
        Assert.Equal(10800, vRow.Duration.TotalSeconds);

        // DerivedN is 0, and that is the amended rule (BRD-179, 2026-09-10) rather than a regression.
        // Reading a record's own `started` and `ended` is now the NORMAL path for every run, so it is not
        // a derivation to be counted; what is left for `DerivedN` is the run whose window this read had
        // to close some other way — `ts` standing in for an absent `ended`, or a run that recorded no
        // elapsed time. Counting every timestamped run as derived would report the whole stream as
        // worked-out and make the figure meaningless.
        Assert.Equal(0, vRow.Duration.DerivedN);
        Assert.Equal(0, vAnalysis.Phases.DurationsDerivedN);
    }

    /// <summary><c>ts</c> stands in for an absent <c>ended</c>, which is SCHEMA.md's own definition.</summary>
    [Fact]
    public void TsStandsInForAnAbsentEnded()
    {
        var vDerived = RunDuration.Derive([
            GateFixtures.Run(aDurationS: null, aStarted: "2026-08-01T09:00:00Z",
                aTs: "2026-08-01T09:30:00Z")
        ]);

        Assert.Equal(1, vDerived.DerivedN);
        Assert.Equal(1800, vDerived.Records[0].DurationS);
        Assert.Equal(RunDuration.FromTs, vDerived.Records[0].DurationDerivedFrom);
    }

    /// <summary>A run with no <c>started</c>, or with an unreadable window, is left exactly as it arrived.</summary>
    [Fact]
    public void AnUnanswerableDurationIsLeftAlone()
    {
        var vDerived = RunDuration.Derive([
            GateFixtures.Run(aDurationS: null, aStarted: null, aEnded: "2026-08-01T11:00:00Z"),
            GateFixtures.Run(aDurationS: null, aStarted: "2026-08-01T11:00:00Z",
                aEnded: "2026-08-01T09:00:00Z"),
            GateFixtures.Run(aDurationS: null, aStarted: "not-a-timestamp", aEnded: "nor-is-this")
        ]);

        Assert.Equal(0, vDerived.DerivedN);
        Assert.All(vDerived.Records, aRun => Assert.Null(aRun.DurationS));
        Assert.All(vDerived.Records, aRun => Assert.Null(aRun.DurationDerivedFrom));
    }

    /// <summary>A backfilled run is never given a derived duration — a reconstructed one is a guess.</summary>
    [Fact]
    public void BackfilledRunsAreOutsideTheDurationDerivation()
    {
        var vDerived = RunDuration.Derive([
            GateFixtures.Run(aDurationS: null, aStarted: "2026-08-01T09:00:00Z",
                aEnded: "2026-08-01T11:00:00Z", aBackfilled: true)
        ]);

        Assert.Equal(0, vDerived.DerivedN);
        Assert.Null(vDerived.Records[0].DurationS);
    }

    /// <summary>
    /// A record naming a <c>cmd</c>, <c>verdict</c> or <c>harness</c> outside the documented vocabulary
    /// is rendered as it stands and counted — never filtered to a known list (BRD-181).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-114 — an unfamiliar cmd, verdict or harness value is shown as it stands and counted, never dropped or hidden")]
    public async Task UnfamiliarValuesAreShownAsTheyStandAndCounted()
    {
        var vAnalysis = await AnalyseAsync(
            [
                GateFixtures.Gate(aReqId: "REQ-FN-001", aVerdict: "NOT-OBSERVABLE", aGate: "build"),
                GateFixtures.Gate(aReqId: "REQ-FN-002", aVerdict: "PASS", aGate: "build"),
                GateFixtures.Gate(aReqId: "REQ-FN-003", aVerdict: "In Progress", aGate: "build")
            ],
            [GateFixtures.Run(aCmd: "rename-page")]);

        // Every off-list verdict is scored as a failure rather than dropped: an unrecognised verdict is
        // not a pass, and the record still exists.
        Assert.Equal(3, vAnalysis.Live["app"].GateDistributionN);

        // The unfamiliar cmd keeps its own row rather than being filtered out of the phase table.
        Assert.Equal(["rename-page"], vAnalysis.Phases.Phases.Select(aRow => aRow.Cmd));
    }

    /// <summary>Runs the engine over gate and run records seeded into one repository.</summary>
    /// <param name="aGates">The gate records to analyse.</param>
    /// <param name="aRuns">The run records to analyse.</param>
    /// <returns>The analysis.</returns>
    private static async Task<AnalysisResult> AnalyseAsync(
        IReadOnlyList<GateRecord> aGates,
        IReadOnlyList<RunRecord>? aRuns = null)
    {
        var vStore = new FixtureTelemetryStore().Seed(UserId, Alpha, Framework, aGates, aRuns);
        var vEngine = new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance);
        return await vEngine.AnalyseAsync(UserId, Framework);
    }

    /// <summary>Runs the engine over run records only.</summary>
    /// <param name="aRuns">The run records to analyse.</param>
    /// <returns>The analysis.</returns>
    private static Task<AnalysisResult> AnalyseRunsAsync(IReadOnlyList<RunRecord> aRuns) =>
        AnalyseAsync([], aRuns);
}
