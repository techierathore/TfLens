using Microsoft.Extensions.Configuration;

namespace TfLens.Core.Tests;

/// <summary>
/// Resolves the PostgreSQL the database-backed Core tests run against — the same way the app does.
/// </summary>
/// <remarks>
/// <para>
/// Added 2026-08-29 (owner report, <c>MISS-TfLens-20260829-23</c>). Three test classes each carried
/// their own copy of the literal
/// <c>Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=tflensdev</c> — a fourth and
/// fifth copy of it lived in <c>TfLensOptions</c> and in the integration fixture. Five copies of one
/// credential, all naming one specific container, is what made the wrong database invisible: every
/// layer agreed with every other layer, so nothing ever disagreed loudly enough to be noticed, and a
/// second PostgreSQL server sat beside the machine's real one for a day without a single failure.
/// </para>
/// <para>
/// There is deliberately **no default here**. Precedence matches the application exactly —
/// <c>TfLensDbConnection</c> environment variable first, then the <c>TfLens:DbConnection</c> user
/// secret — so tests and app can never drift onto different servers again. When nothing is configured
/// the tests report themselves unavailable with the command to fix it, rather than dialling a server
/// nobody chose.
/// </para>
/// </remarks>
public static class TestDatabase
{
    /// <summary>The user-secrets store the TfLens head declares.</summary>
    private const string UserSecretsId = "tflens-dev-secrets";

    /// <summary>
    /// The configured connection string, or <c>null</c> when none is configured.
    /// </summary>
    public static string? ConnectionStringOrNull()
    {
        var vFromEnvironment = Environment.GetEnvironmentVariable("TfLensDbConnection");

        if (!string.IsNullOrWhiteSpace(vFromEnvironment))
        {
            return vFromEnvironment;
        }

        var vFromSecrets = new ConfigurationBuilder()
            .AddUserSecrets(UserSecretsId)
            .Build()["TfLens:DbConnection"];

        return string.IsNullOrWhiteSpace(vFromSecrets) ? null : vFromSecrets;
    }

    /// <summary>What to tell a developer whose machine has no database configured.</summary>
    public const string NotConfiguredReason =
        "No database is configured. These tests read the SAME setting the app does and there is no " +
        "hard-coded default. Point them at a PostgreSQL server you already run:\n" +
        "  dotnet user-secrets set \"TfLens:DbConnection\" " +
        "\"Host=…;Port=…;Database=tflens;Username=…;Password=…\" --project src/TfLens\n" +
        "or set TfLensDbConnection for this run.";
}
