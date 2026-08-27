using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The provenance rules the engine enforces in its shape — REQ-FN-047, REQ-FN-048, REQ-FN-049,
/// REQ-FN-050, REQ-FN-055 and REQ-NFR-009.
/// </summary>
public sealed class ProvenanceRuleTests
{
    private const int UserId = 7;
    private const string Framework = "techieflow";

    /// <summary>A REQ carrying a backfilled record leaves the live numerator, leaves the live denominator, and is listed.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TaintedReqLeavesBothSidesOfTheLiveFirstPassRateAndIsListed()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-FN-001"),
            GateFixtures.Gate(aReqId: "REQ-FN-002"),
            GateFixtures.Gate(aReqId: "REQ-FN-003"),
            GateFixtures.Gate(aReqId: "REQ-FN-004"),
            GateFixtures.Gate(aReqId: "REQ-FN-004", aBackfilled: true)
        ]);

        var vLive = vAnalysis.Live["app"];

        Assert.Equal(3, vLive.ReqsScored);
        Assert.Equal(3, vLive.FirstPassN);
        Assert.Equal(1, vLive.ReqsExcludedBackfillTaint);
        Assert.Equal(["REQ-FN-004"], vAnalysis.TaintedReqs);
        Assert.Equal("100%", vLive.FirstPassRate.Display());
    }

    /// <summary>The live and backfilled figures for the same project type are two separate blocks and are never summed.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task LiveAndBackfilledNeverPool()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-FN-001"),
            GateFixtures.Gate(aReqId: "REQ-FN-002"),
            GateFixtures.Gate(aReqId: "REQ-FN-003"),
            GateFixtures.Gate(aReqId: "REQ-FN-010", aBackfilled: true),
            GateFixtures.Gate(aReqId: "REQ-FN-011", aBackfilled: true),
            GateFixtures.Gate(aReqId: "REQ-FN-012", aBackfilled: true)
        ]);

        Assert.Equal(3, vAnalysis.Live["app"].Records);
        Assert.Equal(3, vAnalysis.Backfilled["app"].Records);
        Assert.DoesNotContain("total", vAnalysis.Live.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", vAnalysis.Backfilled.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An inferred project type lands under <c>unclassified</c> and never joins the declared <c>app</c> segment.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task InferredProjectTypeLandsInUnclassified()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-FN-001"),
            GateFixtures.Gate(aReqId: "REQ-UI-001", aProjectTypeInferred: true)
        ]);

        Assert.Equal(1, vAnalysis.Live["app"].Records);
        Assert.Equal(1, vAnalysis.Live[MetricsConstants.Unclassified].Records);
        Assert.Equal(["app", MetricsConstants.Unclassified], vAnalysis.ProjectTypes);
    }

    /// <summary>A segment with two supporting REQs answers <c>insufficient data (n=2)</c> and carries no number at all.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TwoRecordSegmentYieldsInsufficientDataNotANumber()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-FN-001"),
            GateFixtures.Gate(aReqId: "REQ-FN-002")
        ]);

        var vRate = vAnalysis.Live["app"].FirstPassRate;

        Assert.Equal(FigureKind.InsufficientData, vRate.Kind);
        Assert.False(vRate.TryGetValue(out _));
        Assert.Equal("insufficient data (n=2)", vRate.Display());
    }

    /// <summary>Records on another framework are invisible to the analysis; no figure can span the two axes.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task FrameworksNeverPool()
    {
        var vStore = new FixtureTelemetryStore()
            .Seed(UserId, "acme/alpha", "techieflow", [
                GateFixtures.Gate(aReqId: "REQ-FN-001"),
                GateFixtures.Gate(aReqId: "REQ-FN-002"),
                GateFixtures.Gate(aReqId: "REQ-FN-003")
            ])
            .Seed(UserId, "acme/play", "playbook", [
                GateFixtures.Gate(aReqId: "REQ-FN-900", aRepo: "acme/play"),
                GateFixtures.Gate(aReqId: "REQ-FN-901", aRepo: "acme/play")
            ]);

        var vEngine = new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance);
        var vTechieFlow = await vEngine.AnalyseAsync(UserId, "techieflow");
        var vPlaybook = await vEngine.AnalyseAsync(UserId, "playbook");

        Assert.Equal(3, vTechieFlow.Live["app"].Records);
        Assert.Equal(2, vPlaybook.Live["app"].Records);
        Assert.Single(vTechieFlow.PerRepo);
        Assert.Single(vPlaybook.PerRepo);
    }

    /// <summary>Escape rate counts REQs no gate caught over REQs with any failure, taint included.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task EscapeRateCountsEscapedReqsOverFailingReqs()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-FN-001", aVerdict: "FAIL", aGate: MetricsConstants.Escaped),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aVerdict: "FAIL", aGate: "build"),
            GateFixtures.Gate(aReqId: "REQ-FN-003", aVerdict: "FAIL", aGate: "render"),
            GateFixtures.Gate(aReqId: "REQ-FN-004", aVerdict: "Verified")
        ]);

        Assert.Equal("33%", vAnalysis.Live["app"].EscapeRate.Display());
    }

    /// <summary>A failure naming no gate is counted as <c>unattributed</c> rather than dropped.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task FailureWithNoGateIsUnattributed()
    {
        var vAnalysis = await AnalyseAsync([
            GateFixtures.Gate(aReqId: "REQ-FN-001", aVerdict: "Blocked"),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aVerdict: "FAIL", aGate: "build"),
            GateFixtures.Gate(aReqId: "REQ-FN-003", aVerdict: "FAIL", aGate: "build")
        ]);

        var vRows = vAnalysis.Live["app"].GateDistribution;

        Assert.Equal(["build", MetricsConstants.Unattributed], vRows.Select(aRow => aRow.Gate));
        Assert.Equal([2, 1], vRows.Select(aRow => aRow.Count));
        Assert.Null(vAnalysis.Live["app"].GateDistributionNote);
    }

    /// <summary>Runs the engine over gate records seeded into one repository.</summary>
    /// <param name="aGates">The records to analyse.</param>
    /// <returns>The analysis.</returns>
    private static async Task<AnalysisResult> AnalyseAsync(IReadOnlyList<GateRecord> aGates)
    {
        var vStore = new FixtureTelemetryStore().Seed(UserId, "acme/alpha", Framework, aGates);
        var vEngine = new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance);
        return await vEngine.AnalyseAsync(UserId, Framework);
    }
}
