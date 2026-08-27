using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TfLens.Core.Abstractions;
using TfLens.Core.Repos;

namespace TfLens.Services.Repos;

/// <summary>
/// Registers the per-user repository registry services with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else.
/// </remarks>
public static class ReposRegistration
{
    /// <summary>
    /// Adds the per-user repository registry services.
    /// </summary>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="RepoRegistry"/> is registered once and surfaced under both of its interfaces, so the
    /// Repos page's counted read (<see cref="IRepoListReader"/>) and the connect / remove path
    /// (<see cref="IRepoRegistry"/>) are the same user-scoped object.
    /// </para>
    /// <para>
    /// The sync runner is injected as a factory rather than as an instance. That breaks the potential
    /// registration cycle — connect queues a sync, and sync reads the registry's repositories — and it
    /// keeps the registry constructible before the Sync area has registered anything. The factory
    /// resolves from the root provider and returns <c>null</c> (or throws, which the registry logs and
    /// tolerates) when no runner is registered; the repository is stored either way and the background
    /// poller picks it up on its next tick.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddTfLensRepos(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        aServices.TryAddSingleton<Func<IRepoSyncRunner?>>(aServiceProvider =>
            aServiceProvider.GetService<IRepoSyncRunner>);

        aServices.TryAddScoped<RepoRegistry>();
        aServices.TryAddScoped<IRepoRegistry>(aServiceProvider => aServiceProvider.GetRequiredService<RepoRegistry>());
        aServices.TryAddScoped<IRepoListReader>(aServiceProvider => aServiceProvider.GetRequiredService<RepoRegistry>());

        return aServices;
    }
}
