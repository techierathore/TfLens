using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TfLens.Services.Ui;

/// <summary>
/// Registers the per-user UI state (theme, framework switch) services with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else.
/// </remarks>
public static class UiRegistration
{
    /// <summary>
    /// Adds the per-user UI state (theme, framework switch) services.
    /// </summary>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTfLensUiState(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        // Both are per-circuit: the shell's repo counts and the theme/framework choice belong to one
        // signed-in user's session, never to the process.
        aServices.AddScoped<ShellPreferences>();
        aServices.AddScoped<ShellState>();

        return aServices;
    }
}
