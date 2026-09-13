using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The per-phase counts a page states beside a time or token figure (REQ-FN-112, BRD-179, BRD-189 to
/// BRD-192; REQ-FN-089, BRD-146).
/// </summary>
/// <remarks>
/// <para>
/// Both defects these pin were counts that were true and printed against the wrong denominator. The
/// oracle's per-phase <c>derived_n</c> counts every run closed from its own timestamps, same-second
/// runs included, so a phase of <c>*log-miss</c> runs read "62 of 36 timed durations were derived" — a
/// sentence no count can make true. And a phase measured on no run carries the oracle's share string
/// <c>"0%"</c>, which on a page is a zero standing in for "not measured".
/// </para>
/// <para>
/// The export keeps both oracle values key-for-key (BRD-152); the tests below also pin that, so fixing
/// the page can never quietly move a parity key.
/// </para>
/// </remarks>
public sealed class PhaseDurationCountsTests
{
    /// <summary>
    /// A phase of same-second runs has more derivations than timed runs; the count a page may print
    /// beside the time figure is the derived subset of the timed runs, never more than them.
    /// </summary>
    [Fact]
    public void DerivedCountBesideTheTimeFigureNeverExceedsTheTimedRuns()
    {
        var vRuns = new List<RunRecord>
        {
            Run("log-miss", "10:00:00", "10:00:00"),
            Run("log-miss", "10:01:00", "10:01:00"),
            Run("log-miss", "10:02:00", "10:02:00"),
            Run("log-miss", "10:03:00", "10:03:30", aDurationS: 30),
            Run("log-miss", "10:04:00", aEnded: null, aTs: "10:04:20")
        };

        var vRow = PhaseMetrics.Compute(vRuns).Phases.Single();

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vRow.Duration.TimedN.Should().Be(2, "one run stored an agreeing duration, one was closed from ts");
        vRow.Duration.DerivedN.Should().Be(4, "the oracle's derived_n also counts the three same-second runs");
        vRow.Duration.DerivedTimedN.Should().Be(1, "only the ts-closed run both was derived and was timed");
        vRow.Duration.DerivedTimedN.Should().BeLessThanOrEqualTo(vRow.Duration.TimedN);
        vRow.Duration.AbsentN.Should().Be(3, "a same-second run recorded no elapsed time — never corrupt");
    }

    /// <summary>
    /// A stored duration the timestamps disagree with by more than a second is overridden and counted as
    /// recomputed on its phase; an end before a start carries no duration and is counted apart.
    /// </summary>
    [Fact]
    public void RecomputedAndImpossibleAreCountedOnTheirOwnPhase()
    {
        var vRuns = new List<RunRecord>
        {
            Run("refresh-status", "20:05:00", "17:59:39", aDurationS: 600),
            Run("refresh-status", "10:00:00", "11:00:00", aDurationS: 600),
            Run("refresh-status", "12:00:00", "12:10:00", aDurationS: 600),
            Run("build-phase", "13:00:00", "13:30:00", aDurationS: 60)
        };

        var vAnalysis = PhaseMetrics.Compute(vRuns);
        var vStatus = vAnalysis.Phases.Single(aRow => aRow.Cmd == "refresh-status");
        var vBuild = vAnalysis.Phases.Single(aRow => aRow.Cmd == "build-phase");

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vStatus.Duration.TotalSeconds.Should().Be(3600 + 600, "the timestamps win and the impossible run adds nothing");
        vStatus.Duration.RecomputedN.Should().Be(1, "only the hour-long run disagreed with its stored 600");
        vStatus.Duration.ImpossibleN.Should().Be(1, "an end before its start carries no duration");
        vBuild.Duration.RecomputedN.Should().Be(1);
        vAnalysis.Phases.Sum(aRow => aRow.Duration.RecomputedN).Should().Be(vAnalysis.DurationRecomputedN);
        vAnalysis.Phases.Sum(aRow => aRow.Duration.ImpossibleN).Should().Be(vAnalysis.DurationImpossibleN);
    }

    /// <summary>
    /// Per phase, timed + ended-before-started + no-elapsed-time accounts for every run, and across the
    /// phases the three add up to the page-level counts the export publishes.
    /// </summary>
    [Fact]
    public void PhaseDurationCountsPartitionTheRunsAndAddUpToThePageCounts()
    {
        var vRuns = new List<RunRecord>
        {
            Run("fix-issues", "09:00:00", "09:20:00"),
            Run("fix-issues", "09:30:00", "09:30:00"),
            Run("fix-issues", "10:00:00", "09:00:00"),
            Run("fix-issues", aStarted: null, aEnded: null, aDurationS: 120),
            Run("verify-phase", aStarted: null, aEnded: null)
        };

        var vAnalysis = PhaseMetrics.Compute(vRuns);

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        foreach (var vRow in vAnalysis.Phases)
        {
            (vRow.Duration.TimedN + vRow.Duration.ImpossibleN + vRow.Duration.AbsentN)
                .Should().Be(vRow.Runs, $"{vRow.Cmd}: every run is timed, impossible or recorded no time");
        }

        vAnalysis.Phases.Sum(aRow => aRow.Duration.TimedN).Should().Be(vAnalysis.DurationMeasuredN);
        vAnalysis.Phases.Sum(aRow => aRow.Duration.AbsentN).Should().Be(vAnalysis.DurationAbsentN);
        (vAnalysis.DurationMeasuredN + vAnalysis.DurationImpossibleN + vAnalysis.DurationAbsentN)
            .Should().Be(vAnalysis.RunsLive);
        vAnalysis.DurationsDerivedTimedN.Should().BeLessThanOrEqualTo(vAnalysis.DurationMeasuredN);
    }

    /// <summary>
    /// A phase measured on no run shows no share of output — the dash, not the oracle's <c>"0%"</c> —
    /// while the export keeps the oracle's string key-for-key.
    /// </summary>
    [Fact]
    public void PhaseMeasuredOnNoRunShowsNoShareOfOutput()
    {
        var vRuns = new List<RunRecord>
        {
            Run("build-phase", "09:00:00", "09:30:00", aTokensOut: 900, aScope: "main"),
            Run("refresh-status", "10:00:00", "10:05:00", aTokensOut: null, aScope: "none"),
            Run("refresh-status", "11:00:00", "11:05:00", aTokensOut: 50, aScope: null)
        };

        var vAnalysis = PhaseMetrics.Compute(vRuns);
        var vStatus = vAnalysis.Phases.Single(aRow => aRow.Cmd == "refresh-status");
        var vBuild = vAnalysis.Phases.Single(aRow => aRow.Cmd == "build-phase");

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vStatus.TokensMeasuredN.Should().Be(0);
        vStatus.TokensUnmeasuredN.Should().Be(2, "both runs are counted as unmeasured, not dropped");
        vStatus.ShareOfTokensOut.Should().Be("0%", "the export keeps the oracle's own string (BRD-152)");
        PhaseShare.OfMeasuredOutput(vStatus).Should().Be(PhaseShare.NotApplicable, "a surface never shows it as 0%");
        PhaseShare.OfMeasuredOutput(vBuild).Should().Be(vBuild.ShareOfTokensOut, "a measured share is shown unaltered");
    }

    /// <summary>
    /// Nine runs of which four carry no token window: the per-run figure divides by the five measured
    /// runs, and a measured count below MIN_N refuses to be a number.
    /// </summary>
    [Fact]
    public void PerRunTokensDivideByMeasuredRunsOnly()
    {
        var vRuns = Enumerable.Range(0, 5)
            .Select(aI => Run("fix-issues", "09:00:00", "09:10:00", aTokensOut: 1000 * (aI + 1), aScope: "tree"))
            .Concat(Enumerable.Range(0, 4).Select(aI => Run("fix-issues", "09:00:00", "09:10:00", aTokensOut: null, aScope: "none")))
            .Append(Run("log-miss", "09:00:00", "09:10:00", aTokensOut: 10, aScope: "main"))
            .ToList();

        var vAnalysis = PhaseMetrics.Compute(vRuns);
        var vFix = vAnalysis.Phases.Single(aRow => aRow.Cmd == "fix-issues");
        var vMiss = vAnalysis.Phases.Single(aRow => aRow.Cmd == "log-miss");

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vFix.TokensMeasuredN.Should().Be(5);
        vFix.TokensUnmeasuredN.Should().Be(4);
        vFix.TokensOutPerRun.Tokens.TryGetValue(out var vPerRun).Should().BeTrue();
        vPerRun.Should().Be(3000, "15,000 tokens over the 5 measured runs, not over 9");
        vMiss.TokensOutPerRun.Tokens.Kind.Should().Be(FigureKind.InsufficientData, "one measured run is below MIN_N = 3");
        vMiss.TokensOutPerRun.Tokens.HasValue.Should().BeFalse("a refused figure never renders as 0");
    }

    /// <summary>Builds one live run record for a phase.</summary>
    /// <param name="aCmd">The command phase.</param>
    /// <param name="aStarted">The <c>started</c> clock time, or <c>null</c>.</param>
    /// <param name="aEnded">The <c>ended</c> clock time, or <c>null</c>.</param>
    /// <param name="aDurationS">The stored duration, or <c>null</c>.</param>
    /// <param name="aTs">The <c>ts</c> clock time; defaults to the end of the day.</param>
    /// <param name="aTokensOut">Output tokens, or <c>null</c> when the window measured none.</param>
    /// <param name="aScope">The <c>tokens_scope</c>, or <c>null</c> for absent.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(
        string aCmd,
        string? aStarted,
        string? aEnded,
        int? aDurationS = null,
        string aTs = "23:00:00",
        int? aTokensOut = null,
        string? aScope = null) => new()
    {
        UserId = 7,
        Repo = "acme/alpha",
        SourceSha = "fixture",
        Ts = $"2026-09-01T{aTs}Z",
        Cmd = aCmd,
        Started = aStarted is null ? null : $"2026-09-01T{aStarted}Z",
        Ended = aEnded is null ? null : $"2026-09-01T{aEnded}Z",
        DurationS = aDurationS,
        TokensOut = aTokensOut,
        TokensScope = aScope
    };
}
