using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-052 — a late-added gate reports <c>ran</c> beside <c>caught</c>, on the <c>gates_run</c>
/// denominator, never as a share of the raw distribution.
/// </summary>
public sealed class LateGateCoverageTests
{
    /// <summary>The late-gate table is the reference's: <c>perf</c>, added 2026-08-10.</summary>
    [Fact]
    public void PerfIsTheLateGateWithItsIntroductionDate()
    {
        Assert.Equal("2026-08-10", MetricsConstants.LateGates["perf"]);
    }

    /// <summary><c>ran</c> counts records whose <c>gates_run</c> contains the gate, not records that failed on it.</summary>
    [Fact]
    public void RanCountsGatesRunMembershipNotFailures()
    {
        var vRecords = new List<GateRecord>
        {
            GateFixtures.Gate(aReqId: "REQ-FN-001", aGatesRun: ["build", "perf"]),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aGatesRun: ["build", "perf"]),
            GateFixtures.Gate(aReqId: "REQ-FN-003", aVerdict: "FAIL", aGate: "perf", aGatesRun: ["build", "perf"]),
            GateFixtures.Gate(aReqId: "REQ-FN-004", aGatesRun: ["build"]),
            GateFixtures.Gate(aReqId: "REQ-FN-005", aGatesRun: null)
        };

        var vFailures = vRecords.Where(aRecord => aRecord.Verdict == "FAIL").ToList();
        var vCoverage = LateGateCoverageCalculator.Compute(vRecords, GateDistribution.Count(vFailures)).Single();

        Assert.Equal("perf", vCoverage.Gate);
        Assert.Equal(3, vCoverage.Ran);
        Assert.Equal(1, vCoverage.Caught);
        Assert.Equal("33%", vCoverage.CatchRate.Display());
    }

    /// <summary>The catch rate is caught over ran — never caught over the distribution total.</summary>
    [Fact]
    public void CatchRateIsNeverAShareOfTheDistribution()
    {
        var vRecords = new List<GateRecord>
        {
            GateFixtures.Gate(aReqId: "REQ-FN-001", aVerdict: "FAIL", aGate: "perf", aGatesRun: ["perf"]),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aGatesRun: ["perf"]),
            GateFixtures.Gate(aReqId: "REQ-FN-003", aGatesRun: ["perf"]),
            GateFixtures.Gate(aReqId: "REQ-FN-004", aVerdict: "FAIL", aGate: "build", aGatesRun: ["build"]),
            GateFixtures.Gate(aReqId: "REQ-FN-005", aVerdict: "FAIL", aGate: "build", aGatesRun: ["build"])
        };

        var vFailures = vRecords.Where(aRecord => aRecord.Verdict == "FAIL").ToList();
        var vCounts = GateDistribution.Count(vFailures);
        var vCoverage = LateGateCoverageCalculator.Compute(vRecords, vCounts).Single();
        var vShareOfDistribution = MetricsConstants.Pct(vCounts["perf"], vFailures.Count);

        Assert.Equal("33%", vShareOfDistribution);
        Assert.Equal("33%", vCoverage.CatchRate.Display());
        Assert.Equal(3, vCoverage.Ran);
        Assert.Equal(3, vFailures.Count);

        // The two happen to coincide here; what matters is that Ran and the distribution total are
        // different numbers and only Ran feeds the rate.
        Assert.NotEqual(vCoverage.Ran, vCounts["build"]);
    }

    /// <summary>A gate that has not run at all says so, rather than reporting a zero rate.</summary>
    [Fact]
    public void GateThatNeverRanIsNotApplicableRatherThanZero()
    {
        var vRecords = new List<GateRecord>
        {
            GateFixtures.Gate(aReqId: "REQ-FN-001", aGatesRun: ["build"]),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aGatesRun: ["build"])
        };

        var vCoverage = LateGateCoverageCalculator.Compute(vRecords, GateDistribution.Count([])).Single();

        Assert.Equal(0, vCoverage.Ran);
        Assert.Equal(0, vCoverage.Caught);
        Assert.Equal(FigureKind.NotApplicable, vCoverage.CatchRate.Kind);
        Assert.Equal("—", vCoverage.CatchRate.Display());
    }

    /// <summary>Too few records ran the gate to state a rate, so it refuses rather than dividing by one.</summary>
    [Fact]
    public void GateRunTooFewTimesRefusesARate()
    {
        var vRecords = new List<GateRecord>
        {
            GateFixtures.Gate(aReqId: "REQ-FN-001", aVerdict: "FAIL", aGate: "perf", aGatesRun: ["perf"]),
            GateFixtures.Gate(aReqId: "REQ-FN-002", aGatesRun: ["perf"])
        };

        var vFailures = vRecords.Where(aRecord => aRecord.Verdict == "FAIL").ToList();
        var vCoverage = LateGateCoverageCalculator.Compute(vRecords, GateDistribution.Count(vFailures)).Single();

        Assert.Equal(2, vCoverage.Ran);
        Assert.Equal("insufficient data (n=2)", vCoverage.CatchRate.Display());
    }
}
