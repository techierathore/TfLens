using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The duration rule — the timestamps win (REQ-FN-112, BRD-179; BRD-189 to BRD-192).
/// </summary>
/// <remarks>
/// <para>
/// Every test here pins a case where the wrong answer is <b>plausible</b>. A stored <c>duration_s</c> of
/// 600 on a record whose own clocks say otherwise reads perfectly well; thirteen of TechieBlog's
/// fourteen bad records are exactly that shape, and only the one storing <c>-166</c> was ever detectable
/// by reading the number alone. A rule that trusted the stored field would pass every naive test and
/// still report six hours of work that never happened.
/// </para>
/// <para>
/// The fourth rule is here for the opposite reason. A run that starts and ends in the same second is
/// <b>not</b> corrupt, and reporting it as impossible is a defect rather than a rounding difference: it
/// once made the framework's own repository look as though it held thirty corrupt records when it held
/// thirty runs that simply recorded no elapsed time.
/// </para>
/// </remarks>
public sealed class RunDurationTests
{
    /// <summary>Rule 1 — a run's duration comes from its own two timestamps.</summary>
    [Fact]
    public void TimestampsAreTheDurationWhenBothParse()
    {
        var vRead = RunDuration.Derive([Run("10:00:00", "10:30:00")]);

        vRead.Records[0].DurationS.Should().Be(1800, "the record's own clocks span thirty minutes");
        vRead.MeasuredN.Should().Be(1);
        vRead.RecomputedN.Should().Be(0, "nothing was overridden — the record stored no duration");
        vRead.ImpossibleN.Should().Be(0);
        vRead.AbsentN.Should().Be(0);
    }

    /// <summary>
    /// Rule 2 — a stored duration that disagrees with the timestamps by more than a second loses.
    /// </summary>
    /// <remarks>
    /// The fixture is TechieBlog's own worst record in miniature: a plausible round 600 against clocks
    /// that say something else entirely. The figure to fear is not the crash but the quiet six hours.
    /// </remarks>
    [Fact]
    public void StoredDurationLosesToTheTimestampsAndCountsAsRecomputed()
    {
        var vRead = RunDuration.Derive([Run("10:00:00", "10:30:00", aDurationS: 600)]);

        vRead.Records[0].DurationS.Should().Be(1800, "the timestamps win, not the stored figure");
        vRead.RecomputedN.Should().Be(1, "the override is published, or the total is unexplainable");
        vRead.MeasuredN.Should().Be(1, "recomputed records are a SUBSET of the measured ones");
    }

    /// <summary>Rule 2, the other edge — a stored duration within a second is rounding, not disagreement.</summary>
    [Fact]
    public void StoredDurationWithinOneSecondIsNotReportedAsRecomputed()
    {
        var vRead = RunDuration.Derive([Run("10:00:00", "10:30:00", aDurationS: 1799)]);

        vRead.RecomputedN.Should().Be(0, "one second of rounding is not a disagreement to report");
        vRead.MeasuredN.Should().Be(1);
    }

    /// <summary>
    /// Rule 3 — a record whose <c>ended</c> precedes its <c>started</c> carries no duration at all.
    /// </summary>
    /// <remarks>
    /// The stored 600 is deliberately plausible and deliberately refused. Substituting it, clamping the
    /// span to zero, or guessing anything else would each produce a defensible-looking number from a
    /// record that measured nothing.
    /// </remarks>
    [Fact]
    public void ImpossibleRecordCarriesNoDurationAndIsNeverSubstituted()
    {
        var vRead = RunDuration.Derive([Run("20:05:00", "17:59:39", aDurationS: 600)]);

        vRead.Records[0].DurationS.Should().BeNull("an impossible record contributes nothing");
        vRead.ImpossibleN.Should().Be(1);
        vRead.MeasuredN.Should().Be(0);
        vRead.AbsentN.Should().Be(0, "a start after an end is impossible, not merely unrecorded");
    }

    /// <summary>
    /// Rule 4 — starting and ending in the same second records no elapsed time. It is NOT corrupt.
    /// </summary>
    /// <remarks>
    /// Reporting this as impossible is the defect this test exists to prevent. The two facts have
    /// different remedies, and a reader told the wrong reason chases the wrong thing.
    /// </remarks>
    [Fact]
    public void SameSecondRunRecordsNoElapsedTimeAndIsNeverCalledImpossible()
    {
        var vRead = RunDuration.Derive([Run("10:00:00", "10:00:00")]);

        vRead.AbsentN.Should().Be(1, "the run recorded no elapsed time");
        vRead.ImpossibleN.Should().Be(0, "reporting this as corrupt is a defect, not a rounding difference");
        vRead.MeasuredN.Should().Be(0, "no elapsed time means no contribution to any total");
        vRead.Records[0].DurationS.Should().BeNull();
    }

    /// <summary>Rule 5 — where the timestamps cannot be read, a positive stored duration answers.</summary>
    [Fact]
    public void StoredDurationSurvivesWhereTheTimestampsCannotBeRead()
    {
        var vRead = RunDuration.Derive([Run(null, null, aDurationS: 900)]);

        vRead.Records[0].DurationS.Should().Be(900, "the stored figure is the only measurement left");
        vRead.MeasuredN.Should().Be(1);
        vRead.RecomputedN.Should().Be(0, "there was no timestamp to override it with");
    }

    /// <summary>Rule 5's companion — <c>ts</c> stands in for an absent <c>ended</c>, per SCHEMA.md.</summary>
    [Fact]
    public void TsStandsInForAnAbsentEndedAndTheDerivationIsCounted()
    {
        var vRun = Run("10:00:00", null) with { Ts = "2026-09-01T10:20:00Z" };

        var vRead = RunDuration.Derive([vRun]);

        vRead.Records[0].DurationS.Should().Be(1200, "SCHEMA.md's rule is that `ended` IS when the record was written");
        vRead.Records[0].DurationDerivedFrom.Should().Be(RunDuration.FromTs);
        vRead.DerivedN.Should().Be(1);
        vRead.MeasuredN.Should().Be(1);
    }

    /// <summary>
    /// The three counts partition the records exactly, so they can be checked against each other.
    /// </summary>
    /// <remarks>
    /// A total published without its exclusions is just a different wrong number. This is the arithmetic
    /// that makes the four keys auditable rather than decorative — it holds on every repository in the
    /// estate, and a rule change that broke it would show up here before it reached a report.
    /// </remarks>
    [Fact]
    public void MeasuredImpossibleAndAbsentAccountForEveryRecord()
    {
        var vRuns = new[]
        {
            Run("10:00:00", "10:30:00"),                       // measured
            Run("10:00:00", "10:30:00", aDurationS: 600),      // measured, recomputed
            Run("20:05:00", "17:59:39", aDurationS: 600),      // impossible
            Run("10:00:00", "10:00:00"),                       // absent
            Run(null, null, aDurationS: 900)                   // measured, from the stored figure
        };

        var vRead = RunDuration.Derive(vRuns);

        (vRead.MeasuredN + vRead.ImpossibleN + vRead.AbsentN).Should().Be(vRuns.Length);
        vRead.MeasuredN.Should().Be(3);
        vRead.ImpossibleN.Should().Be(1);
        vRead.AbsentN.Should().Be(1);
        vRead.RecomputedN.Should().Be(1, "a subset of the measured ones, never an addition");
    }

    /// <summary>
    /// The stream is append-only: reading a duration never edits the record it read (SCHEMA.md §3).
    /// </summary>
    [Fact]
    public void ReadingADurationNeverEditsTheRecordItReadFrom()
    {
        var vRun = Run("20:05:00", "17:59:39", aDurationS: 600);

        RunDuration.Derive([vRun]);

        vRun.DurationS.Should().Be(600, "the bad record stays exactly as it arrived; only the READ excludes it");
        vRun.DurationDerivedFrom.Should().BeNull();
    }

    /// <summary>
    /// End to end: an impossible record leaves the wall clock rather than inflating it, and the phase
    /// block publishes what it left out.
    /// </summary>
    /// <remarks>
    /// This is TechieBlog's shape — a total that falls from 79.0 h to 72.9 h with nothing on the page to
    /// say why unless the counts ship beside it.
    /// </remarks>
    [Fact]
    public void PhaseBlockExcludesImpossibleRecordsAndPublishesTheFourCounts()
    {
        var vAnalysis = PhaseMetrics.Compute(
        [
            Run("10:00:00", "10:30:00") with { Cmd = "build-phase" },
            Run("20:05:00", "17:59:39", aDurationS: 600) with { Cmd = "build-phase" },
            Run("10:00:00", "10:00:00") with { Cmd = "log-miss" }
        ]);

        vAnalysis.DurationSecondsTotal.Should().Be(1800, "the impossible record's plausible 600 never joins the total");
        vAnalysis.DurationMeasuredN.Should().Be(1);
        vAnalysis.DurationImpossibleN.Should().Be(1);
        vAnalysis.DurationAbsentN.Should().Be(1);
        vAnalysis.DurationRecomputedN.Should().Be(0);
        (vAnalysis.DurationMeasuredN + vAnalysis.DurationImpossibleN + vAnalysis.DurationAbsentN)
            .Should().Be(vAnalysis.RunsLive, "the three counts partition the live records exactly");
    }

    /// <summary>
    /// One run record, timestamped on a fixed day so only the clock times in a test matter.
    /// </summary>
    /// <param name="aStarted">The <c>started</c> clock time, or <c>null</c> for a record carrying none.</param>
    /// <param name="aEnded">The <c>ended</c> clock time, or <c>null</c> for a record carrying none.</param>
    /// <param name="aDurationS">The stored <c>duration_s</c>, or <c>null</c>.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(string? aStarted, string? aEnded, int? aDurationS = null) => new()
    {
        UserId = 7,
        Repo = "acme/alpha",
        SourceSha = "fixture",
        Ts = "2026-09-01T10:00:00Z",
        Started = aStarted is null ? null : $"2026-09-01T{aStarted}Z",
        Ended = aEnded is null ? null : $"2026-09-01T{aEnded}Z",
        DurationS = aDurationS
    };
}
