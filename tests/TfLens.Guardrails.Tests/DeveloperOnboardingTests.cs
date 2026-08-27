using System.Text.Json;
using FluentAssertions;
using TfLens.Core;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// The clone-and-press-F5 path stays working.
/// </summary>
/// <remarks>
/// <para>
/// These exist because it broke. The application was built, run and smoked many times — but always
/// with <c>TfLensDbConnection</c> exported in a shell, so nobody exercised the path a developer
/// actually uses. Opening the solution and pressing F5 crashed on a bare
/// "supply it as a PascalCase environment variable", with no indication of what value, where, or how
/// on Windows. A working application read as a broken one.
/// </para>
/// <para>
/// Each test here pins one part of that first-run experience, so it cannot regress unnoticed.
/// </para>
/// </remarks>
public sealed class DeveloperOnboardingTests
{
    /// <summary>Repository-relative path of the launch profiles.</summary>
    private static string LaunchSettingsPath =>
        Path.Combine(RepositoryRoot(), "src", "TfLens", "Properties", "launchSettings.json");

    /// <summary>
    /// There is exactly one launch profile, and it does not pin the connection string.
    /// </summary>
    /// <remarks>
    /// The first attempt at fixing the F5 crash put the connection string in the launch profile, which
    /// makes it an environment variable — the HIGHEST-priority configuration source. That silently
    /// overrode <c>dotnet user-secrets set TfLens:DbConnection</c>, the exact mechanism the Developer
    /// Guide recommends, so the documented way to use your own database did nothing. It also meant four
    /// profiles, one of which existed only to fail.
    /// The default now lives in code as the LOWEST-priority source (see
    /// <see cref="TfLensOptions.LocalDevelopmentConnection"/>), so one profile is enough and anything a
    /// developer sets overrides it.
    /// </remarks>
    [Fact]
    public void ThereIsOneLaunchProfileAndItDoesNotPinTheConnectionString()
    {
        var vProfiles = ReadProfiles();

        vProfiles.Should().HaveCount(
            1,
            "extra launch profiles are clutter in the run dropdown; the single profile works out of " +
            "the box and anything else is set through configuration");

        vProfiles[0].Value.TryGetProperty("environmentVariables", out var vEnvironment)
            .Should().BeTrue();

        vEnvironment.TryGetProperty("TfLensDbConnection", out _)
            .Should().BeFalse(
                "a connection string in a launch profile becomes an environment variable, which " +
                "outranks user secrets and would silently override them");
    }

    /// <summary>
    /// The development default is a real, local connection string.
    /// </summary>
    [Fact]
    public void DevelopmentDefaultPointsAtTheLocalComposeDatabase()
    {
        TfLensOptions.LocalDevelopmentConnection.Should().Contain("Host=localhost");
        TfLensOptions.LocalDevelopmentConnection.Should().Contain("Port=5433",
            "docker-compose.override.yml publishes the compose Postgres on 5433 for local development");
        TfLensOptions.LocalDevelopmentConnection.Should().Contain("Database=tflens");
    }

    /// <summary>
    /// The missing-connection-string message tells a developer what to do, not merely what is wrong.
    /// </summary>
    /// <remarks>
    /// Asserted on substance rather than wording: it must name the command that starts the database,
    /// the setting, an example value, and where the fuller instructions live.
    /// </remarks>
    [Fact]
    public void MissingConnectionStringMessageIsActionable()
    {
        var vOptions = new TfLensOptions { DbConnection = null };

        var vMessage = vOptions.Invoking(aO => aO.Validate())
            .Should().Throw<InvalidOperationException>().Which.Message;

        vMessage.Should().Contain("docker compose up -d postgres", "it must name how to start the database");
        vMessage.Should().Contain("TfLensDbConnection", "it must name the setting");
        vMessage.Should().Contain("Port=5433", "it must give a value that actually works locally");
        vMessage.Should().Contain("user-secrets", "it must offer a way that commits nothing");
        vMessage.Should().Contain("TfLens-DevGuide.md", "it must point at the fuller instructions");
    }

    /// <summary>
    /// The unreachable-database message explains the likely causes instead of dumping a driver trace.
    /// </summary>
    [Fact]
    public void UnreachableDatabaseMessageExplainsTheLikelyCauses()
    {
        var vMessage = TfLensOptions.UnreachableDatabaseMessage(
            new InvalidOperationException("Failed to connect to 127.0.0.1:59999"));

        vMessage.Should().Contain("docker compose up -d postgres");
        vMessage.Should().Contain(".env");
        vMessage.Should().Contain("docker-compose.override.yml", "the published port is the usual cause");
        vMessage.Should().Contain("Failed to connect", "the underlying cause must survive, not be swallowed");
    }

    /// <summary>
    /// A half-configured AppManager key pair is refused with a message that says why.
    /// </summary>
    [Fact]
    public void HalfConfiguredAppManagerPairIsRefused()
    {
        var vOptions = new TfLensOptions
        {
            DbConnection = "Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=x",
            AppManagerApiKey = "ak_live_something",
            AppManagerApiSecret = null
        };

        vOptions.Invoking(aO => aO.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*together*");
    }

    /// <summary>
    /// A developer guide exists and covers the first thing a newcomer needs.
    /// </summary>
    /// <remarks>
    /// The absence of any developer guide is what turned a one-line configuration gap into a dead end,
    /// so its existence is pinned here rather than left to good intentions.
    /// </remarks>
    [Fact]
    public void DeveloperGuideExistsAndCoversRunningLocally()
    {
        var vPath = Path.Combine(RepositoryRoot(), "docs", "TfLens-DevGuide.md");

        File.Exists(vPath).Should().BeTrue($"the developer guide must exist at {vPath}");

        var vText = File.ReadAllText(vPath);

        vText.Should().Contain("Running TfLens locally");
        vText.Should().Contain("Troubleshooting");
        vText.Should().Contain("TfLensDbConnection");
        vText.Should().Contain("docker compose up -d postgres");
    }

    /// <summary>Reads the launch profiles in file order.</summary>
    /// <returns>Each profile's name and body.</returns>
    private static List<(string Name, JsonElement Value)> ReadProfiles()
    {
        File.Exists(LaunchSettingsPath).Should().BeTrue($"{LaunchSettingsPath} must exist");

        using var vDocument = JsonDocument.Parse(File.ReadAllText(LaunchSettingsPath));

        return vDocument.RootElement
            .GetProperty("profiles")
            .EnumerateObject()
            .Select(aP => (aP.Name, aP.Value.Clone()))
            .ToList();
    }

    /// <summary>Walks up from the test binary to the repository root.</summary>
    /// <returns>The directory holding the solution file.</returns>
    /// <exception cref="InvalidOperationException">The root could not be located.</exception>
    private static string RepositoryRoot()
    {
        var vDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (vDirectory is not null && vDirectory.GetFiles("TfLens.slnx").Length == 0)
        {
            vDirectory = vDirectory.Parent;
        }

        return vDirectory?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
