using System.Text.Json;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Parsing;

/// <summary>Covers REQ-FN-030, REQ-FN-031, REQ-FN-032 and REQ-FN-036 against the JSONL fixtures.</summary>
public sealed class StreamParserTests
{
    private readonly StreamParser objParser = new();

    /// <summary>A line that is not valid JSON is counted in InvalidLines and skipped, never fatal (REQ-FN-032).</summary>
    [Fact]
    public void InvalidLinesAreCountedAndSkipped()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Runs);

        vResult.InvalidLines.Should().Be(1);
        vResult.Runs.Should().NotBeEmpty();
    }

    /// <summary>Every stream file in the fixture set carries a malformed line, and none of them abort the parse.</summary>
    [Fact]
    public void EveryFixtureStreamSurvivesItsMalformedLine()
    {
        foreach (var vStream in new[] { StreamKind.Runs, StreamKind.Gates, StreamKind.Sessions, StreamKind.Commits })
        {
            var vResult = ParseFixture(Fixtures.TrSetupRepo, vStream);
            vResult.InvalidLines.Should().Be(1, "fixture {0} carries exactly one malformed line", vStream);
            vResult.RecordCount.Should().BeGreaterThan(0);
        }
    }

    /// <summary>A property SCHEMA.md does not document is preserved in Overflow, not dropped (REQ-FN-031).</summary>
    [Fact]
    public void UnknownFieldIsPreservedInOverflow()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Runs);

        var vDrifted = vResult.Runs.Single(aR => aR.Routed == false);
        vDrifted.Overflow.Should().NotBeNull();
        using var vOverflow = JsonDocument.Parse(vDrifted.Overflow!);
        vOverflow.RootElement.GetProperty("routed_reason").GetString().Should().Be("tier-unavailable");
    }

    /// <summary>The distinct unknown field names are reported once for the Coverage report (REQ-FN-031).</summary>
    [Fact]
    public void UnknownFieldNamesAreReportedOnce()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Runs);

        vResult.UnknownFields.Should().Equal("routed_reason");
    }

    /// <summary>A record whose v is greater than 1 has its whole body preserved and is counted (REQ-FN-031).</summary>
    [Fact]
    public void RecordAboveSchemaV1KeepsWholeBodyInOverflow()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Runs);

        vResult.RecordsAboveSchemaV1.Should().Be(1);

        var vFuture = vResult.Runs.Single(aR => aR.V > 1);
        vFuture.Cmd.Should().Be("handoff-phase", "identity columns are still populated so the row can be stored");
        vFuture.DurationS.Should().BeNull("no typed column is filled from a schema TfLens has not read");
        vFuture.Overflow.Should().NotBeNull();

        using var vOverflow = JsonDocument.Parse(vFuture.Overflow!);
        vOverflow.RootElement.GetProperty("duration_s").GetInt32().Should().Be(1364);
        vOverflow.RootElement.GetProperty("build_result").GetString().Should().Be("pass");
    }

    /// <summary>An absent optional stays NULL while a present zero stays zero (SCHEMA.md 2.5, REQ-FN-036).</summary>
    [Fact]
    public void AbsentOptionalStaysNullWhilePresentZeroStaysZero()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Runs);

        var vTriage = vResult.Runs.Single(aR => aR.Cmd == "triage-issues");
        vTriage.FilesWritten.Should().Be(0, "the record carries files_written: 0");
        vTriage.TokensIn.Should().BeNull("tokens_in is absent — not captured is not zero");
        vTriage.TokensOut.Should().BeNull();
        vTriage.CostUsd.Should().BeNull();
        vTriage.Routed.Should().BeNull("an absent bool is null, never false");
        vTriage.Harness.Should().BeNull("harness: null means not detected");
        vTriage.TokensScope.Should().Be("none");
    }

    /// <summary>The same distinction holds on sessions, where zero tokens is a real measurement.</summary>
    [Fact]
    public void SessionZeroTokensAreNotConfusedWithAbsentOnes()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Sessions);

        var vEmpty = vResult.Sessions.Single(aS => aS.Harness == "codex");
        vEmpty.InputTokens.Should().Be(0);
        vEmpty.OutputTokens.Should().Be(0);
        vEmpty.CacheReadTokens.Should().Be(0);
        vEmpty.CacheCreationTokens.Should().BeNull("cache_creation_tokens is absent from that record");
        vEmpty.CostUsd.Should().BeNull();
        vEmpty.Model.Should().BeNull();
    }

    /// <summary>Every snake_case wire name lands on its PascalCase property (REQ-FN-030).</summary>
    [Fact]
    public void SnakeCaseFieldsMapToPascalCaseColumns()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Runs);

        var vBuild = vResult.Runs.First(aR => aR.Cmd == "build-phase" && aR.Mode == "build");
        vBuild.ProjectType.Should().Be("app");
        vBuild.DurationS.Should().Be(1891);
        vBuild.ReqsCount.Should().Be(2);
        vBuild.FilesWritten.Should().Be(14);
        vBuild.BuildResult.Should().Be("pass");
        vBuild.TierModel.Should().Be("claude-opus-5");
        vBuild.TokensCacheRead.Should().Be(5145405);
        vBuild.TokensCacheWrite.Should().Be(330212);
        vBuild.TokensScope.Should().Be("tree");
        vBuild.Attempt.Should().Be(1);
        vBuild.ReqsTouched.Should().Be("""["REQ-UI-004","REQ-FN-011"]""");
        vBuild.Subagents.Should().Be("""["trblazeui"]""");
        vBuild.Overflow.Should().BeNull("every property of that record has a column");
    }

    /// <summary>Gate wire names map across, including the arrays SCHEMA.md types as string[].</summary>
    [Fact]
    public void GateFieldsMapAcrossIncludingArrays()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Gates);

        var vVisual = vResult.Gates.Single(aG => aG.ReqId == "REQ-UI-009" && aG.Backfilled is null);
        vVisual.ReqClass.Should().Be("UI");
        vVisual.RunId.Should().Be("2026-08-24T13:31:19Z");
        vVisual.FailureClass.Should().Be("overlap");
        vVisual.PriorVerdict.Should().Be("Implemented");
        vVisual.GatesRun.Should().Be("""["build","acceptance","render","visual"]""");
        vVisual.Gate.Should().Be("visual");
    }

    /// <summary>Backfilled records keep their provenance flags and the inferred field list (REQ-FN-036).</summary>
    [Fact]
    public void BackfilledProvenanceIsPreserved()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Gates);

        var vTainted = vResult.Gates.Where(aG => aG.Backfilled == true).Select(aG => aG.ReqId).ToList();
        vTainted.Should().BeEquivalentTo(["REQ-UI-004", "REQ-FN-011", "REQ-UI-009"]);

        var vFirst = vResult.Gates.First(aG => aG.Backfilled == true);
        vFirst.Inferred.Should().Be("""["attempt","failure_class"]""");
    }

    /// <summary>project_type_inferred survives so the report can label those records unclassified.</summary>
    [Fact]
    public void ProjectTypeInferredIsPreserved()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Runs);

        vResult.Runs.Count(aR => aR.ProjectTypeInferred == true).Should().Be(1);
        vResult.Runs.Count(aR => aR.ProjectTypeInferred is null).Should().BeGreaterThan(0);
    }

    /// <summary>Commit fields map across and a null subject_prefix stays null rather than becoming empty text.</summary>
    [Fact]
    public void CommitFieldsMapAcross()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Commits);

        var vCommit = vResult.Commits.Single(aC => aC.Sha == "c3d4e5f");
        vCommit.Files.Should().Be(28);
        vCommit.Insertions.Should().Be(6583);
        vCommit.Deletions.Should().Be(0);
        vCommit.SubjectPrefix.Should().BeNull("subject_prefix is explicitly null on that record");
        vCommit.Branch.Should().Be("main");
    }

    /// <summary>Empty text is a legitimate absent stream and yields no records and no error.</summary>
    [Fact]
    public void EmptyTextYieldsNoRecords()
    {
        var vResult = objParser.Parse(
            Fixtures.DemoUserId, Fixtures.TrSetupRepo, Fixtures.SourceSha, StreamKind.Gates, string.Empty);

        vResult.RecordCount.Should().Be(0);
        vResult.InvalidLines.Should().Be(0);
    }

    /// <summary>
    /// Parses one fixture stream for the demo user.
    /// </summary>
    /// <param name="aRepo">The fixture repository.</param>
    /// <param name="aStream">The stream to parse.</param>
    /// <returns>The parse result.</returns>
    private ParseResult ParseFixture(string aRepo, StreamKind aStream) =>
        objParser.Parse(
            Fixtures.DemoUserId, aRepo, Fixtures.SourceSha, aStream, Fixtures.Read(aRepo, aStream));
}
