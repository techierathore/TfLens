using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The <c>FIELD_SINCE</c> eligibility floor (REQ-FN-076, BRD-117).
/// </summary>
/// <remarks>
/// The same shape as the existing late-gate test for <c>perf</c>, one table over: a miss written before
/// <c>why_missed</c> existed had no field to fill, so it leaves that field's denominator entirely and is
/// reported separately rather than counted as unassessed. Getting this wrong makes
/// <c>n of N assessed</c> disagree with the oracle on any repository holding pre-2026-08-28 misses.
/// </remarks>
public sealed class MissFieldEligibilityTests
{
    /// <summary>The floor table sits beside the late-gate table and carries the one field.</summary>
    [Fact]
    public void FieldSinceSitsBesideLateGates()
    {
        MetricsConstants.FieldSince.Should().ContainKey("why_missed").WhoseValue.Should().Be("2026-08-28");
        MetricsConstants.LateGates.Should().ContainKey("perf").WhoseValue.Should().Be("2026-08-10");
    }

    /// <summary>A miss written before the field existed leaves the denominator and is counted separately.</summary>
    [Fact]
    public void MissWrittenBeforeTheFloorLeavesTheDenominator()
    {
        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "why_missed",
            [
                Miss("2026-08-20T09:00:00Z", null),
                Miss("2026-08-28T09:00:00Z", "instruction-ignored"),
                Miss("2026-08-29T09:00:00Z", null)
            ],
            aM => aM.Ts,
            aM => aM.WhyMissed);

        vResult.Since.Should().Be("2026-08-28");
        vResult.PredatesField.Should().Be(1);
        vResult.Eligible.Should().Be(2, "the denominator is what could have carried the field");
        vResult.Assessed.Should().Be(1);
    }

    /// <summary>A record written on the introduction day itself is eligible, not excluded.</summary>
    [Fact]
    public void ARecordOnTheFloorDateIsEligible()
    {
        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "why_missed", [Miss("2026-08-28T00:00:00Z", "other")], aM => aM.Ts, aM => aM.WhyMissed);

        vResult.PredatesField.Should().Be(0);
        vResult.Eligible.Should().Be(1);
        vResult.Assessed.Should().Be(1);
    }

    /// <summary>A field with no floor excludes nothing — every record is eligible.</summary>
    [Fact]
    public void AFieldWithNoFloorExcludesNothing()
    {
        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "severity", [Miss("2020-01-01T00:00:00Z", null)], aM => aM.Ts, aM => aM.Severity);

        vResult.Since.Should().BeNull();
        vResult.PredatesField.Should().Be(0);
        vResult.Eligible.Should().Be(1);
    }

    /// <summary>An unusable timestamp is eligible: a missing field never shrinks a denominator.</summary>
    [Fact]
    public void ARecordWithNoUsableTimestampStaysEligible()
    {
        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "why_missed", [Miss(string.Empty, "other")], aM => aM.Ts, aM => aM.WhyMissed);

        vResult.PredatesField.Should().Be(0);
        vResult.Eligible.Should().Be(1);
    }

    /// <summary>Assessed is a subset of eligible, never of the record total.</summary>
    [Fact]
    public void AssessedIsNeverMeasuredAgainstTheRecordTotal()
    {
        var vMisses = new[]
        {
            Miss("2026-08-01T09:00:00Z", null),
            Miss("2026-08-02T09:00:00Z", null),
            Miss("2026-08-28T09:00:00Z", "code-audit-limitation")
        };

        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "why_missed", vMisses, aM => aM.Ts, aM => aM.WhyMissed);

        vResult.Eligible.Should().Be(1);
        vResult.Assessed.Should().Be(1);
        vResult.PredatesField.Should().Be(2);
        (vResult.Eligible + vResult.PredatesField).Should().Be(vMisses.Length, "nothing is silently dropped");
    }

    /// <summary>Builds a miss carrying only the two fields the floor reads.</summary>
    /// <param name="aTs">The timestamp compared against the floor.</param>
    /// <param name="aWhyMissed">The optional field's value, or <c>null</c> for not assessed.</param>
    /// <returns>The record.</returns>
    private static MissRecord Miss(string aTs, string? aWhyMissed) => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = "owner/name",
        SourceSha = Fixtures.SourceSha,
        Ts = aTs,
        MissId = "MISS-A-1",
        WhyMissed = aWhyMissed,
        Severity = "major"
    };
}
