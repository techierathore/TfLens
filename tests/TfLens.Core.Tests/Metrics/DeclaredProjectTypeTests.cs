using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-058 — <c>per_repo[].project_type</c> is the repository's <em>current</em> declaration.
/// </summary>
/// <remarks>
/// The reference reads one value out of <c>core-config.yaml</c>: what the project declares itself to be
/// today. TfLens has only the streams, and every record froze the declaration in force when it was
/// written, so the record that answers the reference's question is the newest one carrying a
/// declaration — never the first the store happens to return, and never the most numerous.
/// </remarks>
public sealed class DeclaredProjectTypeTests
{
    private const int UserId = 7;
    private const string Framework = "techieflow";
    private const string Repo = "acme/alpha";

    /// <summary>
    /// The reclassified-repository regression: TfLens moved from <c>docs</c> to <c>app</c>, so <c>docs</c>
    /// is still the more numerous value in the gate stream while every stream's newest record reads
    /// <c>app</c>. The current declaration is <c>app</c>, and the older, larger <c>docs</c> population
    /// must not win — the defect that demoted this REQ printed <c>docs</c> here.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ReclassifiedRepoReportsTheNewestDeclaration()
    {
        var vGates = new List<GateRecord>();
        for (var vIndex = 0; vIndex < 225; vIndex++)
        {
            vGates.Add(Gate("docs", "2026-08-27T13:28:32Z"));
        }

        for (var vIndex = 0; vIndex < 180; vIndex++)
        {
            vGates.Add(Gate("app", "2026-08-29T07:59:15Z"));
        }

        var vFacts = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            vGates,
            [Run("docs", "2026-08-28T05:12:35Z"), Run("app", "2026-08-29T09:01:41Z")],
            [Session("s1", "docs", "2026-08-28T05:13:14Z"), Session("s2", "app", "2026-08-29T07:35:42Z")],
            [Commit("c1", "docs", "2026-08-27T18:37:00Z"), Commit("c2", "app", "2026-08-29T07:34:42Z")]));

        Assert.Equal("app", vFacts.ProjectType);
    }

    /// <summary>
    /// The answer does not depend on the order the store returns rows in: seeding the same reclassified
    /// repository with the older records last gives the same current declaration. The original defect was
    /// precisely a read whose answer changed with row order.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ReclassifiedRepoIsUnaffectedByRowOrder()
    {
        var vNewestFirst = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("app", "2026-08-29T07:59:15Z"), Gate("docs", "2026-08-27T13:28:32Z"), Gate("docs", "2026-08-26T13:28:32Z")]));

        var vOldestFirst = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("docs", "2026-08-26T13:28:32Z"), Gate("docs", "2026-08-27T13:28:32Z"), Gate("app", "2026-08-29T07:59:15Z")]));

        Assert.Equal("app", vNewestFirst.ProjectType);
        Assert.Equal("app", vOldestFirst.ProjectType);
    }

    /// <summary>A repository that has only ever declared one type reports that type, whatever the dates.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task SingleDeclaredTypeIsReported()
    {
        var vFacts = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("library", "2026-08-01T00:00:00Z"), Gate("library", "2026-08-09T00:00:00Z")],
            [Run("library", "2026-08-05T00:00:00Z")]));

        Assert.Equal("library", vFacts.ProjectType);
    }

    /// <summary>
    /// The newest record wins across streams, not within one: the gate stream is wholly <c>docs</c> and
    /// outnumbers everything else, but the single newest record in the repository is a commit declaring
    /// <c>app</c>, so <c>app</c> is the current declaration.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NewestRecordWinsAcrossStreams()
    {
        var vFacts = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("docs", "2026-08-10T00:00:00Z"), Gate("docs", "2026-08-11T00:00:00Z"), Gate("docs", "2026-08-12T00:00:00Z")],
            aCommits: [Commit("c1", "app", "2026-08-13T00:00:00Z")]));

        Assert.Equal("app", vFacts.ProjectType);
    }

    /// <summary>A repository whose records declare no type at all falls back to <c>app</c>, as the reference does.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NoDeclarationFallsBackToApp()
    {
        var vFacts = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate(null, "2026-08-10T00:00:00Z"), Gate(string.Empty, "2026-08-12T00:00:00Z")],
            [Run(null, "2026-08-11T00:00:00Z")]));

        Assert.Equal("app", vFacts.ProjectType);
    }

    /// <summary>
    /// Two declarations at the same instant are broken by ordinal comparison of the declared value,
    /// lowest first — a rule that reads nothing but the values in hand, so it answers the same way on
    /// every run whichever order the store returns the rows in.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task SameInstantIsBrokenOrdinally()
    {
        var vOneWay = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("docs", "2026-08-12T00:00:00Z"), Gate("app", "2026-08-12T00:00:00Z")]));

        var vOtherWay = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("app", "2026-08-12T00:00:00Z"), Gate("docs", "2026-08-12T00:00:00Z")]));

        Assert.Equal("app", vOneWay.ProjectType);
        Assert.Equal("app", vOtherWay.ProjectType);
    }

    /// <summary>
    /// Offsets are compared as instants, not as text: the same moment written in two zones is a tie and
    /// resolves ordinally rather than by which string sorts higher.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task OffsetsAreComparedAsInstants()
    {
        var vFacts = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("docs", "2026-08-12T05:30:00+05:30"), Gate("app", "2026-08-12T00:00:00Z")]));

        Assert.Equal("app", vFacts.ProjectType);
    }

    /// <summary>
    /// A record whose timestamp cannot be read never outranks one that can: the dated <c>app</c> record
    /// answers, even though the undated <c>docs</c> record is more numerous and seeded first.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task UndatedRecordNeverOutranksADatedOne()
    {
        var vFacts = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("docs", "not-a-timestamp"), Gate("docs", string.Empty), Gate("app", "2026-08-01T00:00:00Z")]));

        Assert.Equal("app", vFacts.ProjectType);
    }

    /// <summary>
    /// When nothing carries a usable timestamp the same ordinal tie-break settles it, so an all-undated
    /// repository still answers the same way on every run.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AllUndatedRecordsStillResolveDeterministically()
    {
        var vOneWay = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("docs", "not-a-timestamp"), Gate("app", "also-not-a-timestamp")]));

        var vOtherWay = await AnalyseAsync(new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("app", "also-not-a-timestamp"), Gate("docs", "not-a-timestamp")]));

        Assert.Equal("app", vOneWay.ProjectType);
        Assert.Equal("app", vOtherWay.ProjectType);
    }

    /// <summary>
    /// The declared type is a coverage fact and never a segment key: an inferred record still segments as
    /// <c>unclassified</c> (REQ-FN-048) while contributing its declaration to <c>project_type</c>.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task DeclaredTypeIsNotASegmentKey()
    {
        var vStore = new FixtureTelemetryStore().Seed(
            UserId,
            Repo,
            Framework,
            [Gate("app", "2026-08-12T00:00:00Z", aInferred: true)]);

        var vAnalysis = await new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(UserId, Framework);

        Assert.Equal("app", vAnalysis.PerRepo.Single().ProjectType);
        Assert.Equal([MetricsConstants.Unclassified], vAnalysis.Live.Keys);
    }

    /// <summary>
    /// Runs the engine over a seeded store and returns the repository's per-repo facts.
    /// </summary>
    /// <param name="aStore">The seeded store to analyse.</param>
    /// <returns>The single repository's facts.</returns>
    private static async Task<PerRepoFacts> AnalyseAsync(FixtureTelemetryStore aStore)
    {
        var vAnalysis = await new MetricsEngine(aStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(UserId, Framework);

        return vAnalysis.PerRepo.Single();
    }

    /// <summary>
    /// Builds a gate record carrying only the fields these tests are about.
    /// </summary>
    /// <param name="aProjectType">The declared project type, or <c>null</c> for a record declaring none.</param>
    /// <param name="aTs">The record timestamp, which may deliberately be unparseable.</param>
    /// <param name="aInferred">Whether the project type was inferred rather than declared.</param>
    /// <returns>The record.</returns>
    private static GateRecord Gate(string? aProjectType, string aTs, bool? aInferred = null) => new()
    {
        UserId = UserId,
        Repo = Repo,
        SourceSha = "fixture",
        Ts = aTs,
        App = "AlphaApp",
        ProjectType = aProjectType,
        ProjectTypeInferred = aInferred,
        ReqId = "REQ-FN-001",
        ReqClass = "FN",
        Attempt = 1,
        Verdict = "Verified"
    };

    /// <summary>
    /// Builds a run record carrying only the fields these tests are about.
    /// </summary>
    /// <param name="aProjectType">The declared project type, or <c>null</c> for a record declaring none.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(string? aProjectType, string aTs) => new()
    {
        UserId = UserId,
        Repo = Repo,
        SourceSha = "fixture",
        Ts = aTs,
        App = "AlphaApp",
        ProjectType = aProjectType,
        Cmd = "build-phase",
        Mode = "build"
    };

    /// <summary>
    /// Builds a session record carrying only the fields these tests are about.
    /// </summary>
    /// <param name="aSessionId">The harness session identifier — the dedupe key.</param>
    /// <param name="aProjectType">The declared project type.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <returns>The record.</returns>
    private static SessionRecord Session(string aSessionId, string? aProjectType, string aTs) => new()
    {
        UserId = UserId,
        Repo = Repo,
        SourceSha = "fixture",
        Ts = aTs,
        App = "AlphaApp",
        ProjectType = aProjectType,
        SessionId = aSessionId
    };

    /// <summary>
    /// Builds a commit record carrying only the fields these tests are about.
    /// </summary>
    /// <param name="aSha">The commit SHA — the dedupe key within a repository.</param>
    /// <param name="aProjectType">The declared project type.</param>
    /// <param name="aTs">The record timestamp.</param>
    /// <returns>The record.</returns>
    private static CommitRecord Commit(string aSha, string? aProjectType, string aTs) => new()
    {
        UserId = UserId,
        Repo = Repo,
        SourceSha = "fixture",
        Ts = aTs,
        App = "AlphaApp",
        ProjectType = aProjectType,
        Sha = aSha
    };
}
