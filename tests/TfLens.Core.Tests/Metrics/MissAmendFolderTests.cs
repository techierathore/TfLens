using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The read-time amendment fold and the invariant the whole record kind stands on (REQ-FN-075, BRD-116).
/// </summary>
/// <remarks>
/// An amend may fill a <c>null</c> and may never overwrite a value — and that has to hold <b>whichever
/// order the two records arrive in</b>, because TfLens ingests archived files from many machines. Every
/// order-sensitive case below is asserted in both directions for exactly that reason.
/// </remarks>
public sealed class MissAmendFolderTests
{
    /// <summary>An amend fills a field the miss left null.</summary>
    [Fact]
    public void AmendFillsANullField()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-08-28T09:00:00Z")],
            [Amend("MISS-A-1", "why_missed", "instruction-ignored", "2026-08-28T10:00:00Z")]);

        vResult.Misses.Single().WhyMissed.Should().Be("instruction-ignored");
        vResult.AmendmentsApplied.Should().Be(1);
        vResult.OrphanAmends.Should().Be(0);
    }

    /// <summary>An amend never overwrites a non-null value, whichever order the records arrive in.</summary>
    [Fact]
    public void AmendNeverOverwritesANonNullValue()
    {
        var vMiss = Miss("MISS-A-1", "other", "2026-08-28T09:00:00Z");
        var vAmend = Amend("MISS-A-1", "why_missed", "instruction-ignored", "2026-08-28T10:00:00Z");
        var vEarlierAmend = Amend("MISS-A-1", "why_missed", "instruction-ignored", "2026-08-27T10:00:00Z");

        var vAmendAfter = MissAmendFolder.Fold([vMiss], [vAmend]);
        var vAmendBefore = MissAmendFolder.Fold([vMiss], [vEarlierAmend]);

        vAmendAfter.Misses.Single().WhyMissed.Should().Be("other");
        vAmendBefore.Misses.Single().WhyMissed.Should().Be(
            "other", "arrival order cannot decide whether an amend becomes an edit");
        vAmendAfter.AmendmentsApplied.Should().Be(0);
        vAmendAfter.AmendmentsIgnored.Should().Be(1);
    }

    /// <summary>A second amend of the same field is ignored, in either order.</summary>
    [Fact]
    public void SecondAmendOfTheSameFieldIsIgnored()
    {
        var vFirst = Amend("MISS-A-1", "why_missed", "instruction-ignored", "2026-08-28T10:00:00Z");
        var vSecond = Amend("MISS-A-1", "why_missed", "other", "2026-08-28T11:00:00Z");

        var vForward = MissAmendFolder.Fold([Miss("MISS-A-1", null, "2026-08-28T09:00:00Z")], [vFirst, vSecond]);
        var vReverse = MissAmendFolder.Fold([Miss("MISS-A-1", null, "2026-08-28T09:00:00Z")], [vSecond, vFirst]);

        vForward.Misses.Single().WhyMissed.Should().Be("instruction-ignored", "amendments fold oldest first");
        vReverse.Misses.Single().WhyMissed.Should().Be("instruction-ignored", "and the input order does not matter");
        vForward.AmendmentsApplied.Should().Be(1);
        vForward.AmendmentsIgnored.Should().Be(1);
    }

    /// <summary>A field off the allowlist is never applied and counts as an orphan.</summary>
    [Fact]
    public void FieldOffTheAllowlistIsAnOrphan()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-08-28T09:00:00Z")],
            [Amend("MISS-A-1", "found_gate", "render", "2026-08-28T10:00:00Z")]);

        vResult.Misses.Single().FoundGate.Should().BeNull("an observation is never amendable");
        vResult.AmendmentsApplied.Should().Be(0);
        vResult.Orphans.Single().Reason.Should().Be(MissAmendOrphanReasons.FieldNotAllowlisted);
    }

    /// <summary>An emitter-derived field can never be amended, which is the hole the allowlist closes.</summary>
    [Fact]
    public void EmitterDerivedFieldsAreNotAmendable()
    {
        MissAmendFolder.AmendableFields.Keys.Should().Equal("why_missed", "sort");
        MissAmendFolder.AmendableFreeTextFields.Should().Equal("what");
        MissAmendFolder.AmendableFields.Should().NotContainKey("origin_model");
        MissAmendFolder.AmendableFields.Should().NotContainKey("origin_confidence");
        MissAmendFolder.AmendableFields.Should().NotContainKey("cost_attribution");
        MissAmendFolder.IsAmendable("origin_model").Should().BeFalse();
        MissAmendFolder.IsAmendable("origin_confidence").Should().BeFalse();
        MissAmendFolder.IsAmendable("cost_attribution").Should().BeFalse();
    }

    /// <summary>A value outside the field's closed vocabulary is never applied and counts as an orphan.</summary>
    [Fact]
    public void ValueOutsideTheVocabularyIsAnOrphan()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-08-28T09:00:00Z")],
            [Amend("MISS-A-1", "why_missed", "we were in a hurry", "2026-08-28T10:00:00Z")]);

        vResult.Misses.Single().WhyMissed.Should().BeNull("the kind is never a free-text back door");
        vResult.Orphans.Single().Reason.Should().Be(MissAmendOrphanReasons.ValueOutsideVocabulary);
    }

    /// <summary>An amend naming no known miss counts as an orphan and is never applied.</summary>
    [Fact]
    public void AmendNamingNoKnownMissIsAnOrphan()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-08-28T09:00:00Z")],
            [Amend("MISS-NOPE-9", "why_missed", "other", "2026-08-28T10:00:00Z")]);

        vResult.Misses.Single().WhyMissed.Should().BeNull();
        vResult.AmendmentsApplied.Should().Be(0);
        vResult.OrphanAmends.Should().Be(1);
        vResult.Orphans.Single().Reason.Should().Be(MissAmendOrphanReasons.UnknownMiss);
    }

    /// <summary>An amend never reaches a miss of the same id in another repository.</summary>
    [Fact]
    public void AmendDoesNotCrossRepositories()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-08-28T09:00:00Z", "owner/one")],
            [Amend("MISS-A-1", "why_missed", "other", "2026-08-28T10:00:00Z", "owner/two")]);

        vResult.Misses.Single().WhyMissed.Should().BeNull();
        vResult.Orphans.Single().Reason.Should().Be(MissAmendOrphanReasons.UnknownMiss);
    }

    /// <summary>A why_missed supplied only by an amend is eligible for the failed-practice distribution.</summary>
    [Fact]
    public void AmendedWhyMissedReachesTheFailedPracticeDenominator()
    {
        var vFolded = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-08-28T09:00:00Z")],
            [Amend("MISS-A-1", "why_missed", "ambiguous-acceptance", "2026-08-28T10:00:00Z")]);

        var vEligibility = LateGateCoverageCalculator.EligibilityFor(
            "why_missed", vFolded.Misses, aM => aM.Ts, aM => aM.WhyMissed);

        vEligibility.Assessed.Should().Be(1, "folding happens before anything is counted");
        vEligibility.Eligible.Should().Be(1);
    }

    /// <summary>The fold never mutates the records it was given, so a rebuild re-derives the same values.</summary>
    [Fact]
    public void FoldingLeavesTheStoredRecordsUntouched()
    {
        var vStored = new[] { Miss("MISS-A-1", null, "2026-08-28T09:00:00Z") };
        var vAmends = new[] { Amend("MISS-A-1", "why_missed", "other", "2026-08-28T10:00:00Z") };

        var vFirst = MissAmendFolder.Fold(vStored, vAmends);
        var vSecond = MissAmendFolder.Fold(vStored, vAmends);

        vStored[0].WhyMissed.Should().BeNull("the stored row is the source of truth and is never edited");
        vSecond.Misses.Single().WhyMissed.Should().Be(vFirst.Misses.Single().WhyMissed);
        vSecond.AmendmentsApplied.Should().Be(vFirst.AmendmentsApplied);
    }

    /// <summary>A fold over nothing is an honest nothing, not a throw.</summary>
    [Fact]
    public void FoldingNothingReturnsNothing()
    {
        var vResult = MissAmendFolder.Fold([], []);

        vResult.Misses.Should().BeEmpty();
        vResult.AmendmentsApplied.Should().Be(0);
        vResult.OrphanAmends.Should().Be(0);
    }

    /// <summary>The fixture's amendments fold exactly as the stream describes them.</summary>
    [Fact]
    public void TheFixtureStreamFoldsToOneAppliedAndOneOrphan()
    {
        var vParser = new global::TfLens.Core.Parsing.StreamParser();
        var vParsed = vParser.Parse(
            Fixtures.DemoUserId,
            Fixtures.TrSetupRepo,
            Fixtures.SourceSha,
            StreamKind.Misses,
            Fixtures.Read(Fixtures.TrSetupRepo, StreamKind.Misses));

        var vResult = MissAmendFolder.Fold(vParsed.Misses, vParsed.MissAmends);

        vResult.AmendmentsApplied.Should().Be(1);
        vResult.OrphanAmends.Should().Be(1);
        vResult.Misses.Single(aM => aM.MissId == "MISS-TrSetup-20260825-01")
            .WhyMissed.Should().Be("instruction-ignored");
    }

    /// <summary>
    /// <c>sort</c> is amendable in its closed four-value vocabulary (BRD-116, added 2026-09-08).
    /// </summary>
    /// <remarks>
    /// This is the case the clause was written for: most amendments in the estate today complete
    /// <c>sort</c> on records written before the field existed.
    /// </remarks>
    [Theory]
    [InlineData("spec")]
    [InlineData("unsaid")]
    [InlineData("weak-check")]
    [InlineData("ignored")]
    public void AmendFillsANullSort(string aValue)
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-09-07T09:00:00Z")],
            [Amend("MISS-A-1", "sort", aValue, "2026-09-08T10:00:00Z")]);

        vResult.Misses.Single().Sort.Should().Be(aValue);
        vResult.AmendmentsApplied.Should().Be(1);
        vResult.OrphanAmends.Should().Be(0);
    }

    /// <summary>
    /// <b>An out-of-vocabulary <c>sort</c> is an orphan, and is never coerced to the nearest legal
    /// value</b> (BRD-116, BRD-170).
    /// </summary>
    [Fact]
    public void SortOutsideTheVocabularyIsAnOrphanAndIsNeverCoerced()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-09-07T09:00:00Z")],
            [Amend("MISS-A-1", "sort", "specification", "2026-09-08T10:00:00Z")]);

        var vMiss = vResult.Misses.Single();
        vMiss.Sort.Should().BeNull("an unrecognised judgement is surfaced, never filed under 'spec'");
        vResult.AmendmentsApplied.Should().Be(0);
        vResult.OrphanAmends.Should().Be(1);

        var vOrphan = vResult.Orphans.Single();
        vOrphan.Reason.Should().Be(MissAmendOrphanReasons.ValueOutsideVocabulary);
        vOrphan.Field.Should().Be("sort");
        vOrphan.Value.Should().Be("specification", "the reader is shown the value that was refused");
    }

    /// <summary>An amend never overwrites a <c>sort</c> the record already carries, either order.</summary>
    [Fact]
    public void AmendNeverOverwritesAnExistingSort()
    {
        var vMiss = Miss("MISS-A-1", null, "2026-09-07T09:00:00Z") with { Sort = "unsaid" };
        var vLater = Amend("MISS-A-1", "sort", "spec", "2026-09-08T10:00:00Z");
        var vEarlier = Amend("MISS-A-1", "sort", "spec", "2026-09-06T10:00:00Z");

        MissAmendFolder.Fold([vMiss], [vLater]).Misses.Single().Sort.Should().Be("unsaid");
        MissAmendFolder.Fold([vMiss], [vEarlier]).Misses.Single().Sort.Should().Be(
            "unsaid", "arrival order cannot decide whether an amend becomes an edit");
    }

    /// <summary><c>what</c> is amendable as free prose — there is no vocabulary to close (BRD-116).</summary>
    [Fact]
    public void AmendFillsANullWhatAsFreeProse()
    {
        const string vSentence = "the export wrote one snapshot and silently defaulted its framework";

        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-09-07T09:00:00Z")],
            [Amend("MISS-A-1", "what", vSentence, "2026-09-08T10:00:00Z")]);

        vResult.Misses.Single().What.Should().Be(vSentence);
        vResult.AmendmentsApplied.Should().Be(1);
        vResult.OrphanAmends.Should().Be(0);
    }

    /// <summary>A blank free-text amendment completes nothing and is an orphan.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankFreeTextIsAnOrphan(string aValue)
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-09-07T09:00:00Z")],
            [Amend("MISS-A-1", "what", aValue, "2026-09-08T10:00:00Z")]);

        vResult.Misses.Single().What.Should().BeNull("a blank sentence would make a record look answered");
        vResult.Orphans.Single().Reason.Should().Be(MissAmendOrphanReasons.ValueOutsideVocabulary);
    }

    /// <summary>A free-text allowlist entry does not open every other field to free text.</summary>
    [Fact]
    public void FreeTextDoesNotOpenTheOtherFields()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-09-07T09:00:00Z")],
            [Amend("MISS-A-1", "severity", "quite bad really", "2026-09-08T10:00:00Z")]);

        vResult.Misses.Single().Severity.Should().BeNull();
        vResult.Orphans.Single().Reason.Should().Be(MissAmendOrphanReasons.FieldNotAllowlisted);
    }

    /// <summary>The three amendable fields fold independently, oldest first, in one pass.</summary>
    [Fact]
    public void TheThreeAmendableFieldsFoldIndependently()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-09-07T09:00:00Z")],
            [
                Amend("MISS-A-1", "sort", "weak-check", "2026-09-08T10:00:00Z"),
                Amend("MISS-A-1", "what", "the check existed and did not fire", "2026-09-08T10:01:00Z"),
                Amend("MISS-A-1", "why_missed", "insufficient-verify-method", "2026-09-08T10:02:00Z")
            ]);

        var vMiss = vResult.Misses.Single();
        vMiss.Sort.Should().Be("weak-check");
        vMiss.What.Should().Be("the check existed and did not fire");
        vMiss.WhyMissed.Should().Be("insufficient-verify-method");
        vResult.AmendmentsApplied.Should().Be(3);
        vResult.OrphanAmends.Should().Be(0);
    }

    /// <summary>A second amendment of <c>sort</c> is ignored rather than counted as an orphan.</summary>
    [Fact]
    public void ASecondSortAmendmentIsIgnoredNotAnOrphan()
    {
        var vResult = MissAmendFolder.Fold(
            [Miss("MISS-A-1", null, "2026-09-07T09:00:00Z")],
            [
                Amend("MISS-A-1", "sort", "spec", "2026-09-08T10:00:00Z"),
                Amend("MISS-A-1", "sort", "ignored", "2026-09-08T11:00:00Z")
            ]);

        vResult.Misses.Single().Sort.Should().Be("spec", "oldest first, and the second finds a value");
        vResult.AmendmentsApplied.Should().Be(1);
        vResult.AmendmentsIgnored.Should().Be(1);
        vResult.OrphanAmends.Should().Be(0);
    }

    /// <summary>Re-folding the same rows re-derives identical values, which is what a rebuild does.</summary>
    [Fact]
    public void ReFoldingSortAndWhatReDerivesIdenticalValues()
    {
        var vStored = new[] { Miss("MISS-A-1", null, "2026-09-07T09:00:00Z") };
        var vAmends = new[]
        {
            Amend("MISS-A-1", "sort", "ignored", "2026-09-08T10:00:00Z"),
            Amend("MISS-A-1", "what", "the rule was written and not followed", "2026-09-08T10:01:00Z")
        };

        var vFirst = MissAmendFolder.Fold(vStored, vAmends);
        var vSecond = MissAmendFolder.Fold(vStored, vAmends);

        vStored[0].Sort.Should().BeNull("the stored row is the source of truth and is never edited");
        vStored[0].What.Should().BeNull();
        vSecond.Misses.Single().Sort.Should().Be(vFirst.Misses.Single().Sort);
        vSecond.Misses.Single().What.Should().Be(vFirst.Misses.Single().What);
    }

    /// <summary>
    /// <b>The acceptance line.</b> Amendments read on <c>/misses</c> are folded oldest first into a
    /// <c>null</c> field only, and a rebuild — which folds the same stored rows again — re-derives
    /// identical values (REQ-FN-075, BRD-116).
    /// </summary>
    /// <remarks>
    /// The three clauses are one behaviour and are asserted together here: the older of two amendments of
    /// the same null field wins, a field the miss already answers is left alone, and neither the stored
    /// rows nor the folded values change when the fold is run a second time — which is exactly what
    /// <c>RebuildAsync</c> does after replaying the raw archive. The amendments are supplied newest-first
    /// so that "oldest wins" cannot be an accident of input order.
    /// </remarks>
    [Fact(DisplayName =
        "REQ-FN-075 — amendments fold oldest-first into a null field only, and a rebuild re-derives "
        + "identical values")]
    public void AmendmentsFoldOldestFirstIntoANullFieldOnlyAndARebuildReDerivesIdenticalValues()
    {
        var vStored = new[]
        {
            Miss("MISS-A-1", null, "2026-09-07T09:00:00Z") with { Sort = "unsaid" }
        };
        var vAmends = new[]
        {
            Amend("MISS-A-1", "why_missed", "other", "2026-09-08T12:00:00Z"),
            Amend("MISS-A-1", "why_missed", "instruction-ignored", "2026-09-08T10:00:00Z"),
            Amend("MISS-A-1", "sort", "spec", "2026-09-08T11:00:00Z")
        };

        var vFirst = MissAmendFolder.Fold(vStored, vAmends);

        var vFolded = vFirst.Misses.Single();
        vFolded.WhyMissed.Should().Be("instruction-ignored", "amendments fold oldest first");
        vFolded.Sort.Should().Be("unsaid", "an amend fills a null and never overwrites an answered field");
        vFirst.AmendmentsApplied.Should().Be(1);
        vFirst.AmendmentsIgnored.Should().Be(2, "the later why_missed and the sort both found a value");
        vFirst.OrphanAmends.Should().Be(0);

        // What a rebuild does: replay the same stored rows and fold them again.
        var vSecond = MissAmendFolder.Fold(vStored, vAmends);

        vStored[0].WhyMissed.Should().BeNull("the stored row is never edited, so the fold stays re-derivable");
        vStored[0].Sort.Should().Be("unsaid");
        vSecond.Misses.Single().WhyMissed.Should().Be(vFolded.WhyMissed);
        vSecond.Misses.Single().Sort.Should().Be(vFolded.Sort);
        vSecond.AmendmentsApplied.Should().Be(vFirst.AmendmentsApplied);
        vSecond.AmendmentsIgnored.Should().Be(vFirst.AmendmentsIgnored);
    }

    /// <summary>Builds a miss carrying only what the fold reads.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aWhyMissed">The stored value of the amendable field, or <c>null</c>.</param>
    /// <param name="aTs">The timestamp.</param>
    /// <param name="aRepo">The repository the record came from.</param>
    /// <returns>The record.</returns>
    private static MissRecord Miss(string aMissId, string? aWhyMissed, string aTs, string aRepo = "owner/name") => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = aRepo,
        SourceSha = Fixtures.SourceSha,
        Ts = aTs,
        MissId = aMissId,
        WhyMissed = aWhyMissed
    };

    /// <summary>Builds an amendment carrying only what the fold reads.</summary>
    /// <param name="aMissId">The miss it names.</param>
    /// <param name="aField">The field it tries to complete.</param>
    /// <param name="aValue">The value it carries.</param>
    /// <param name="aTs">The timestamp; amendments fold oldest first.</param>
    /// <param name="aRepo">The repository the record came from.</param>
    /// <returns>The record.</returns>
    private static MissAmendRecord Amend(
        string aMissId, string aField, string aValue, string aTs, string aRepo = "owner/name") => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = aRepo,
        SourceSha = Fixtures.SourceSha,
        Ts = aTs,
        MissId = aMissId,
        Field = aField,
        Value = aValue
    };
}
