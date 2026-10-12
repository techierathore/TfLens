using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Parsing;

/// <summary>
/// A second fix attempt and a second review correction are separate records (REQ-FN-144).
/// </summary>
/// <remarks>
/// SCHEMA.md §5.5.3 defines <c>fix_attempt</c> as <c>1 +</c> the prior miss-fix records for the miss,
/// and the reference (<c>tf-metrics.sh</c>) counts every non-backfilled miss-fix and every review with no
/// dedupe. The shapes below are the real estate pairs: TechieRag's two fix attempts inside one fix run
/// and Chatur's day1-review pair differing only in <c>correction_run_id</c>.
/// </remarks>
public sealed class MissAttemptKeyTests
{
    private const string FirstAttempt =
        """{"v":1,"ts":"2026-10-06T10:00:00Z","kind":"miss-fix","app":"TechieRag","miss_id":"MISS-TechieRag-20261006-02","fix_run_id":"2026-10-06T09:00:00Z","fix_attempt":1,"verdict_after":"FAIL"}""";

    private const string SecondAttempt =
        """{"v":1,"ts":"2026-10-06T11:00:00Z","kind":"miss-fix","app":"TechieRag","miss_id":"MISS-TechieRag-20261006-02","fix_run_id":"2026-10-06T09:00:00Z","fix_attempt":2,"verdict_after":"PASS"}""";

    private const string FirstCorrection =
        """{"v":1,"ts":"2026-10-01T10:00:00Z","kind":"review","app":"Chatur","phase":"day1-review","reviewed_run_id":"2026-10-01T08:00:00Z","correction_run_id":"2026-10-01T09:00:00Z","corrections":3,"tokens_correct":1000}""";

    private const string SecondCorrection =
        """{"v":1,"ts":"2026-10-01T12:00:00Z","kind":"review","app":"Chatur","phase":"day1-review","reviewed_run_id":"2026-10-01T08:00:00Z","correction_run_id":"2026-10-01T11:00:00Z","corrections":3,"tokens_correct":2000}""";

    private readonly StreamParser objParser = new();

    /// <summary>
    /// Two miss-fix records for one miss in one fix run, with different <c>fix_attempt</c>, both survive
    /// the parse.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-144 — two fix attempts in one run are two records")]
    public void TwoFixAttemptsAreTwoRecords()
    {
        var vResult = Parse(FirstAttempt, SecondAttempt);

        vResult.MissFixes.Select(aF => aF.FixAttempt).Should().BeEquivalentTo(new int?[] { 1, 2 });
        vResult.DuplicatesCollapsed.Should().Be(0);
    }

    /// <summary>
    /// Two review records for one reviewed run, with different <c>correction_run_id</c>, both survive the
    /// parse.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-144 — two review corrections of one run are two records")]
    public void TwoCorrectionsAreTwoRecords()
    {
        var vResult = Parse(FirstCorrection, SecondCorrection);

        vResult.MissReviews.Should().HaveCount(2);
        vResult.MissReviews.Select(aR => aR.TokensCorrect).Should().BeEquivalentTo(new int?[] { 1000, 2000 });
    }

    /// <summary>
    /// A re-parse of the same attempt and the same correction still collapses — the widened key keeps the
    /// stream idempotent.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-144 — the same attempt and correction still collapse")]
    public void SameAttemptStillCollapses()
    {
        var vResult = Parse(FirstAttempt, SecondAttempt, FirstAttempt, FirstCorrection, SecondCorrection, SecondCorrection);

        vResult.MissFixes.Should().HaveCount(2);
        vResult.MissReviews.Should().HaveCount(2);
    }

    /// <summary>
    /// A fix with no <c>fix_attempt</c> keys on <c>-1</c>, so two copies of it still collapse rather than
    /// both surviving.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-144 — a fix with no attempt still collapses with itself")]
    public void NoAttemptStillCollapses()
    {
        var vResult = Dedupe.MissFixes([Fix(null, "2026-10-06T10:00:00Z"), Fix(null, "2026-10-06T11:00:00Z")]);

        vResult.Records.Should().ContainSingle().Which.Ts.Should().Be("2026-10-06T11:00:00Z");
        vResult.Collapsed.Should().Be(1);
    }

    /// <summary>
    /// A review with no <c>correction_run_id</c> (applied by hand) keys on the empty string and still
    /// collapses with itself.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-144 — a review with no correction run still collapses with itself")]
    public void NoCorrectionRunStillCollapses()
    {
        var vResult = Dedupe.MissReviews([Review(null, "2026-10-01T12:00:00Z"), Review(null, "2026-10-01T10:00:00Z")]);

        vResult.Records.Should().ContainSingle().Which.Ts.Should().Be("2026-10-01T10:00:00Z");
        vResult.Collapsed.Should().Be(1);
    }

    /// <summary>The DDL carries the widened keys and drops the narrow ones by name.</summary>
    [Fact(DisplayName = "REQ-FN-144 — the schema widens both keys and drops the old ones")]
    public void SchemaWidensBothKeys()
    {
        var vSchema = File.ReadAllText(Path.Combine(RepoRoot(), "database", "001-schema.sql"));

        vSchema.Should().Contain("""DROP INDEX IF EXISTS "UcMissFixUserRepoMissIdFixRunId";""");
        vSchema.Should().Contain("""DROP INDEX IF EXISTS "UcMissReviewUserRepoPhaseRunId";""");
        vSchema.Should().Contain("""COALESCE("FixRunId", ''), COALESCE("FixAttempt", -1))""");
        vSchema.Should().Contain("""COALESCE("ProducedRunId", ''), COALESCE("CorrectionRunId", ''))""");
    }

    /// <summary>Parses the given lines as one misses stream.</summary>
    /// <param name="aLines">The JSONL lines.</param>
    /// <returns>The parse result.</returns>
    private ParseResult Parse(params string[] aLines) =>
        objParser.Parse(Fixtures.DemoUserId, "owner/name", Fixtures.SourceSha, StreamKind.Misses, string.Join('\n', aLines));

    /// <summary>Builds a fix record in one run with the given attempt.</summary>
    /// <param name="aAttempt">The fix attempt, or <c>null</c>.</param>
    /// <param name="aTs">The record's own timestamp.</param>
    /// <returns>The record.</returns>
    private static MissFixRecord Fix(int? aAttempt, string aTs) => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = "owner/name",
        SourceSha = Fixtures.SourceSha,
        Ts = aTs,
        MissId = "MISS-A-1",
        FixRunId = "2026-10-06T09:00:00Z",
        FixAttempt = aAttempt
    };

    /// <summary>Builds a review of one run with the given correction run.</summary>
    /// <param name="aCorrectionRunId">The correction run, or <c>null</c>.</param>
    /// <param name="aTs">The record's own timestamp.</param>
    /// <returns>The record.</returns>
    private static MissReviewRecord Review(string? aCorrectionRunId, string aTs) => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = "owner/name",
        SourceSha = Fixtures.SourceSha,
        Ts = aTs,
        ReviewPhase = "day1-review",
        ProducedRunId = "2026-10-01T08:00:00Z",
        CorrectionRunId = aCorrectionRunId
    };

    /// <summary>Walks up from the test binary to the directory holding <c>TfLens.slnx</c>.</summary>
    /// <returns>The repository root.</returns>
    private static string RepoRoot()
    {
        var vDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (vDir is not null && !File.Exists(Path.Combine(vDir.FullName, "TfLens.slnx")))
        {
            vDir = vDir.Parent;
        }

        return vDir?.FullName ?? throw new InvalidOperationException("TfLens.slnx not found above the test binary.");
    }
}
