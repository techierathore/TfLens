using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TfLens.Core.Abstractions;
using TfLens.Core.Playbook;

namespace TfLens.Services.Playbook;

/// <summary>
/// Registers the Playbook adapter services with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else.
/// </remarks>
public static class PlaybookRegistration
{
    /// <summary>
    /// Adds the Playbook adapter services.
    /// </summary>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Both are <b>scoped</b>, because both constructor-inject scoped dependencies —
    /// <c>PlaybookAdapter</c> takes <see cref="IStreamParser"/> and <c>PlaybookReportBuilder</c> takes
    /// <see cref="ITelemetryStore"/>, and both of those own a per-request database connection.
    /// Registering them as singletons made the container refuse to start with a captive-dependency
    /// error, which is the correct outcome: a singleton holding a scoped store would have pinned one
    /// connection for the life of the process and quietly shared it across every user's requests — in
    /// an app whose central safety property is per-user isolation, that is the worst possible bug to
    /// take on for the sake of avoiding an allocation.
    /// </para>
    /// <para>
    /// Nothing here is conditional on a repository being a Playbook one: whether the adapter is used at
    /// all is <c>PlaybookRouting</c>'s decision, taken per repository from the telemetry layout it
    /// committed (REQ-FN-069) — a repository that emits schema-v1 streams reaches the shared parser,
    /// engine and pages without any of this being involved.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddTfLensPlaybook(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        aServices.AddScoped<IPlaybookAdapter, PlaybookAdapter>();
        aServices.AddScoped<IPlaybookReportBuilder, PlaybookReportBuilder>();
        return aServices;
    }
}
