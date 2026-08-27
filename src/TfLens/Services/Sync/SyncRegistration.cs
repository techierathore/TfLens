using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TfLens.Core.Abstractions;
using TfLens.Core.GitHub;

namespace TfLens.Services.Sync;

/// <summary>
/// Registers the GitHub fetch and background sync services with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else.
/// </remarks>
public static class SyncRegistration
{
    /// <summary>
    /// Adds the GitHub fetch and background sync services.
    /// </summary>
    /// <remarks>
    /// <see cref="GitHubStreamFetcher"/> is a typed client so its handler is pooled and its base
    /// address, <c>User-Agent</c> and optional PAT are applied in its own constructor. The runner is a
    /// <b>singleton</b> so the hosted poller and the repo registry's first-sync factory — which resolves
    /// from the root provider — share one instance; it opens its own scope per pass for the fetcher,
    /// store and parser, so no scoped service is captured.
    /// </remarks>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTfLensSync(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        aServices.AddHttpClient<IGitHubStreamFetcher, GitHubStreamFetcher>();

        aServices.AddSingleton<AnalysisCacheInvalidator>();
        aServices.AddSingleton<IRepoSyncRunner, RepoSyncRunner>();
        aServices.AddHostedService<RepoSyncService>();

        return aServices;
    }
}
