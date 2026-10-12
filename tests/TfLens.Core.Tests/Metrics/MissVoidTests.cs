using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The read-time half of the withdrawal of a miss — <see cref="MissVoid.Apply"/> (SCHEMA.md §5.5.11,
/// REQ-FN-143).
/// </summary>
/// <remarks>
/// The expected shapes are the reference's: <c>analyse_misses</c> in <c>tf-metrics.sh</c> drops every
/// non-review record whose <c>miss_id</c> a void names, publishes <c>"%s — %s" % (miss_id, reason or
/// "no reason recorded")</c> per withdrawn miss, and counts a void naming no miss as an orphan.
/// </remarks>
public sealed class MissVoidTests
{
    private const string Repo = "acme/alpha";
    private const string OtherRepo = "acme/beta";

    /// <summary>
    /// A voided miss leaves the kept list, and so do every fix and amend naming it; the others stay.
    /// </summary>
    [Fact]
    public void VoidDropsMissFixesAndAmends()
    {
        var vResult = MissVoid.Apply(
            [Miss(Repo, "M-1"), Miss(Repo, "M-2")],
            [Fix(Repo, "M-1", "R-1"), Fix(Repo, "M-2", "R-2")],
            [Amend(Repo, "M-1"), Amend(Repo, "M-2")],
            [Void(Repo, "M-1", "written twice")]);

        vResult.Misses.Select(aMiss => aMiss.MissId).Should().Equal("M-2");
        vResult.Fixes.Select(aFix => aFix.MissId).Should().Equal("M-2");
        vResult.Amends.Select(aAmend => aAmend.MissId).Should().Equal("M-2");
    }

    /// <summary>
    /// Each withdrawn miss is described as the reference prints it, and its count is the line count.
    /// </summary>
    [Fact]
    public void VoidedLinesMatchTheReference()
    {
        var vResult = MissVoid.Apply(
            [Miss(Repo, "M-1"), Miss(Repo, "M-2")],
            [],
            [],
            [Void(Repo, "M-1", "written twice"), Void(Repo, "M-2", null)]);

        vResult.Voided.Should().Equal("M-1 — written twice", "M-2 — no reason recorded");
        vResult.VoidedN.Should().Be(2);
    }

    /// <summary>
    /// A void naming no miss on its repository is an orphan: counted, not listed, and removes nothing.
    /// </summary>
    [Fact]
    public void OrphanVoidIsCountedNotApplied()
    {
        var vResult = MissVoid.Apply(
            [Miss(Repo, "M-1")],
            [],
            [],
            [Void(Repo, "M-9", "gone")]);

        vResult.OrphanedN.Should().Be(1);
        vResult.Voided.Should().BeEmpty();
        vResult.Misses.Should().HaveCount(1);
    }

    /// <summary>
    /// A void in one repository never withdraws a miss with the same id in another; it is an orphan there.
    /// </summary>
    [Fact]
    public void VoidStaysInsideItsRepository()
    {
        var vResult = MissVoid.Apply(
            [Miss(OtherRepo, "M-1")],
            [],
            [],
            [Void(Repo, "M-1", "wrong repo")]);

        vResult.Misses.Should().HaveCount(1);
        vResult.OrphanedN.Should().Be(1);
    }

    /// <summary>
    /// With no voids the three lists come back untouched and the three figures are zero.
    /// </summary>
    [Fact]
    public void NoVoidsChangesNothing()
    {
        var vResult = MissVoid.Apply([Miss(Repo, "M-1")], [Fix(Repo, "M-1", "R-1")], [], []);

        vResult.Misses.Should().HaveCount(1);
        vResult.Fixes.Should().HaveCount(1);
        vResult.VoidedN.Should().Be(0);
        vResult.OrphanedN.Should().Be(0);
    }

    /// <summary>
    /// Review records are not an input to the withdrawal at all, so a void can never remove one.
    /// </summary>
    [Fact]
    public void ReviewsAreNeverAnInput()
    {
        var vParameters = typeof(MissVoid).GetMethod(nameof(MissVoid.Apply))!.GetParameters();

        vParameters.Select(aParameter => aParameter.ParameterType)
            .Should().NotContain(typeof(IReadOnlyList<MissReviewRecord>));
    }

    /// <summary>
    /// The stamp carries all three figures onto the miss analysis.
    /// </summary>
    [Fact]
    public void StampCarriesTheThreeFigures()
    {
        var vResult = MissVoid.Apply(
            [Miss(Repo, "M-1")],
            [],
            [],
            [Void(Repo, "M-1", "r"), Void(Repo, "M-7", "orphan")]);

        var vStamped = vResult.Stamp(MissAnalysis.Empty);

        vStamped.MissesVoidedN.Should().Be(1);
        vStamped.MissesVoided.Should().Equal("M-1 — r");
        vStamped.MissVoidsOrphanedN.Should().Be(1);
    }

    private static MissRecord Miss(string aRepo, string aMissId) => new()
    {
        UserId = 1, Repo = aRepo, SourceSha = "sha", Ts = "2026-09-20T10:00:00Z", MissId = aMissId
    };

    private static MissFixRecord Fix(string aRepo, string aMissId, string aRunId) => new()
    {
        UserId = 1, Repo = aRepo, SourceSha = "sha", Ts = "2026-09-20T11:00:00Z", MissId = aMissId,
        FixRunId = aRunId
    };

    private static MissAmendRecord Amend(string aRepo, string aMissId) => new()
    {
        UserId = 1, Repo = aRepo, SourceSha = "sha", Ts = "2026-09-20T12:00:00Z", MissId = aMissId,
        Field = "sort", Value = "spec"
    };

    private static MissVoidRecord Void(string aRepo, string aMissId, string? aReason) => new()
    {
        UserId = 1, Repo = aRepo, SourceSha = "sha", Ts = "2026-09-22T10:00:00Z", MissId = aMissId,
        Reason = aReason
    };
}
