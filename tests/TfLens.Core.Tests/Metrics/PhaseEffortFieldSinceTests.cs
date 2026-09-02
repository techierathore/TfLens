using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The <c>FIELD_SINCE</c> floor extended with the three SCHEMA §2.6 <c>runs</c> fields
/// (REQ-FN-091, BRD-148).
/// </summary>
/// <remarks>
/// <para>
/// The three fields entered the stream on 2026-08-31, so a run written before that had no field to
/// fill. That is not a run reporting "no sub-agents" — it is a run that could not have said, and it
/// therefore leaves the denominator entirely rather than pushing every fan-out figure down. The
/// exclusion is permanent: unlike "the window was <c>main</c> scope, we did not look", which could be
/// different tomorrow, "written before the field existed" never will be (ADR-026).
/// </para>
/// <para>
/// The second assertion here is that this lives in the <b>same</b> table and the <b>same</b> code path
/// as <c>why_missed</c>. A parallel table would be a second place for a floor to be forgotten, and a
/// forgotten floor is invisible: the figure still renders, just over the wrong denominator.
/// </para>
/// </remarks>
public sealed class PhaseEffortFieldSinceTests
{
    /// <summary>The three §2.6 fields carry a 2026-08-31 floor, beside the existing <c>why_missed</c> row.</summary>
    [Fact]
    public void TheThreeFieldsCarryTheirFloor()
    {
        foreach (var vField in new[] { "subagent_runs", "tokens_out_subagents", "model_tokens_out" })
        {
            MetricsConstants.FieldSince.Should().ContainKey(vField).WhoseValue.Should().Be("2026-08-31");
        }

        MetricsConstants.FieldSince.Should().ContainKey("why_missed").WhoseValue.Should().Be("2026-08-28");
        MetricsConstants.LateGates.Should().ContainKey("perf").WhoseValue.Should().Be("2026-08-10");
    }

    /// <summary>
    /// The floor is read through the one existing code path, so a run predating the field is excluded by
    /// the same calculator that excludes a pre-<c>why_missed</c> miss.
    /// </summary>
    [Fact]
    public void TheSameCalculatorAppliesTheFanoutFloor()
    {
        LateGateCoverageCalculator.IsEligibleForField("subagent_runs", "2026-08-30T23:59:59Z")
            .Should().BeFalse("the field did not exist yet, so this run could not have carried it");

        LateGateCoverageCalculator.IsEligibleForField("subagent_runs", "2026-08-31T00:00:00Z")
            .Should().BeTrue();
    }

    /// <summary>
    /// A mixed set splits into eligible, predates and observed — and a <c>main</c>-scope run inside the
    /// eligible window still counts as eligible-but-unobserved rather than as a measured zero.
    /// </summary>
    [Fact]
    public void RunsSplitIntoEligiblePredatesAndObserved()
    {
        var vRuns = new[]
        {
            Run("2026-08-25T09:00:00Z", null),   // predates the field entirely
            Run("2026-09-01T09:00:00Z", null),   // eligible, but the window never looked
            Run("2026-09-01T10:00:00Z", 3),      // eligible and measured
            Run("2026-09-01T11:00:00Z", 0)       // eligible and measured as zero — a real observation
        };

        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "subagent_runs",
            vRuns,
            aR => aR.Ts,
            aR => aR.SubagentRuns?.ToString());

        vResult.Since.Should().Be("2026-08-31");
        vResult.PredatesField.Should().Be(1);
        vResult.Eligible.Should().Be(3);
        vResult.Assessed.Should().Be(2, "a measured zero is an observation; an absent count is not");
    }

    /// <summary>Builds a run record carrying only what the floor is read from.</summary>
    /// <param name="aTs">The run's ISO-8601 timestamp.</param>
    /// <param name="aSubagentRuns">The measured sub-agent count, or <c>null</c> for not captured.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(string aTs, int? aSubagentRuns) => new()
    {
        UserId = 1,
        Repo = "techierathore/TechieFlow",
        SourceSha = "b17c0de",
        Ts = aTs,
        SubagentRuns = aSubagentRuns
    };
}
