using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-052 — a late-added gate reports <c>ran</c> beside <c>caught</c>, on the <c>gates_run</c>
/// denominator, never as a share of the raw distribution.
/// </summary>
public sealed class LateGateCoverageTests
{
    /// <summary>
    /// The late-gate table is the reference's, entry for entry: <c>perf</c> from 2026-08-10, and
    /// <c>assets</c> + <c>mockup-parity</c> from 2026-08-31.
    /// </summary>
    /// <remarks>
    /// Asserted as the WHOLE table rather than one key, because the failure this guards against is an
    /// omission, not a wrong value — the two 2026-08-31 gates ran and caught real defects for two days
    /// while this table still listed one gate, and a per-key assertion cannot notice a missing key. Keep
    /// in step with <c>LATE_GATES</c> in <c>tf-metrics.sh</c>; the §13 diff is what catches drift here.
    /// </remarks>
    [Fact]
    public void TheLateGateTableMatchesTheReferenceEntryForEntry()
    {
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["perf"] = "2026-08-10",
                ["assets"] = "2026-08-31",
                ["mockup-parity"] = "2026-08-31"
            },
            MetricsConstants.LateGates);
    }

    /// <summary>
    /// The coverage row for one gate, by name.
    /// </summary>
    /// <remarks>
    /// The calculator emits a row per late gate, so these tests name the gate they are about instead of
    /// taking the only row. They were written when <c>perf</c> was the only late gate and <c>Single()</c>
    /// silently encoded that; naming the gate makes them survive the next addition to the table.
    /// </remarks>
    /// <param name="aRecords">The gate records.</param>
    /// <param name="aCounts">The failure distribution.</param>
    /// <param name="aGate">The gate whose row is wanted.</param>
    /// <returns>That gate's coverage row.</returns>
    private static LateGateCoverage CoverageFor(
        IReadOnlyList<GateRecord> aRecords,
        IReadOnlyDictionary<string, int> aCounts,
        string aGate) =>
        LateGateCoverageCalculator.Compute(aRecords, aCounts).Single(aRow => aRow.Gate == aGate);

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
        var vCoverage = CoverageFor(vRecords, GateDistribution.Count(vFailures), "perf");

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
        var vCoverage = CoverageFor(vRecords, vCounts, "perf");
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

        var vCoverage = CoverageFor(vRecords, GateDistribution.Count([]), "perf");

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
        var vCoverage = CoverageFor(vRecords, GateDistribution.Count(vFailures), "perf");

        Assert.Equal(2, vCoverage.Ran);
        Assert.Equal("insufficient data (n=2)", vCoverage.CatchRate.Display());
    }
}
