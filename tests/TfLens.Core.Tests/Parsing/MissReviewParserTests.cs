using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Parsing;

/// <summary>
/// The fourth dispatch arm on the misses stream — <c>kind: "review"</c> (REQ-FN-072, BRD-113, BRD-174).
/// </summary>
/// <remarks>
/// <para>
/// Until this arm existed, every <c>review</c> record fell through the dispatch's default case into
/// <c>InvalidLines</c> and was discarded: a counter of mistakes silently throwing away the records that
/// price them. These tests pin the two halves of the fix — a review parses into its own record type, and
/// it reaches <b>neither</b> <c>InvalidLines</c> <b>nor</b> any miss list.
/// </para>
/// <para>
/// The record used throughout is the shape the estate actually emits: <c>reviewed_run_id</c> absent,
/// which is also the case the dedupe key has to <c>COALESCE</c>.
/// </para>
/// </remarks>
public sealed class MissReviewParserTests
{
    /// <summary>The live shape: a day-1 review with no <c>reviewed_run_id</c> and a correction run.</summary>
    private const string LiveReview =
        """
        {"kind":"review","phase":"day1-review","correction_run_id":"2026-09-08T17:27:15Z","corrections":3,
        "what":"the owner corrected the prompt's rules","v":1,"ts":"2026-09-08T17:45:14Z",
        "project_type":"app","harness":"claude-code","app":"TfLens","tokens_produce":null,
        "cost_produce_usd":null,"model_produce":null,"tokens_correct":178645,"cost_correct_usd":null,
        "model_correct":"claude-opus-5"}
        """;

    private readonly StreamParser objParser = new();

    /// <summary>The stream declares four kinds, and <c>review</c> is the fourth.</summary>
    [Fact]
    public void ReviewIsTheFourthMissStreamKind()
    {
        MissKinds.Review.Should().Be("review");
        MissReviewPhases.All.Should().Equal(
            "day1-review", "build-review", "verify-review", "handoff-review");
    }

    /// <summary>A review record dispatches to its own list and never reaches <c>InvalidLines</c>.</summary>
    [Fact]
    public void ReviewParsesAndNeverReachesInvalidLines()
    {
        var vResult = Parse(OneLine(LiveReview));

        vResult.MissReviews.Should().HaveCount(1);
        vResult.InvalidLines.Should().Be(0, "a review is a kind TfLens knows, not a malformed line");
    }

    /// <summary>A review is never counted as a miss, a fix or an amendment.</summary>
    [Fact]
    public void ReviewIsNeverCountedAsAMiss()
    {
        var vResult = Parse(OneLine(LiveReview));

        vResult.Misses.Should().BeEmpty("a review is about a phase's output, not about any one miss");
        vResult.MissFixes.Should().BeEmpty();
        vResult.MissAmends.Should().BeEmpty();
    }

    /// <summary>All four kinds parse out of one file in a single pass.</summary>
    [Fact]
    public void AllFourKindsParseFromOneFileInOnePass()
    {
        var vText = string.Join(
            '\n',
            """{"v":1,"ts":"2026-09-07T07:00:00Z","kind":"miss","app":"X","miss_id":"MISS-X-1"}""",
            """{"v":1,"ts":"2026-09-07T08:00:00Z","kind":"miss-fix","app":"X","miss_id":"MISS-X-1","fix_run_id":"2026-09-07T07:30:00Z"}""",
            """{"v":1,"ts":"2026-09-07T09:00:00Z","kind":"miss-amend","app":"X","miss_id":"MISS-X-1","field":"sort","value":"spec"}""",
            OneLine(LiveReview));

        var vResult = Parse(vText);

        vResult.Misses.Should().HaveCount(1);
        vResult.MissFixes.Should().HaveCount(1);
        vResult.MissAmends.Should().HaveCount(1);
        vResult.MissReviews.Should().HaveCount(1);
        vResult.RecordCount.Should().Be(4);
        vResult.InvalidLines.Should().Be(0);
    }

    /// <summary>An unknown kind is still counted and skipped, never thrown, beside the four known ones.</summary>
    [Fact]
    public void AnUnknownKindIsStillCountedAndSkipped()
    {
        var vText = string.Join(
            '\n',
            OneLine(LiveReview),
            """{"v":1,"ts":"2026-09-07T10:00:00Z","kind":"review-elsewhere","app":"X","phase":"day1-review"}""");

        var vResult = Parse(vText);

        vResult.MissReviews.Should().HaveCount(1);
        vResult.InvalidLines.Should().Be(1, "the fourth arm did not turn the default arm off");
    }

    /// <summary>Every wire field maps to a column, and an absent one stays <c>null</c>.</summary>
    [Fact]
    public void ReviewFieldsMapToColumnsAndAbsentOnesStayNull()
    {
        var vReview = Parse(OneLine(LiveReview)).MissReviews.Single();

        vReview.ReviewPhase.Should().Be("day1-review");
        vReview.CorrectionRunId.Should().Be("2026-09-08T17:27:15Z");
        vReview.Corrections.Should().Be(3);
        vReview.What.Should().Be("the owner corrected the prompt's rules");
        vReview.TokensCorrect.Should().Be(178645);
        vReview.ModelCorrect.Should().Be("claude-opus-5");
        vReview.App.Should().Be("TfLens");
        vReview.Harness.Should().Be("claude-code");
        vReview.ProjectType.Should().Be("app");

        vReview.ProducedRunId.Should().BeNull("the reviewed output named no run");
        vReview.TokensProduce.Should().BeNull("an uncaptured produce cost is not a free one");
        vReview.CostProduceUsd.Should().BeNull();
        vReview.ModelProduce.Should().BeNull();
        vReview.CostCorrectUsd.Should().BeNull();
        vReview.Overflow.Should().BeNull("every documented review field has a column");
    }

    /// <summary>A property with no column reaches <c>Overflow</c> and is reported once.</summary>
    [Fact]
    public void UnknownReviewPropertiesReachOverflow()
    {
        const string vText =
            """{"v":1,"ts":"2026-09-08T17:45:14Z","kind":"review","phase":"build-review","corrections":1,"mood":"patient"}""";

        var vResult = Parse(vText);

        vResult.MissReviews.Single().Overflow.Should().NotBeNull()
            .And.Subject.ToString()!.Should().Contain("mood");
        vResult.UnknownFields.Should().Contain("mood");
    }

    /// <summary>
    /// <c>IsDocumented</c> takes the union of all four vocabularies, so no review field is reported as
    /// one SCHEMA.md does not document.
    /// </summary>
    [Fact]
    public void IsDocumentedTakesTheUnionOfAllFourVocabularies()
    {
        foreach (var vField in new[]
        {
            "phase", "reviewed_run_id", "correction_run_id", "corrections", "what",
            "tokens_produce", "cost_produce_usd", "model_produce",
            "tokens_correct", "cost_correct_usd", "model_correct"
        })
        {
            StreamParser.IsDocumented(StreamKind.Misses, vField).Should().BeTrue(vField);
        }

        StreamParser.IsDocumented(StreamKind.Misses, "mood").Should().BeFalse();
    }

    /// <summary>A <c>v &gt; 1</c> review keeps only its identity columns and its payload.</summary>
    [Fact]
    public void ReviewAboveSchemaV1IsPreservedWhole()
    {
        const string vText =
            """{"v":2,"ts":"2026-09-08T17:45:14Z","kind":"review","phase":"verify-review","corrections":9}""";

        var vResult = Parse(vText);

        vResult.RecordsAboveSchemaV1.Should().Be(1);
        var vReview = vResult.MissReviews.Single();
        vReview.ReviewPhase.Should().Be("verify-review");
        vReview.Corrections.Should().BeNull("a v>1 record keeps only its identity columns");
        vReview.Overflow.Should().NotBeNull().And.Subject.ToString()!.Should().Contain("corrections");
    }

    /// <summary>Flattens a multi-line raw string into the single JSONL line the stream really carries.</summary>
    /// <param name="aJson">The pretty-printed literal.</param>
    /// <returns>One line of JSON.</returns>
    private static string OneLine(string aJson) =>
        string.Concat(aJson.Split('\n').Select(aLine => aLine.Trim()));

    /// <summary>Parses inline JSONL text as the misses stream.</summary>
    /// <param name="aText">The lines to parse.</param>
    /// <returns>The parse result.</returns>
    private ParseResult Parse(string aText) =>
        objParser.Parse(Fixtures.DemoUserId, "owner/name", Fixtures.SourceSha, StreamKind.Misses, aText);
}
