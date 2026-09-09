using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Parsing;

/// <summary>
/// The per-<c>kind</c> dispatch inside <see cref="StreamKind.Misses"/> (REQ-FN-071, REQ-FN-072).
/// </summary>
/// <remarks>
/// <c>misses.jsonl</c> is the first stream whose records do not all share a shape, so these tests are
/// about the one structural change it forces on the parser: all four kinds come out of one file in one
/// pass, an unknown kind is counted rather than thrown, absent optionals stay <c>null</c>, and
/// <c>IsDocumented</c> answers over the union of the four vocabularies. The fourth kind, <c>review</c>,
/// has its own class — <c>MissReviewParserTests</c>.
/// </remarks>
public sealed class MissStreamParserTests
{
    private readonly StreamParser objParser = new();

    /// <summary>The fifth stream has a wire name, a kind, and a place in the TechieFlow report order.</summary>
    [Fact]
    public void MissesIsTheFifthTechieFlowStream()
    {
        StreamNames.TechieFlow.Should().Equal(
            StreamNames.Runs, StreamNames.Gates, StreamNames.Sessions, StreamNames.Commits, StreamNames.Misses);
        StreamNames.ToKind(StreamNames.Misses).Should().Be(StreamKind.Misses);
        StreamNames.ToName(StreamKind.Misses).Should().Be(StreamNames.Misses);
    }

    /// <summary>All four record kinds parse out of one file in a single pass (REQ-FN-072).</summary>
    [Fact]
    public void AllFourKindsParseFromOneFileInOnePass()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo);

        vResult.Misses.Should().HaveCount(3);
        vResult.MissFixes.Should().HaveCount(2);
        vResult.MissAmends.Should().HaveCount(2);
        vResult.MissReviews.Should().HaveCount(2);
        vResult.RecordCount.Should().Be(9);
    }

    /// <summary>An unrecognised <c>kind</c> is counted as an invalid line and skipped, never thrown.</summary>
    [Fact]
    public void UnknownKindIsCountedAndSkipped()
    {
        const string vText = """
            {"v":1,"ts":"2026-08-28T07:00:00Z","kind":"miss","app":"X","miss_id":"MISS-X-1"}
            {"v":1,"ts":"2026-08-28T07:00:01Z","kind":"miss-elsewhere","app":"X","miss_id":"MISS-X-1"}
            {"v":1,"ts":"2026-08-28T07:00:02Z","kind":"gate","app":"X"}
            """;

        var vResult = Parse(vText);

        vResult.Misses.Should().HaveCount(1);
        vResult.InvalidLines.Should().Be(2, "an unknown kind is the same class of event as a malformed line");
    }

    /// <summary>A malformed line inside the misses stream never fails the parse (REQ-FN-032).</summary>
    [Fact]
    public void MalformedLineIsCountedNotThrown()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo);

        vResult.InvalidLines.Should().Be(2, "the fixture carries one truncated line and one unknown kind");
    }

    /// <summary>
    /// <b>The acceptance line.</b> A miss stream parsed as <c>/repos</c> archived it dispatches every
    /// record on its <b>own</b> <c>kind</c>, and a record whose kind is none of the four is counted as an
    /// invalid line and skipped rather than thrown (REQ-FN-072, BRD-113).
    /// </summary>
    /// <remarks>
    /// The two halves are one clause: a dispatch that routed by file rather than by record would put a
    /// fix or a review among the misses, and a dispatch that threw on the unknown kind would fail the
    /// whole sync over one line a newer emitter wrote. Each list is checked against the field only its
    /// own kind carries, so a record landing in the wrong list cannot pass on count alone.
    /// </remarks>
    [Fact(DisplayName =
        "REQ-FN-072 — each record dispatches on its own kind and an unknown kind is counted and skipped, "
        + "never thrown")]
    public void EachRecordDispatchesOnItsOwnKindAndAnUnknownKindIsCountedNotThrown()
    {
        var vText = Fixtures.Read(Fixtures.TrSetupRepo, StreamKind.Misses)
            + "\n"
            + """{"v":1,"ts":"2026-09-08T19:00:00Z","kind":"miss-elsewhere","app":"TrSetup","miss_id":"MISS-TrSetup-UNKNOWNKIND"}""";

        var vParse = () => Parse(vText);
        vParse.Should().NotThrow("a kind TfLens does not know never fails a sync");

        var vResult = vParse();

        vResult.Misses.Select(aM => aM.MissId).Should().BeEquivalentTo(
        [
            "MISS-TrSetup-20260824-01", "MISS-TrSetup-20260820-01", "MISS-TrSetup-20260825-01"
        ]);
        vResult.MissFixes.Select(aF => aF.FixCmd).Should().BeEquivalentTo(
            ["fix-issues", "log-miss"], "only a miss-fix names the command that repaired it");
        vResult.MissAmends.Should().HaveCount(2)
            .And.OnlyContain(aA => aA.Field == "why_missed", "only a miss-amend names a field to complete");
        vResult.MissReviews.Select(aR => aR.ReviewPhase).Should().BeEquivalentTo(
            [MissReviewPhases.Day1, MissReviewPhases.Build], "a review is the fourth kind, not a miss");

        vResult.Misses.Should().NotContain(
            aM => aM.MissId == "MISS-TrSetup-UNKNOWNKIND", "an unknown kind is skipped, never coerced to a miss");
        vResult.InvalidLines.Should().Be(
            3, "the fixture's truncated line and its miss-blob line, plus the unknown kind added here");
        vResult.RecordCount.Should().Be(9, "an invalid line is counted as invalid and never as a record");
    }

    /// <summary>An absent optional stays null on every nullable of all three records (REQ-FN-036).</summary>
    [Fact]
    public void AbsentOptionalsStayNullAndAreNeverCoerced()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo);

        var vUnassessed = vResult.Misses.Single(aM => aM.MissId == "MISS-TrSetup-20260825-01");
        vUnassessed.WhyMissed.Should().BeNull("null means not assessed, never a bucket");
        vUnassessed.FoundPhase.Should().BeNull();
        vUnassessed.FoundGate.Should().BeNull();

        var vNoReq = vResult.Misses.Single(aM => aM.MissId == "MISS-TrSetup-20260820-01");
        vNoReq.ReqId.Should().BeNull("null is the finding: no REQ existed to miss");
        vNoReq.OriginModel.Should().BeNull("the emitter forces it null when the run lookup fails");

        var vUnattributable = vResult.MissFixes.Single(aF => aF.FixCmd == "log-miss");
        vUnattributable.FixRunId.Should().BeNull("log-miss --fixed omits it deliberately");
        vUnattributable.TokensOut.Should().BeNull("an unmeasured window is not zero tokens");
        vUnattributable.CostUsd.Should().BeNull();
        vUnattributable.CostAttribution.Should().Be("none");
    }

    /// <summary>A present zero survives as zero rather than reading as absent (REQ-FN-036).</summary>
    [Fact]
    public void PresentZeroSurvivesAsZero()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo);

        var vSole = vResult.MissFixes.Single(aF => aF.CostAttribution == "sole");
        vSole.TokensCacheWrite.Should().Be(0, "a measured zero is a measurement");
        vSole.Reopened.Should().BeFalse();
    }

    /// <summary>A property with no column reaches <c>Overflow</c> verbatim (REQ-FN-031).</summary>
    [Fact]
    public void UnknownPropertiesReachOverflow()
    {
        const string vText =
            """{"v":1,"ts":"2026-08-28T07:00:00Z","kind":"miss","app":"X","miss_id":"MISS-X-1","blast_radius":"wide"}""";

        var vResult = Parse(vText);

        vResult.Misses.Single().Overflow.Should().NotBeNull().And.Subject.ToString()!.Should().Contain("blast_radius");
        vResult.UnknownFields.Should().Contain("blast_radius");
    }

    /// <summary>
    /// <c>IsDocumented</c> answers over the union of the four vocabularies, so a fix-only field seen on
    /// a miss record is not reported as undocumented (REQ-FN-072).
    /// </summary>
    [Fact]
    public void IsDocumentedTakesTheUnionOfTheFourVocabularies()
    {
        StreamParser.IsDocumented(StreamKind.Misses, "sort").Should().BeTrue();
        StreamParser.IsDocumented(StreamKind.Misses, "what").Should().BeTrue();
        StreamParser.IsDocumented(StreamKind.Misses, "phase").Should().BeTrue();
        StreamParser.IsDocumented(StreamKind.Misses, "why_missed").Should().BeTrue();
        StreamParser.IsDocumented(StreamKind.Misses, "fix_run_id").Should().BeTrue();
        StreamParser.IsDocumented(StreamKind.Misses, "field").Should().BeTrue();
        StreamParser.IsDocumented(StreamKind.Misses, "value").Should().BeTrue();
        StreamParser.IsDocumented(StreamKind.Misses, "kind").Should().BeTrue();
        StreamParser.IsDocumented(StreamKind.Misses, "blast_radius").Should().BeFalse();
    }

    /// <summary>A fix-only field on a miss record is therefore never reported as an unknown field.</summary>
    [Fact]
    public void AFixOnlyFieldOnAMissRecordIsNotReportedUnknown()
    {
        const string vText =
            """{"v":1,"ts":"2026-08-28T07:00:00Z","kind":"miss","app":"X","miss_id":"MISS-X-1","fix_run_id":"2026-08-28T06:00:00Z"}""";

        var vResult = Parse(vText);

        vResult.UnknownFields.Should().BeEmpty();
        vResult.Misses.Single().Overflow.Should().NotBeNull(
            "the Miss table has no FixRunId column, so the value is still preserved verbatim");
    }

    /// <summary>Every wire field SCHEMA.md §5.5 documents lands in a column rather than in overflow.</summary>
    [Fact]
    public void EveryDocumentedMissFieldHasAColumn()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo);

        vResult.Misses.Should().OnlyContain(aM => aM.Overflow == null);
        vResult.MissFixes.Should().OnlyContain(aF => aF.Overflow == null);
        vResult.MissAmends.Should().OnlyContain(aA => aA.Overflow == null);
        vResult.MissReviews.Should().OnlyContain(aR => aR.Overflow == null);
    }

    /// <summary>A record from a newer schema is preserved whole rather than squeezed through the columns.</summary>
    [Fact]
    public void RecordsAboveSchemaV1ArePreservedWhole()
    {
        const string vText =
            """{"v":2,"ts":"2026-08-28T07:00:00Z","kind":"miss","app":"X","miss_id":"MISS-X-1","severity":"blocker"}""";

        var vResult = Parse(vText);

        vResult.RecordsAboveSchemaV1.Should().Be(1);
        var vMiss = vResult.Misses.Single();
        vMiss.Severity.Should().BeNull("a v>1 record keeps only its identity columns");
        vMiss.Overflow.Should().NotBeNull().And.Subject.ToString()!.Should().Contain("severity");
    }

    /// <summary>An empty stream file parses to nothing at all — the 404 case, which is never an error.</summary>
    [Fact]
    public void AnAbsentStreamParsesToZeroRecords()
    {
        var vResult = Parse(string.Empty);

        vResult.RecordCount.Should().Be(0);
        vResult.InvalidLines.Should().Be(0);
    }

    /// <summary>Parses the fixture repository's misses stream.</summary>
    /// <param name="aFixtureRepo">Which fixture directory supplies the text.</param>
    /// <returns>The parse result.</returns>
    private ParseResult ParseFixture(string aFixtureRepo) =>
        objParser.Parse(
            Fixtures.DemoUserId,
            aFixtureRepo,
            Fixtures.SourceSha,
            StreamKind.Misses,
            Fixtures.Read(aFixtureRepo, StreamKind.Misses));

    /// <summary>Parses inline JSONL text as the misses stream.</summary>
    /// <param name="aText">The lines to parse.</param>
    /// <returns>The parse result.</returns>
    private ParseResult Parse(string aText) =>
        objParser.Parse(Fixtures.DemoUserId, "owner/name", Fixtures.SourceSha, StreamKind.Misses, aText);
}
