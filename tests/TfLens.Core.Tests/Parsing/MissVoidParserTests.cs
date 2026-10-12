using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Parsing;

/// <summary>
/// The fifth dispatch arm on the misses stream — <c>kind: "miss-void"</c> (SCHEMA.md §5.5.11, REQ-FN-143).
/// </summary>
/// <remarks>
/// Until this arm existed every <c>miss-void</c> fell into the default case: counted as an invalid line
/// and discarded, while the miss it withdrew stayed in every figure. These tests pin the parse half of the
/// fix; <c>MissVoidTests</c> pins the read-time half.
/// </remarks>
public sealed class MissVoidParserTests
{
    /// <summary>The shape <c>tf-emit.sh --void-miss</c> writes.</summary>
    private const string LiveVoid =
        """{"v":1,"ts":"2026-09-22T10:00:00Z","kind":"miss-void","app":"X","project_type":"library","harness":"claude-code","miss_id":"MISS-X-1","reason":"duplicate of MISS-X-2"}""";

    private readonly StreamParser objParser = new();

    /// <summary>
    /// A <c>miss-void</c> line parses into its own list and is not counted as an invalid line.
    /// </summary>
    [Fact]
    public void VoidIsNotAnInvalidLine()
    {
        var vResult = Parse(LiveVoid);

        vResult.MissVoids.Should().HaveCount(1);
        vResult.InvalidLines.Should().Be(0, "a miss-void is a kind TfLens knows, not a malformed line");
    }

    /// <summary>
    /// A <c>miss-void</c> is never parsed as a miss, fix, amend or review.
    /// </summary>
    [Fact]
    public void VoidIsNeverAMiss()
    {
        var vResult = Parse(LiveVoid);

        vResult.Misses.Should().BeEmpty();
        vResult.MissFixes.Should().BeEmpty();
        vResult.MissAmends.Should().BeEmpty();
        vResult.MissReviews.Should().BeEmpty();
    }

    /// <summary>
    /// <c>miss_id</c>, <c>reason</c> and the envelope map to columns, and nothing reaches Overflow.
    /// </summary>
    [Fact]
    public void VoidFieldsMapToColumns()
    {
        var vVoid = Parse(LiveVoid).MissVoids.Single();

        vVoid.MissId.Should().Be("MISS-X-1");
        vVoid.Reason.Should().Be("duplicate of MISS-X-2");
        vVoid.App.Should().Be("X");
        vVoid.ProjectType.Should().Be("library");
        vVoid.Harness.Should().Be("claude-code");
        vVoid.Overflow.Should().BeNull("every documented miss-void field has a column");
        StreamParser.IsDocumented(StreamKind.Misses, "reason").Should().BeTrue();
    }

    /// <summary>
    /// Two voids of one miss collapse to one, keeping the earliest <c>ts</c>.
    /// </summary>
    [Fact]
    public void DuplicateVoidKeepsEarliest()
    {
        var vText = string.Join(
            '\n',
            """{"v":1,"ts":"2026-09-23T10:00:00Z","kind":"miss-void","miss_id":"MISS-X-1","reason":"later"}""",
            """{"v":1,"ts":"2026-09-22T10:00:00Z","kind":"miss-void","miss_id":"MISS-X-1","reason":"earlier"}""");

        var vResult = Parse(vText);

        vResult.MissVoids.Should().ContainSingle().Which.Reason.Should().Be("earlier");
        vResult.DuplicatesCollapsed.Should().Be(1);
    }

    /// <summary>
    /// All five kinds parse from one file in one pass, and the record count includes the void.
    /// </summary>
    [Fact]
    public void AllFiveKindsParseFromOneFile()
    {
        var vText = string.Join(
            '\n',
            """{"v":1,"ts":"2026-09-07T07:00:00Z","kind":"miss","app":"X","miss_id":"MISS-X-1"}""",
            """{"v":1,"ts":"2026-09-07T08:00:00Z","kind":"miss-fix","app":"X","miss_id":"MISS-X-1","fix_run_id":"2026-09-07T07:30:00Z"}""",
            """{"v":1,"ts":"2026-09-07T09:00:00Z","kind":"miss-amend","app":"X","miss_id":"MISS-X-1","field":"sort","value":"spec"}""",
            """{"v":1,"ts":"2026-09-07T10:00:00Z","kind":"review","app":"X","phase":"build-review","corrections":1}""",
            LiveVoid);

        var vResult = Parse(vText);

        vResult.RecordCount.Should().Be(5);
        vResult.InvalidLines.Should().Be(0);
    }

    /// <summary>Parses inline JSONL text as the misses stream.</summary>
    /// <param name="aText">The lines to parse.</param>
    /// <returns>The parse result.</returns>
    private ParseResult Parse(string aText) =>
        objParser.Parse(Fixtures.DemoUserId, "owner/name", Fixtures.SourceSha, StreamKind.Misses, aText);
}
