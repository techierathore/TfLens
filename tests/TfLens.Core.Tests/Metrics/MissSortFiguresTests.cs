using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// Whose gap it was — the closed four-value <c>sort</c>, its honest denominator, and the count of values
/// an amendment completed (REQ-FN-106, REQ-FN-107, REQ-FN-109; BRD-170, BRD-172, BRD-176).
/// </summary>
/// <remarks>
/// <para>
/// Three separate failures are pinned here and they fail in three different directions. <b>Coercion</b>
/// files a miss under a remedy nobody chose, and it is invisible afterwards. <b>Pooling</b> counts a
/// record written before the field existed as one that declined to answer, which understates every
/// category at once and does so worst on the oldest records nobody can now complete. <b>A silent fold</b>
/// leaves a reader unable to tell a field that was answered from one answered later.
/// </para>
/// <para>
/// The segment key is <c>app</c> throughout, because every rate lives inside a project-type segment and
/// there is deliberately no "all types" entry to read instead.
/// </para>
/// </remarks>
public sealed class MissSortFiguresTests
{
    /// <summary>The segment every fixture record falls into.</summary>
    private const string AppSegment = "app";

    /// <summary>A date after the 2026-09-07 floor, so the record is eligible to carry a sort.</summary>
    private const string AfterFloor = "2026-09-08T09:00:00Z";

    /// <summary>A date before the 2026-09-07 floor — a record from before the question was asked.</summary>
    private const string BeforeFloor = "2026-08-29T09:00:00Z";

    /// <summary>The four values are exactly the four SCHEMA.md §5.5.10 names, in protocol order.</summary>
    [Fact]
    public void SortVocabularyIsTheClosedFour()
    {
        MissSorts.All.Should().Equal("spec", "unsaid", "weak-check", "ignored");
        MissSorts.Field.Should().Be("sort");
    }

    /// <summary>The amend allowlist and the stored-value check read the same four, not two copies.</summary>
    [Fact]
    public void AmendAllowlistSharesTheOneVocabulary()
    {
        MissAmendFolder.AmendableFields[MissAmendFolder.SortField]
            .Should().BeSameAs(MissSorts.All, "one closed set cannot be closed in two places");
    }

    /// <summary>Blank and absent are absence, never an unrecognised value.</summary>
    [Fact]
    public void BlankSortIsAbsenceNotAnUnrecognisedValue()
    {
        MissSorts.IsRecognised(null).Should().BeFalse();
        MissSorts.IsRecognised(" ").Should().BeFalse();
        MissSorts.IsRecognised("weak-check").Should().BeTrue();
    }

    /// <summary>An out-of-vocabulary sort keeps its own row and is never merged into a legal one.</summary>
    [Fact(DisplayName = "REQ-FN-106 — a sort value outside the four is shown as unrecognised and counted, never filed under the nearest legal one")]
    public void AnUnrecognisedSortIsShownUnderItsOwnName()
    {
        var vFigures = Compute(
        [
            MissFixtures.Miss("MISS-A-1", aTs: AfterFloor, aSort: "weak-check"),
            MissFixtures.Miss("MISS-A-2", aTs: AfterFloor, aSort: "weak-checks"),
            MissFixtures.Miss("MISS-A-3", aTs: AfterFloor, aSort: "spec")
        ]);

        var vUnrecognised = vFigures.SortDistribution.Single(aRow => !aRow.IsRecognised);
        vUnrecognised.Key.Should().Be("weak-checks", "the value is stored as it stands");
        vUnrecognised.Count.Should().Be(1);

        vFigures.SortDistribution
            .Single(aRow => aRow.Key == "weak-check").Count
            .Should().Be(1, "the near-miss value was never filed under the nearest legal one");

        vFigures.SortUnrecognised.Should().Be(1, "it is counted, not merely displayed");
    }

    /// <summary>An unrecognised value is counted in the numerator; it is a sort, just not one of the four.</summary>
    [Fact]
    public void AnUnrecognisedSortIsCountedNotDropped()
    {
        var vFigures = Compute(
        [
            MissFixtures.Miss("MISS-A-1", aTs: AfterFloor, aSort: "spec"),
            MissFixtures.Miss("MISS-A-2", aTs: AfterFloor, aSort: "vibes")
        ]);

        vFigures.SortN.Should().Be(2, "a record that answered is a record that answered");
        vFigures.SortDistribution.Sum(aRow => aRow.Count).Should().Be(2);
        vFigures.SortEligibility.Assessed.Should().Be(2);
    }

    /// <summary>The denominator is the eligible misses, and never the miss count.</summary>
    [Fact(DisplayName = "REQ-FN-107 — the sort denominator counts only the misses eligible to carry the field, never the miss count")]
    public void TheDenominatorIsTheEligibleMisses()
    {
        var vFigures = Compute(
        [
            MissFixtures.Miss("MISS-A-1", aTs: AfterFloor, aSort: "spec"),
            MissFixtures.Miss("MISS-A-2", aTs: AfterFloor),
            MissFixtures.Miss("MISS-A-3", aTs: BeforeFloor),
            MissFixtures.Miss("MISS-A-4", aTs: BeforeFloor)
        ]);

        vFigures.Misses.Should().Be(4);
        vFigures.SortEligibility.Eligible.Should().Be(2, "the two older records left the denominator");
        vFigures.SortEligibility.Assessed.Should().Be(1);
        vFigures.SortEligibility.Since.Should().Be("2026-09-07");
    }

    /// <summary>Records predating the field are stated apart and never pooled as unsorted.</summary>
    [Fact(DisplayName = "REQ-FN-107 — records predating the sort field are stated apart and never pooled as unsorted")]
    public void RecordsPredatingTheFieldAreStatedApart()
    {
        var vFigures = Compute(
        [
            MissFixtures.Miss("MISS-A-1", aTs: AfterFloor, aSort: "spec"),
            MissFixtures.Miss("MISS-A-2", aTs: BeforeFloor),
            MissFixtures.Miss("MISS-A-3", aTs: BeforeFloor)
        ]);

        vFigures.SortEligibility.PredatesField.Should().Be(2);
        vFigures.SortEligibility.Eligible.Should().Be(1);
        (vFigures.SortEligibility.Eligible - vFigures.SortEligibility.Assessed)
            .Should().Be(0, "no record from before the field counts as one that declined to answer");
    }

    /// <summary>The <c>n of N</c> phrase reads against the eligible count, never the miss count.</summary>
    [Fact]
    public void SortedLabelReadsAgainstTheEligibleCount()
    {
        var vFigures = Compute(
        [
            MissFixtures.Miss("MISS-A-1", aTs: AfterFloor, aSort: "spec"),
            MissFixtures.Miss("MISS-A-2", aTs: AfterFloor),
            MissFixtures.Miss("MISS-A-3", aTs: BeforeFloor)
        ]);

        vFigures.SortEligibility.NOfN("sorted").Should().Be("1 of 2 sorted");
    }

    /// <summary>A record whose sort came from an amendment stays in the denominator, whatever its date.</summary>
    [Fact]
    public void AnAmendedOlderRecordIsSortedNotPredating()
    {
        var vFigures = Compute(
            [MissFixtures.Miss("MISS-A-1", aTs: BeforeFloor)],
            [MissFixtures.Amend("MISS-A-1", "unsaid", MissSorts.Field)]);

        vFigures.SortEligibility.PredatesField.Should().Be(0);
        vFigures.SortEligibility.Eligible.Should().Be(1);
        vFigures.SortN.Should().Be(1, "folding an answer in and then discarding it is not a denominator");
        vFigures.SortDistribution.Single().Key.Should().Be("unsaid");
    }

    /// <summary>Every distribution over folded records states how many values an amendment completed.</summary>
    [Fact(DisplayName = "REQ-FN-109 — every distribution over folded records states how many values a miss-amend completed")]
    public void EveryDistributionStatesItsAmendedCount()
    {
        var vFigures = Compute(
            [
                MissFixtures.Miss("MISS-A-1", aMissClass: "unspecified-gap", aTs: BeforeFloor),
                MissFixtures.Miss("MISS-A-2", aMissClass: "unspecified-gap", aTs: AfterFloor, aSort: "spec")
            ],
            [
                MissFixtures.Amend("MISS-A-1", "unsaid", MissSorts.Field),
                MissFixtures.Amend("MISS-A-1", "instruction-ignored", MissAmendFolder.WhyMissedField)
            ]);

        vFigures.AmendedValues.Keys.Should().BeEquivalentTo(
            MissFigures.DistributionFields,
            "a distribution with no count beside it is exactly the silent fold BRD-176 ends");

        vFigures.AmendedValues[MissSorts.Field].Should().Be(1);
        vFigures.AmendedValues[MissAmendFolder.WhyMissedField].Should().Be(1);
        vFigures.AmendedValues[MissFigures.MissClassField]
            .Should().Be(0, "0 is an answer; an absent key is not");
        vFigures.AmendedValues[MissFigures.FoundByField].Should().Be(0);
    }

    /// <summary>An amended count belongs to the segment its parent miss was counted in.</summary>
    [Fact]
    public void AnAmendedCountStaysInItsOwnSegment()
    {
        var vAnalysis = MissFigures.Compute(
            [
                MissFixtures.Miss("MISS-A-1", aTs: AfterFloor, aProjectType: "app"),
                MissFixtures.Miss("MISS-L-1", aTs: AfterFloor, aProjectType: "library")
            ],
            [],
            [MissFixtures.Amend("MISS-L-1", "ignored", MissSorts.Field)],
            []);

        vAnalysis.Live[AppSegment].AmendedValues[MissSorts.Field].Should().Be(0);
        vAnalysis.Live["library"].AmendedValues[MissSorts.Field].Should().Be(1);
        vAnalysis.AmendmentsApplied.Should().Be(1);
    }

    /// <summary>An amendment carrying a value outside the four is an orphan, never a coerced value.</summary>
    [Fact]
    public void AnAmendOutsideTheFourIsAnOrphan()
    {
        var vFold = MissAmendFolder.Fold(
            [MissFixtures.Miss("MISS-A-1", aTs: BeforeFloor)],
            [MissFixtures.Amend("MISS-A-1", "weak-checks", MissSorts.Field)]);

        vFold.Misses.Single().Sort.Should().BeNull("nothing is mapped to the nearest legal value");
        vFold.OrphanAmends.Should().Be(1);
        vFold.AmendmentsApplied.Should().Be(0);
        vFold.CompletedFor(MissSorts.Field).Should().Be(0);
    }

    /// <summary>An empty segment still names the field's floor rather than implying it always existed.</summary>
    [Fact]
    public void AnEmptySortBlockStillCarriesTheFloor()
    {
        var vNone = FieldEligibility.NoneFor(MissSorts.Field);

        vNone.Since.Should().Be("2026-09-07");
        vNone.Eligible.Should().Be(0);
        vNone.PredatesField.Should().Be(0);
    }

    /// <summary>Computes the <c>app</c> segment's figures over the given records.</summary>
    /// <param name="aMisses">The miss records.</param>
    /// <param name="aAmends">The amendment records; none by default.</param>
    /// <returns>The segment block.</returns>
    private static MissSegmentFigures Compute(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissAmendRecord>? aAmends = null) =>
        MissFigures.Compute(aMisses, [], aAmends ?? [], []).Live[AppSegment];
}
