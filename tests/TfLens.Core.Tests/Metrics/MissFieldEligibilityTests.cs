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
    [Fact(DisplayName =
        "REQ-FN-076 — a miss predating an optional field leaves that field's denominator and is reported "
        + "separately")]
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

    /// <summary>
    /// The floor gains <c>sort</c> and <c>what</c>, both at 2026-09-07 (BRD-117, amended 2026-09-08).
    /// </summary>
    /// <remarks>
    /// Declared once, in data, so every figure built on either field inherits the floor rather than
    /// hand-writing it — which is what makes BRD-172's honest denominator automatic.
    /// </remarks>
    [Fact]
    public void FieldSinceCarriesTheTwo20260907Fields()
    {
        MetricsConstants.FieldSince.Should().ContainKey("sort").WhoseValue.Should().Be("2026-09-07");
        MetricsConstants.FieldSince.Should().ContainKey("what").WhoseValue.Should().Be("2026-09-07");
    }

    /// <summary>
    /// A miss predating <c>sort</c> leaves that field's denominator and is reported separately — it is a
    /// record from before the question was asked, not one that declined to answer.
    /// </summary>
    [Fact(DisplayName =
        "REQ-FN-076 — a miss predating `sort` leaves the sort denominator and is reported separately as "
        + "sort_predates_field")]
    public void MissPredatingSortLeavesTheSortDenominator()
    {
        var vMisses = new[]
        {
            Miss("2026-08-20T09:00:00Z", null),
            Miss("2026-09-06T23:59:59Z", null),
            Miss("2026-09-07T09:00:00Z", null) with { Sort = "weak-check" },
            Miss("2026-09-08T09:00:00Z", null)
        };

        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "sort", vMisses, aM => aM.Ts, aM => aM.Sort);

        vResult.Since.Should().Be("2026-09-07");
        vResult.PredatesField.Should().Be(2);
        vResult.Eligible.Should().Be(2, "the denominator is what could have carried the field");
        vResult.Assessed.Should().Be(1);
        (vResult.Eligible + vResult.PredatesField).Should().Be(vMisses.Length, "nothing is silently dropped");
    }

    /// <summary>The same floor applies to <c>what</c>, through the same one code path.</summary>
    [Fact]
    public void MissPredatingWhatLeavesTheWhatDenominator()
    {
        var vMisses = new[]
        {
            Miss("2026-08-20T09:00:00Z", null),
            Miss("2026-09-07T09:00:00Z", null) with { What = "the export defaulted its framework" }
        };

        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "what", vMisses, aM => aM.Ts, aM => aM.What);

        vResult.Since.Should().Be("2026-09-07");
        vResult.PredatesField.Should().Be(1);
        vResult.Eligible.Should().Be(1);
        vResult.Assessed.Should().Be(1);
    }

    /// <summary>
    /// <b>A record that carries the field is eligible whatever its date</b> — which is what makes an
    /// amendment of an old record visible at all (BRD-116, BRD-117).
    /// </summary>
    /// <remarks>
    /// Without this, folding a <c>sort</c> onto a pre-2026-09-07 miss would add the answer and the floor
    /// would immediately throw it back out, so the amendment would have no effect on any figure. The
    /// reference states the same rule, so the two agree by construction.
    /// </remarks>
    [Fact]
    public void ARecordAmendedAfterTheFloorIsEligibleWhateverItsDate()
    {
        var vMisses = new[]
        {
            Miss("2026-08-20T09:00:00Z", null) with { Sort = "spec" },
            Miss("2026-08-21T09:00:00Z", null)
        };

        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "sort", vMisses, aM => aM.Ts, aM => aM.Sort);

        vResult.Eligible.Should().Be(1, "an older record completed by an amend is sorted");
        vResult.Assessed.Should().Be(1);
        vResult.PredatesField.Should().Be(1, "the one that still says nothing had no chance to");
    }

    /// <summary>The fold and the floor agree: an amended old record reaches the sort denominator.</summary>
    [Fact]
    public void AmendedSortOnAnOldMissReachesTheSortDenominator()
    {
        var vStored = new[] { Miss("2026-08-20T09:00:00Z", null) };
        var vAmends = new[]
        {
            new MissAmendRecord
            {
                UserId = Fixtures.DemoUserId,
                Repo = "owner/name",
                SourceSha = Fixtures.SourceSha,
                Ts = "2026-09-08T10:00:00Z",
                MissId = "MISS-A-1",
                Field = "sort",
                Value = "unsaid"
            }
        };

        var vFolded = MissAmendFolder.Fold(vStored, vAmends);
        var vResult = LateGateCoverageCalculator.EligibilityFor(
            "sort", vFolded.Misses, aM => aM.Ts, aM => aM.Sort);

        vFolded.AmendmentsApplied.Should().Be(1);
        vResult.Assessed.Should().Be(1, "folding happens before anything is counted");
        vResult.Eligible.Should().Be(1);
        vResult.PredatesField.Should().Be(0);
    }

    /// <summary>
    /// A record predating the field is excluded through <c>IsEligibleForField</c> too — one table, one
    /// code path, so a second caller cannot re-derive the comparison differently.
    /// </summary>
    [Fact]
    public void TheSingleRecordFormAgreesWithTheBulkForm()
    {
        LateGateCoverageCalculator.IsEligibleForField("sort", "2026-09-06T23:59:59Z").Should().BeFalse();
        LateGateCoverageCalculator.IsEligibleForField("sort", "2026-09-07T00:00:00Z").Should().BeTrue();
        LateGateCoverageCalculator.IsEligibleForField("what", "2026-09-06T23:59:59Z").Should().BeFalse();
        LateGateCoverageCalculator.IsEligibleForField("what", "2026-09-07T00:00:00Z").Should().BeTrue();
    }

    /// <summary>Both excluded counts are reported under the <c>&lt;field&gt;_*</c> names the oracle uses.</summary>
    [Fact]
    public void BothCountsAreReportedUnderTheFieldScopedKeys()
    {
        MetricsConstants.EligibleKey("sort").Should().Be("sort_eligible");
        MetricsConstants.PredatesFieldKey("sort").Should().Be("sort_predates_field");
        MetricsConstants.EligibleKey("what").Should().Be("what_eligible");
        MetricsConstants.PredatesFieldKey("what").Should().Be("what_predates_field");
        MetricsConstants.EligibleKey("why_missed").Should().Be("why_missed_eligible");
        MetricsConstants.PredatesFieldKey("why_missed").Should().Be("why_missed_predates_field");
    }

    /// <summary>Every field with a floor can name both of its report keys — no floor is silent.</summary>
    [Fact]
    public void EveryFlooredFieldHasBothReportKeys()
    {
        foreach (var vField in MetricsConstants.FieldSince.Keys)
        {
            MetricsConstants.EligibleKey(vField).Should().EndWith("_eligible");
            MetricsConstants.PredatesFieldKey(vField).Should().Be(vField + "_predates_field");
        }
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
