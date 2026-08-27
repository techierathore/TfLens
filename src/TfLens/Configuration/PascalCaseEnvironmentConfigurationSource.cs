using Microsoft.Extensions.Configuration;

namespace TfLens.Configuration;

/// <summary>
/// Maps PascalCase environment variables onto <c>:</c>-nested configuration paths.
/// </summary>
/// <remarks>
/// The Coding Standards fix the environment-variable spelling as <c>TfLensDbConnection</c> — not
/// <c>TFLENS_DB_CONNECTION</c> and not <c>TfLens__DbConnection</c>. This provider is what makes that
/// spelling reachable through <c>IConfiguration["TfLens:DbConnection"]</c>, so application code never
/// calls <c>Environment.GetEnvironmentVariable</c> directly.
/// </remarks>
public sealed class PascalCaseEnvironmentConfigurationSource : IConfigurationSource
{
    /// <summary>The variable-name prefix that marks a TfLens setting.</summary>
    public string Prefix { get; init; } = "TfLens";

    /// <summary>
    /// Builds the provider.
    /// </summary>
    /// <param name="aBuilder">The configuration builder adding this source.</param>
    /// <returns>A provider that reads the process environment.</returns>
    public IConfigurationProvider Build(IConfigurationBuilder aBuilder) =>
        new PascalCaseEnvironmentConfigurationProvider(Prefix);
}

/// <summary>
/// Reads <c>TfLens*</c> environment variables into the <c>TfLens:*</c> configuration section.
/// </summary>
/// <remarks>
/// <c>TfLensAppManagerApiKey</c> becomes <c>TfLens:AppManagerApiKey</c>: the prefix is stripped and the
/// remainder is used verbatim, because the option property names are themselves PascalCase. Nothing is
/// logged here — the values are secrets.
/// </remarks>
public sealed class PascalCaseEnvironmentConfigurationProvider : ConfigurationProvider
{
    private readonly string objPrefix;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <param name="aPrefix">The variable-name prefix that marks a TfLens setting.</param>
    public PascalCaseEnvironmentConfigurationProvider(string aPrefix)
    {
        objPrefix = aPrefix;
    }

    /// <summary>
    /// Reads the process environment into <see cref="ConfigurationProvider.Data"/>.
    /// </summary>
    /// <remarks>Variables that do not start with the prefix, or that are exactly the prefix, are ignored.</remarks>
    public override void Load()
    {
        var vData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry vEntry in Environment.GetEnvironmentVariables())
        {
            var vName = vEntry.Key as string;
            if (string.IsNullOrEmpty(vName) || !vName.StartsWith(objPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var vTail = vName[objPrefix.Length..];
            if (vTail.Length == 0)
            {
                continue;
            }

            vData[$"{objPrefix}:{vTail}"] = vEntry.Value as string;
        }

        Data = vData;
    }
}

/// <summary>Adds the PascalCase environment provider to a configuration builder.</summary>
public static class PascalCaseEnvironmentConfigurationExtensions
{
    /// <summary>
    /// Registers the provider that maps <c>TfLens*</c> environment variables to <c>TfLens:*</c> keys.
    /// </summary>
    /// <param name="aBuilder">The configuration builder.</param>
    /// <param name="aPrefix">The variable-name prefix; defaults to <c>TfLens</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IConfigurationBuilder AddPascalCaseEnvironmentVariables(
        this IConfigurationBuilder aBuilder,
        string aPrefix = "TfLens")
    {
        aBuilder.Add(new PascalCaseEnvironmentConfigurationSource { Prefix = aPrefix });
        return aBuilder;
    }
}
