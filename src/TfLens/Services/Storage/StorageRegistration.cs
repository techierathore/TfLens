using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Parsing;
using TfLens.Core.Storage;

namespace TfLens.Services.Storage;

/// <summary>
/// Registers the PostgreSQL store and stream parser services with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else.
/// </remarks>
public static class StorageRegistration
{
    /// <summary>
    /// Adds the PostgreSQL store and stream parser services.
    /// </summary>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Both are scoped: the store opens one pooled <c>NpgsqlConnection</c> per unit of work and holds no
    /// state between them, and the parser is stateless. The connection string reaches the store through
    /// <see cref="TfLensOptions.DbConnection"/> — bound here from the <c>TfLens</c> section, which the
    /// PascalCase environment provider has already filled from <c>TfLensDbConnection</c>. Binding is
    /// idempotent, so it is safe alongside the same call in <c>Program.cs</c>.
    /// </remarks>
    public static IServiceCollection AddTfLensStorage(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        ArgumentNullException.ThrowIfNull(aServices);
        ArgumentNullException.ThrowIfNull(aConfiguration);

        aServices.Configure<TfLensOptions>(aConfiguration.GetSection(TfLensOptions.SectionName));

        aServices.AddScoped<IStreamParser, StreamParser>();
        aServices.AddScoped<ITelemetryStore, PostgresStore>();

        return aServices;
    }
}
