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
        MissAmendFolder.AmendableFields.Keys.Should().Equal("why_missed");
        MissAmendFolder.AmendableFields.Should().NotContainKey("origin_model");
        MissAmendFolder.AmendableFields.Should().NotContainKey("origin_confidence");
        MissAmendFolder.AmendableFields.Should().NotContainKey("cost_attribution");
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
