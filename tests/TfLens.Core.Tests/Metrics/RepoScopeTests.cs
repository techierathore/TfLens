using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.Fakes;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-UI-077 — the report pages' repo picker: an engine call given one repository computes every figure
/// from that repository's records only, and a call given none computes exactly what it computed before.
/// </summary>
/// <remarks>
/// Two repositories on the same framework and user, with different record counts, so a figure that leaked
/// the other repository's rows would show up as a wrong count rather than pass by coincidence.
/// </remarks>
public sealed class RepoScopeTests : IDisposable
{
    private const int UserId = 7;
    private const string Alpha = "acme/alpha";
    private const string Beta = "acme/beta";

    private readonly string objDataRoot = ExtrasFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>
    /// Analysing one repository reads only that repository: its per-repo line is the only one, and the live
    /// segment counts its two gate records, not the five across both repositories.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnalyseOneRepoReadsOnlyThatRepo()
    {
        var vAnalysis = await Engine(SeededStore()).AnalyseAsync(UserId, FrameworkNames.TechieFlow, Beta);

        vAnalysis.PerRepo.Select(aFacts => aFacts.Repo).Should().Equal(Beta);
        vAnalysis.Live["app"].Records.Should().Be(2);
    }

    /// <summary>
    /// A <c>null</c> repository is the all-repos analysis, identical field for field to a call that names no
    /// repository at all — the view <c>tf-metrics.sh --rollup</c> is the oracle for.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnalyseNullRepoIsUnchanged()
    {
        var vStore = SeededStore();

        var vDefault = await Engine(vStore).AnalyseAsync(UserId, FrameworkNames.TechieFlow);
        var vNull = await Engine(vStore).AnalyseAsync(UserId, FrameworkNames.TechieFlow, null);

        JsonSerializer.Serialize(vNull).Should().Be(JsonSerializer.Serialize(vDefault));
        vDefault.Live["app"].Records.Should().Be(5);
    }

    /// <summary>
    /// The cache keys on the repository: a one-repo analysis is never served for the all-repos one, and a
    /// repeated one-repo request is served from memory.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task CacheKeysOnRepo()
    {
        var vStore = SeededStore();
        var vEngine = new CachingMetricsEngine(
            Engine(vStore), new MemoryAnalysisCache(new MemoryCache(new MemoryCacheOptions())), vStore);

        var vAll = await vEngine.AnalyseAsync(UserId, FrameworkNames.TechieFlow);
        var vBeta = await vEngine.AnalyseAsync(UserId, FrameworkNames.TechieFlow, Beta);
        var vBetaAgain = await vEngine.AnalyseAsync(UserId, FrameworkNames.TechieFlow, Beta);

        vBeta.Should().NotBeSameAs(vAll);
        vBetaAgain.Should().BeSameAs(vBeta);
        vBeta.Live["app"].Records.Should().Be(2);
    }

    /// <summary>
    /// Routing narrowed to one repository counts only that repository's runs that carry routing fields.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task RoutingOneRepoReadsOnlyThatRepo()
    {
        var vExtras = new ExtraMetrics(SeededStore(), ExtrasFixture.Options(objDataRoot));

        var vAll = await vExtras.AnalyseRoutingAsync(UserId, FrameworkNames.TechieFlow);
        var vAlpha = await vExtras.AnalyseRoutingAsync(UserId, FrameworkNames.TechieFlow, Alpha);

        vAll.RunsWithRoutingFields.Should().Be(4);
        vAlpha.RunsWithRoutingFields.Should().Be(3);
    }

    /// <summary>
    /// The harness comparison narrowed to one repository counts only that repository's runs and gates.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task HarnessOneRepoCountsOnlyThatRepo()
    {
        var vExtras = new ExtraMetrics(SeededStore(), ExtrasFixture.Options(objDataRoot));

        var vColumn = (await vExtras.CompareHarnessesAsync(UserId, FrameworkNames.TechieFlow, Beta))
            .Columns.Single(aColumn => aColumn.Harness == "claude-code");

        vColumn.Runs.Should().Be(1);
    }

    /// <summary>Builds the uncached engine over a store.</summary>
    /// <param name="aStore">The seeded store.</param>
    /// <returns>The engine under test.</returns>
    private static MetricsEngine Engine(FixtureTelemetryStore aStore) =>
        new(aStore, NullLogger<MetricsEngine>.Instance);

    /// <summary>
    /// Alpha: three gates and three routed runs. Beta: two gates and one routed run. Same user, same
    /// framework, same project type.
    /// </summary>
    /// <returns>The store.</returns>
    private static FixtureTelemetryStore SeededStore() =>
        new FixtureTelemetryStore()
            .Seed(
                UserId,
                Alpha,
                FrameworkNames.TechieFlow,
                aGates:
                [
                    GateFixtures.Gate(aReqId: "REQ-FN-001", aRepo: Alpha),
                    GateFixtures.Gate(aReqId: "REQ-FN-002", aRepo: Alpha),
                    GateFixtures.Gate(aReqId: "REQ-FN-003", aRepo: Alpha)
                ],
                aRuns: [Run(Alpha, "10:00:00"), Run(Alpha, "11:00:00"), Run(Alpha, "12:00:00")])
            .Seed(
                UserId,
                Beta,
                FrameworkNames.TechieFlow,
                aGates:
                [
                    GateFixtures.Gate(aReqId: "REQ-FN-101", aRepo: Beta, aApp: "BetaApp"),
                    GateFixtures.Gate(aReqId: "REQ-FN-102", aRepo: Beta, aApp: "BetaApp")
                ],
                aRuns: [Run(Beta, "13:00:00")]);

    /// <summary>One claude-code run on one model, which therefore carries routing fields.</summary>
    /// <param name="aRepo">The repository it was read from.</param>
    /// <param name="aStarted">Its start, as a clock time on the fixture's day.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(string aRepo, string aStarted) => new()
    {
        UserId = UserId,
        Repo = aRepo,
        SourceSha = "fixture",
        Ts = $"2026-09-01T{aStarted}Z",
        App = aRepo,
        Cmd = "build-phase",
        Started = $"2026-09-01T{aStarted}Z",
        Ended = $"2026-09-01T{aStarted}Z",
        Harness = "claude-code",
        Model = "claude-sonnet-4-5",
        TokensIn = 100
    };
}
