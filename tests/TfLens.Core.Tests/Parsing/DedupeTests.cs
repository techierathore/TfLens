using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Parsing;

/// <summary>Covers the four natural-key dedupe rules — REQ-FN-033, REQ-FN-034 and REQ-FN-035.</summary>
public sealed class DedupeTests
{
    private readonly StreamParser objParser = new();

    /// <summary>Commit records sharing a sha collapse to the first one, and the collapse is counted (REQ-FN-033).</summary>
    [Fact]
    public void CommitsCollapseOnShaKeepingFirst()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Commits);

        vResult.DuplicatesCollapsed.Should().Be(2);
        vResult.Commits.Select(aC => aC.Sha).Should()
            .Equal("a1b2c3d", "b2c3d4e", "c3d4e5f", "d4e5f6a", "e5f6a7b");
        vResult.Commits.Single(aC => aC.Sha == "a1b2c3d").Branch.Should()
            .Be("main", "the first record wins, not the later one from another branch");
    }

    /// <summary>Two repositories may legitimately share a short sha, so both records survive (REQ-FN-033).</summary>
    [Fact]
    public void CommitsWithTheSameShaInTwoReposBothSurvive()
    {
        var vSetup = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Commits);
        var vBlazeUi = ParseFixture(Fixtures.TrBlazeUiRepo, StreamKind.Commits);

        var vShared = Dedupe.Commits([.. vSetup.Commits, .. vBlazeUi.Commits]);

        vShared.Collapsed.Should().Be(0, "the dedupe key is per repository");
        vShared.Records.Count(aC => aC.Sha == "a1b2c3d").Should().Be(2);
    }

    /// <summary>A commit record with no sha has no natural key, so it is kept rather than collapsed.</summary>
    [Fact]
    public void CommitWithoutShaIsKept()
    {
        var vText = string.Join('\n',
            """{"v":1,"ts":"2026-08-20T00:00:00Z","kind":"commit","app":"TrSetup","files":1}""",
            """{"v":1,"ts":"2026-08-20T00:00:01Z","kind":"commit","app":"TrSetup","files":2}""");

        var vResult = Parse(StreamKind.Commits, vText);

        vResult.Commits.Should().HaveCount(2);
        vResult.DuplicatesCollapsed.Should().Be(0);
    }

    /// <summary>Cumulative OpenCode snapshots collapse to the one with the highest output_tokens (REQ-FN-034).</summary>
    [Fact]
    public void SessionsKeepHighestOutputTokens()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Sessions);

        var vSnapshot = vResult.Sessions.Single(aS => aS.SessionId.StartsWith("7b31", StringComparison.Ordinal));
        vSnapshot.OutputTokens.Should().Be(41880, "replaying an earlier snapshot never lowers the figure");
        vSnapshot.CostUsd.Should().Be(0.4213m);
    }

    /// <summary>Equal output_tokens are broken by the latest ts, deterministically (REQ-FN-034).</summary>
    [Fact]
    public void SessionsTieBreakOnLatestTs()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Sessions);

        var vTied = vResult.Sessions.Single(aS => aS.SessionId.StartsWith("c2d9", StringComparison.Ordinal));
        vTied.Ts.Should().Be("2026-08-25T09:25:41Z");
        vTied.DurationS.Should().Be(905);
    }

    /// <summary>The session rule is order-independent: reversing the file changes nothing.</summary>
    [Fact]
    public void SessionDedupeIsOrderIndependent()
    {
        var vForward = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Sessions);
        var vReversed = Dedupe.Sessions(vForward.Sessions.Reverse().ToList());

        vReversed.Records.Should().BeEquivalentTo(vForward.Sessions);
    }

    /// <summary>Run records sharing ts + app + cmd collapse to the first (REQ-FN-035).</summary>
    [Fact]
    public void RunsCollapseOnTsAppCmd()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Runs);

        vResult.DuplicatesCollapsed.Should().Be(1);
        vResult.Runs.Should().HaveCount(6);
    }

    /// <summary>Gate records sharing ts + app + req_id + run_id collapse to the first (REQ-FN-035).</summary>
    [Fact]
    public void GatesCollapseOnTsAppReqIdRunId()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Gates);

        vResult.DuplicatesCollapsed.Should().Be(1);
        vResult.Gates.Should().HaveCount(14);
    }

    /// <summary>The same gate id at a different timestamp is a different verdict and must not collapse.</summary>
    [Fact]
    public void GatesAtDifferentTimestampsAreDistinct()
    {
        var vResult = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Gates);

        vResult.Gates.Count(aG => aG.ReqId == "REQ-UI-004").Should().Be(2, "one live verdict and one backfilled");
    }

    /// <summary>Parsing the same text twice yields identical record counts — the parser is a pure function.</summary>
    [Fact]
    public void ParsingTheSameTextTwiceYieldsTheSameCounts()
    {
        var vFirst = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Gates);
        var vSecond = ParseFixture(Fixtures.TrSetupRepo, StreamKind.Gates);

        vSecond.RecordCount.Should().Be(vFirst.RecordCount);
        vSecond.DuplicatesCollapsed.Should().Be(vFirst.DuplicatesCollapsed);
        vSecond.InvalidLines.Should().Be(vFirst.InvalidLines);
    }

    /// <summary>
    /// Parses one fixture stream for the demo user.
    /// </summary>
    /// <param name="aRepo">The fixture repository.</param>
    /// <param name="aStream">The stream to parse.</param>
    /// <returns>The parse result.</returns>
    private ParseResult ParseFixture(string aRepo, StreamKind aStream) =>
        objParser.Parse(Fixtures.DemoUserId, aRepo, Fixtures.SourceSha, aStream, Fixtures.Read(aRepo, aStream));

    /// <summary>
    /// Parses inline JSONL for the demo user and the busy fixture repository.
    /// </summary>
    /// <param name="aStream">The stream the text is.</param>
    /// <param name="aText">The raw JSONL.</param>
    /// <returns>The parse result.</returns>
    private ParseResult Parse(StreamKind aStream, string aText) =>
        objParser.Parse(Fixtures.DemoUserId, Fixtures.TrSetupRepo, Fixtures.SourceSha, aStream, aText);
}
