using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-046 — the analysis is memoised in process memory, never written back, and dropped on the
/// invalidation hook a completed sync or rebuild calls.
/// </summary>
public sealed class AnalysisCacheTests
{
    private const int UserId = 7;
    private const string Framework = "techieflow";

    /// <summary>A second request for the same user, framework and sync version reuses the memoised analysis.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task SecondRequestIsServedFromMemory()
    {
        var (vStore, vEngine, _) = Build();

        var vFirst = await vEngine.AnalyseAsync(UserId, Framework);
        var vSecond = await vEngine.AnalyseAsync(UserId, Framework);

        Assert.Same(vFirst, vSecond);
        Assert.Equal(1, vStore.GateReads);
    }

    /// <summary>Invalidating a user throws their memoised analysis away and the next request recomputes it.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task InvalidateForcesARecompute()
    {
        var (vStore, vEngine, vCache) = Build();

        await vEngine.AnalyseAsync(UserId, Framework);
        vCache.Invalidate(UserId);
        await vEngine.AnalyseAsync(UserId, Framework);

        Assert.Equal(2, vStore.GateReads);
    }

    /// <summary>Invalidating one user leaves another user's memoised analysis alone.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task InvalidateTouchesOnlyTheNamedUser()
    {
        var vStore = new FixtureTelemetryStore()
            .Seed(UserId, "acme/alpha", Framework, [GateFixtures.Gate()])
            .Seed(8, "acme/other", Framework, [GateFixtures.Gate(aRepo: "acme/other")]);
        var vCache = new MemoryAnalysisCache(new MemoryCache(new MemoryCacheOptions()));
        var vEngine = new CachingMetricsEngine(
            new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance), vCache, vStore);

        await vEngine.AnalyseAsync(UserId, Framework);
        var vOtherFirst = await vEngine.AnalyseAsync(8, Framework);
        vCache.Invalidate(UserId);
        var vOtherSecond = await vEngine.AnalyseAsync(8, Framework);

        Assert.Same(vOtherFirst, vOtherSecond);
    }

    /// <summary>Two frameworks never share a cache entry, so a framework switch cannot serve the other's figures.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task FrameworksGetSeparateEntries()
    {
        var vStore = new FixtureTelemetryStore()
            .Seed(UserId, "acme/alpha", "techieflow", [GateFixtures.Gate()])
            .Seed(UserId, "acme/play", "playbook", [GateFixtures.Gate(aRepo: "acme/play"), GateFixtures.Gate(aReqId: "REQ-FN-002", aRepo: "acme/play")]);
        var vCache = new MemoryAnalysisCache(new MemoryCache(new MemoryCacheOptions()));
        var vEngine = new CachingMetricsEngine(
            new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance), vCache, vStore);

        var vTechieFlow = await vEngine.AnalyseAsync(UserId, "techieflow");
        var vPlaybook = await vEngine.AnalyseAsync(UserId, "playbook");

        Assert.NotSame(vTechieFlow, vPlaybook);
        Assert.Equal("techieflow", vTechieFlow.Framework);
        Assert.Equal("playbook", vPlaybook.Framework);
    }

    /// <summary>Builds a cached engine over one seeded repository.</summary>
    /// <returns>The store, the cached engine and the cache.</returns>
    private static (FixtureTelemetryStore Store, CachingMetricsEngine Engine, MemoryAnalysisCache Cache) Build()
    {
        var vStore = new FixtureTelemetryStore().Seed(UserId, "acme/alpha", Framework, [GateFixtures.Gate()]);
        var vCache = new MemoryAnalysisCache(new MemoryCache(Options.Create(new MemoryCacheOptions())));
        var vEngine = new CachingMetricsEngine(
            new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance), vCache, vStore);
        return (vStore, vEngine, vCache);
    }
}
