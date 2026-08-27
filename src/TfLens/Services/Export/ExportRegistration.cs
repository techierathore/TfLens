using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TfLens.Core.Abstractions;
using TfLens.Core.Export;
using TfLens.Core.Metrics;

namespace TfLens.Services.Export;

/// <summary>
/// Registers the snapshot exporter and parity record services with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else.
/// </remarks>
public static class ExportRegistration
{
    /// <summary>
    /// Adds the snapshot exporter and the extra metrics it composes.
    /// </summary>
    /// <remarks>
    /// Both are scoped, because they read through <see cref="ITelemetryStore"/> and must live no longer
    /// than the connection scope that serves them — the <c>export</c> verb opens its own scope for
    /// exactly this reason (<c>CommandRunner.RunAsync</c>), so the button and the verb resolve the same
    /// object graph and a parity run exercises the code the pages use (ADR-005).
    /// <para>
    /// <see cref="IExtraMetrics"/> is registered here rather than beside the engine because the harness,
    /// routing and repricing figures have no parity oracle: they are the part of the export that the
    /// reference cannot check, and keeping them in the export area keeps that boundary visible.
    /// </para>
    /// </remarks>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aServices"/> was not supplied.</exception>
    public static IServiceCollection AddTfLensExport(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        ArgumentNullException.ThrowIfNull(aServices);

        aServices.AddScoped<IExtraMetrics, ExtraMetrics>();
        aServices.AddScoped<ISnapshotExporter, SnapshotExporter>();

        return aServices;
    }
}
