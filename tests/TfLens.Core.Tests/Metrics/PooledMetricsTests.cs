using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-051 and REQ-FN-053 — the poolable formulas, their rounding, and the dollar figure that is
/// always absent.
/// </summary>
public sealed class PooledMetricsTests
{
    /// <summary>Rework ratio is fix-mode runs over build-phase runs, rendered as a whole percentage.</summary>
    [Fact]
    public void ReworkRatioIsFixRunsOverBuildPhaseRuns()
    {
        var vPooled = Compute(aRuns: [
            GateFixtures.Run(aCmd: "build-phase", aMode: "build"),
            GateFixtures.Run(aCmd: "build-phase", aMode: "build"),
            GateFixtures.Run(aCmd: "build-phase", aMode: "fix"),
            GateFixtures.Run(aCmd: "build-phase", aMode: "build"),
            GateFixtures.Run(aCmd: "fix-issues", aMode: "fix")
        ]);

        Assert.Equal("50%", vPooled.ReworkRatio.Display());
        Assert.Equal(5, vPooled.RunsTotal);
        Assert.Equal(["build-phase=4", "fix-issues=1"], vPooled.RunsByCmd.Select(aCmd => $"{aCmd.Key}={aCmd.Value}"));
    }

    /// <summary>Throughput is the median of REQs per second across runs, in REQs per hour to two decimal places.</summary>
    [Fact]
    public void ThroughputMedianIsReqsPerHourToTwoDecimalPlaces()
    {
        var vPooled = Compute(aRuns: [
            GateFixtures.Run(aDurationS: 3600, aReqsCount: 4),
            GateFixtures.Run(aDurationS: 1800, aReqsCount: 3),
            GateFixtures.Run(aDurationS: 900, aReqsCount: 2),
            GateFixtures.Run(aDurationS: 600, aReqsCount: 5),
            GateFixtures.Run(aDurationS: 1200, aReqsCount: 1),
            GateFixtures.Run(aDurationS: 7200, aReqsCount: 6)
        ]);

        Assert.True(vPooled.ThroughputMedianReqsPerHour.TryGetValue(out var vValue));
        Assert.Equal(5.0d, vValue);
    }

    /// <summary>A run missing either half of the throughput fraction is excluded, as the reference's truthiness check does.</summary>
    [Fact]
    public void ThroughputSkipsRunsMissingDurationOrCount()
    {
        var vPooled = Compute(aRuns: [
            GateFixtures.Run(aDurationS: 3600, aReqsCount: 4),
            GateFixtures.Run(aDurationS: null, aReqsCount: 4),
            GateFixtures.Run(aDurationS: 3600, aReqsCount: null),
            GateFixtures.Run(aDurationS: 0, aReqsCount: 4)
        ]);

        Assert.Equal("insufficient data (n=1)", vPooled.ThroughputMedianReqsPerHour.Display());
    }

    /// <summary>Batch size is the unrounded median REQ count of a build-phase run.</summary>
    [Fact]
    public void BatchSizeMedianIsTheUnroundedMedianOfBuildPhaseRuns()
    {
        var vPooled = Compute(aRuns: [
            GateFixtures.Run(aReqsCount: 4),
            GateFixtures.Run(aReqsCount: 3),
            GateFixtures.Run(aReqsCount: 2),
            GateFixtures.Run(aReqsCount: 6),
            GateFixtures.Run(aCmd: "verify-phase", aReqsCount: 99)
        ]);

        Assert.True(vPooled.BatchSizeMedian.TryGetValue(out var vValue));
        Assert.Equal(3.5d, vValue);
    }

    /// <summary>Tokens per verified REQ is total session tokens over <c>Verified</c> verdicts, to one decimal place.</summary>
    [Fact]
    public void TokensPerVerifiedReqIsToOneDecimalPlace()
    {
        var vPooled = Compute(
            aSessions: [
                GateFixtures.Session("s1", 100000, 20000),
                GateFixtures.Session("s2", 50000, 10000),
                GateFixtures.Session("s3", 30000, 5000)
            ],
            aGates: [
                GateFixtures.Gate(aReqId: "REQ-FN-001"),
                GateFixtures.Gate(aReqId: "REQ-FN-002"),
                GateFixtures.Gate(aReqId: "REQ-FN-003"),
                GateFixtures.Gate(aReqId: "REQ-FN-004", aVerdict: "FAIL", aGate: "build")
            ]);

        Assert.Equal(215000L, vPooled.TokensTotal);
        Assert.True(vPooled.TokensPerVerifiedReq.TryGetValue(out var vValue));
        Assert.Equal(71666.7d, vValue);
    }

    /// <summary>Commit cadence is commits per active day to two decimal places, over distinct commit dates.</summary>
    [Fact]
    public void CommitCadenceIsCommitsPerActiveDayToTwoDecimalPlaces()
    {
        var vPooled = Compute(aCommits: [
            GateFixtures.Commit("a1", "2026-08-01T09:00:00Z"),
            GateFixtures.Commit("a2", "2026-08-01T12:00:00Z"),
            GateFixtures.Commit("a3", "2026-08-02T09:00:00Z"),
            GateFixtures.Commit("a4", "2026-08-03T09:00:00Z")
        ]);

        Assert.Equal(3, vPooled.ActiveDays);
        Assert.True(vPooled.CommitsPerActiveDay.TryGetValue(out var vValue));
        Assert.Equal(1.33d, vValue);
    }

    /// <summary>Duplicate SHAs collapse within a repository but two repositories may share a short SHA.</summary>
    [Fact]
    public void DuplicateShasCollapsePerRepositoryOnly()
    {
        var (vRecords, vDuplicates) = DedupeCommits.PerRepo([
            GateFixtures.Commit("a1", "2026-08-01T09:00:00Z"),
            GateFixtures.Commit("a1", "2026-08-01T09:00:00Z"),
            GateFixtures.Commit("a1", "2026-08-04T09:00:00Z", "acme/beta")
        ]);

        Assert.Equal(2, vRecords.Count);
        Assert.Equal(1, vDuplicates);
    }

    /// <summary>The pooled dollar figure is null, and the contract computes it rather than storing it.</summary>
    [Fact]
    public void PooledCostIsAlwaysNull()
    {
        Assert.Null(Compute().CostUsd);
        Assert.False(typeof(PooledMetrics).GetProperty(nameof(PooledMetrics.CostUsd))!.CanWrite);
    }

    /// <summary>Computes the pooled block over the records a test supplies.</summary>
    /// <param name="aRuns">Run records.</param>
    /// <param name="aSessions">Session records.</param>
    /// <param name="aCommits">Commit records.</param>
    /// <param name="aGates">Gate records.</param>
    /// <returns>The pooled metrics.</returns>
    private static PooledMetrics Compute(
        IReadOnlyList<RunRecord>? aRuns = null,
        IReadOnlyList<SessionRecord>? aSessions = null,
        IReadOnlyList<CommitRecord>? aCommits = null,
        IReadOnlyList<GateRecord>? aGates = null)
    {
        var (vCommits, vDuplicates) = DedupeCommits.PerRepo(aCommits ?? []);
        return Pooled.Compute(aRuns ?? [], aSessions ?? [], vCommits, vDuplicates, aGates ?? []);
    }
}
