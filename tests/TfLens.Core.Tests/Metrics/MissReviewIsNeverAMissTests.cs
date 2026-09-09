using System.Reflection;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The read half of the fourth record kind — a <c>review</c> reaches no miss count, chart or filter
/// (REQ-FN-108; BRD-174, BRD-113, BRD-114, BRD-115).
/// </summary>
/// <remarks>
/// <para>
/// <c>MissReviewParserTests</c> pins the parse half: a review record no longer falls through the
/// dispatch into <c>InvalidLines</c>. That fix creates the opposite hazard, and this file pins it. A
/// kind the reader now <i>knows</i> is a kind the reader could start counting, and a review counted as a
/// miss would inflate every figure on the page — a record that prices an owner review is not a defect.
/// </para>
/// <para>
/// The guarantee is structural rather than a filter: <see cref="MissFigures.Compute"/> has no parameter
/// a review could arrive through, so there is no code path to get one wrong. The first test asserts that
/// shape, and the rest walk a real mixed stream end to end to prove the shape holds in practice.
/// </para>
/// </remarks>
public sealed class MissReviewIsNeverAMissTests
{
    /// <summary>A stream carrying all four kinds, two misses among them.</summary>
    private const string MixedStream =
        """
        {"v":1,"ts":"2026-09-08T07:00:00Z","kind":"miss","app":"X","miss_id":"MISS-X-1","project_type":"app","miss_class":"unspecified-gap","sort":"spec","found_by":"owner"}
        {"v":1,"ts":"2026-09-08T07:10:00Z","kind":"miss","app":"X","miss_id":"MISS-X-2","project_type":"app","miss_class":"regression","sort":"weak-check","found_by":"gate"}
        {"v":1,"ts":"2026-09-08T08:00:00Z","kind":"miss-fix","app":"X","miss_id":"MISS-X-1","project_type":"app","fix_run_id":"2026-09-08T07:30:00Z","verdict_after":"Verified"}
        {"v":1,"ts":"2026-09-08T09:00:00Z","kind":"miss-amend","app":"X","miss_id":"MISS-X-2","field":"what","value":"the check never ran on the second axis"}
        {"v":1,"ts":"2026-09-08T17:45:14Z","kind":"review","phase":"day1-review","corrections":3,"project_type":"app","app":"X","correction_run_id":"2026-09-08T17:27:15Z","tokens_correct":178645}
        {"v":1,"ts":"2026-09-08T18:45:14Z","kind":"review","phase":"build-review","corrections":9,"project_type":"app","app":"X"}
        """;

    private readonly StreamParser objParser = new();

    /// <summary>
    /// The engine cannot count a review because no argument can carry one to it.
    /// </summary>
    [Fact]
    public void TheMissEngineHasNoDoorAReviewCouldEnterBy()
    {
        var vCompute = typeof(MissFigures).GetMethod(
            nameof(MissFigures.Compute),
            BindingFlags.Public | BindingFlags.Static);

        vCompute.Should().NotBeNull();
        vCompute!.GetParameters()
            .Select(aParameter => aParameter.ParameterType.ToString())
            .Should().NotContain(aName => aName.Contains(nameof(MissReviewRecord), StringComparison.Ordinal),
                "a review is about a phase's output, never about any one miss");
    }

    /// <summary>No result the miss engine returns has anywhere to put a review.</summary>
    [Fact]
    public void NoMissResultTypeCarriesAReview()
    {
        foreach (var vType in new[] { typeof(MissAnalysis), typeof(MissSegmentFigures), typeof(MissFoldResult) })
        {
            vType.GetProperties()
                .Select(aProperty => aProperty.PropertyType.ToString())
                .Should().NotContain(aName => aName.Contains(nameof(MissReviewRecord), StringComparison.Ordinal),
                    vType.Name);
        }
    }

    /// <summary>Two reviews in a stream of six records raise no miss count by one.</summary>
    [Fact(DisplayName = "REQ-FN-108 — review records parse without an InvalidLine and enter no miss count")]
    public void ReviewsEnterNoMissCount()
    {
        var vParsed = Parse(MixedStream);
        var vAnalysis = Analyse(vParsed);

        vParsed.MissReviews.Should().HaveCount(2, "both reviews parsed and neither was discarded");
        vParsed.InvalidLines.Should().Be(0);

        vAnalysis.MissesTotal.Should().Be(2, "only the two miss records are misses");
        vAnalysis.MissFixesTotal.Should().Be(1);
        vAnalysis.OrphanFixes.Should().Be(0);
        vAnalysis.OrphanAmends.Should().Be(0, "a review is not an amendment naming an unknown miss either");
        vAnalysis.Live["app"].Misses.Should().Be(2);
    }

    /// <summary>No chart row anywhere is keyed by a review phase or a corrections count.</summary>
    [Fact(DisplayName = "REQ-FN-108 — review records enter no chart on /misses and no sort denominator")]
    public void ReviewsEnterNoChart()
    {
        var vSegment = Analyse(Parse(MixedStream)).Live["app"];

        var vEveryRow = vSegment.ClassDistribution
            .Concat(vSegment.SortDistribution)
            .Concat(vSegment.FoundBy)
            .Concat(vSegment.FailedPracticeDistribution)
            .Concat(vSegment.Attribution.ByOriginPhase)
            .Select(aRow => aRow.Key)
            .ToList();

        vEveryRow.Should().NotIntersectWith(MissReviewPhases.All);
        vSegment.SortDistribution.Sum(aRow => aRow.Count)
            .Should().Be(2, "the sort denominator counts misses, not reviews");
    }

    /// <summary>A review never becomes a record a period or segment filter could select.</summary>
    [Fact(DisplayName = "REQ-FN-108 — review records enter no period or segment filter a reader could select")]
    public void ReviewsEnterNoFilter()
    {
        var vParsed = Parse(MixedStream);

        vParsed.Misses.Select(aMiss => aMiss.MissId)
            .Should().BeEquivalentTo(["MISS-X-1", "MISS-X-2"]);
        vParsed.MissFixes.Should().HaveCount(1);
        vParsed.MissAmends.Should().HaveCount(1);
    }

    /// <summary>Reviews are counted, and counted apart — beside the miss figures and never inside them.</summary>
    [Fact]
    public void ReviewsAreCountedApartOnTheDataQualityBlock()
    {
        var vParsed = Parse(MixedStream);
        var vAnalysis = Analyse(vParsed);

        var vQuality = DataQualityFigures.Compute(
            vParsed.Misses, vParsed.MissAmends, vParsed.MissReviews, [], [], vAnalysis);

        vQuality.Diagnostics.ReviewRecords.Should().Be(2);
        vAnalysis.MissesTotal.Should().Be(2, "the two counts are two facts and are never one");
    }

    /// <summary>Runs the miss engine over a parse result, exactly as a reader page does.</summary>
    /// <param name="aParsed">The parse result.</param>
    /// <returns>The miss block.</returns>
    private static MissAnalysis Analyse(ParseResult aParsed) =>
        MissFigures.Compute(aParsed.Misses, aParsed.MissFixes, aParsed.MissAmends, []);

    /// <summary>Parses inline JSONL text as the misses stream.</summary>
    /// <param name="aText">The lines to parse.</param>
    /// <returns>The parse result.</returns>
    private ParseResult Parse(string aText) =>
        objParser.Parse(Fixtures.DemoUserId, "owner/name", Fixtures.SourceSha, StreamKind.Misses, aText);
}
