using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TfLens.Core.Abstractions;
using TfLens.Core.Metrics;

namespace TfLens.Services.Metrics;

/// <summary>
/// Registers the metrics engine and extra metrics services with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else.
/// </remarks>
public static class MetricsRegistration
{
    /// <summary>
    /// Adds the metrics engine and extra metrics services.
    /// </summary>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <see cref="IMetricsEngine"/> resolves to <see cref="CachingMetricsEngine"/>, which decorates the
    /// <see cref="MetricsEngine"/> port with the memoisation of REQ-FN-046 — so every consumer, pages
    /// and export alike, goes through one code path and no caller can opt out of the cache or into a
    /// different engine. <see cref="IAnalysisCache"/> is a singleton because the memoisation outlives a
    /// request and is invalidated by a completed sync or rebuild (REQ-FN-026), which the sync area
    /// calls through <see cref="IAnalysisCache.Invalidate"/>. There is deliberately no configuration
    /// read here: no key relaxes a provenance rule (REQ-NFR-009).
    /// </remarks>
    public static IServiceCollection AddTfLensMetrics(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        ArgumentNullException.ThrowIfNull(aServices);
        ArgumentNullException.ThrowIfNull(aConfiguration);

        aServices.AddMemoryCache();
        aServices.AddSingleton<IAnalysisCache, MemoryAnalysisCache>();
        aServices.AddScoped<MetricsEngine>();
        aServices.AddScoped<IMetricsEngine, CachingMetricsEngine>();

        return aServices;
    }
}
