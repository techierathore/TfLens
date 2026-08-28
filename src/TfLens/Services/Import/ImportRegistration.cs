using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TfLens.Core.Import;

namespace TfLens.Services.Import;

/// <summary>
/// Registers the telemetry import service with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else.
/// </remarks>
public static class ImportRegistration
{
    /// <summary>
    /// Adds the import service.
    /// </summary>
    /// <remarks>
    /// Scoped, because it writes through <see cref="TfLens.Core.Abstractions.ITelemetryStore"/> and
    /// must live no longer than the connection scope serving the request. It resolves the same
    /// <see cref="TfLens.Core.Abstractions.IStreamParser"/> the sync runner resolves, which is what
    /// makes "the import path runs the shared parser" true by construction rather than by convention
    /// (REQ-FN-083).
    /// </remarks>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aServices"/> was not supplied.</exception>
    public static IServiceCollection AddTfLensImport(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        ArgumentNullException.ThrowIfNull(aServices);

        aServices.TryAddScoped<ITelemetryImportService, TelemetryImportService>();

        return aServices;
    }
}
