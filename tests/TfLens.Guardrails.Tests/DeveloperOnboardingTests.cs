using System.Text.RegularExpressions;
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
    private static string RepoRoot()
    {
        var vDir = new DirectoryInfo(AppContext.BaseDirectory);

        while (vDir is not null && !File.Exists(Path.Combine(vDir.FullName, "TfLens.slnx")))
        {
            vDir = vDir.Parent;
        }

        Assert.NotNull(vDir);
        return vDir!.FullName;
    }

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
    /// user secrets), so one profile is enough and anything a
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
    /// There is NO database default in source — in any environment.
    /// </summary>
    /// <remarks>
    /// This test is the inversion of the one it replaces. That test pinned a hard-coded development
    /// connection string (<c>Host=localhost;Port=5433;…;Password=tflensdev</c>) and asserted it stayed
    /// hard-coded. It did two kinds of harm: a credential lived in committed source, and every
    /// unconfigured developer was silently pointed at one specific database — which is how a second
    /// PostgreSQL container came to sit beside this machine's real local dev server, unnoticed
    /// (owner report 2026-08-29, MISS-TfLens-20260829-23). The connection string is now configuration
    /// with no fallback, and this test exists so nothing quietly reintroduces one.
    /// </remarks>
    [Fact]
    public void NoConnectionStringIsHardCodedInSource()
    {
        var vOptionsSource = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "TfLens.Core", "TfLensOptions.cs"));
        var vProgramSource = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "TfLens", "Program.cs"));

        foreach (var vFile in new[] { vOptionsSource, vProgramSource })
        {
            Regex.Matches(vFile, "Password=[A-Za-z0-9]")
                .Should().BeEmpty("no source file may carry a database password, throwaway or not");
        }

        // An unconfigured TfLens must REFUSE to start rather than guess a server.
        var vUnconfigured = new TfLensOptions { AppManagerAppId = 1 };
        vUnconfigured.DbConnection.Should().BeNull("there is no development fallback any more");

        var vAct = () => vUnconfigured.Validate();
        vAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*DbConnection*", "the failure must name the setting the developer has to supply");
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

        // Rewritten 2026-08-29 (MISS-TfLens-20260829-23). This test used to REQUIRE the message to say
        // "docker compose up -d postgres" and to quote a working "Port=5433" — that is, it pinned the
        // very instruction that told developers to stand up a second PostgreSQL server beside the one
        // their machine already ran, and pinned a hard-coded port as proof of helpfulness. The message
        // must still be actionable; it must no longer choose a database on the developer's behalf.
        vMessage.Should().Contain("TfLensDbConnection", "it must name the setting");
        vMessage.Should().Contain("user-secrets", "it must offer a way that commits nothing");
        vMessage.Should().Contain("TfLens-DevGuide.md", "it must point at the fuller instructions");
        vMessage.Should().Contain("ALREADY RUN",
            "it must send the developer to a server they already have, not to a new container");
        vMessage.Should().NotContain("docker compose up -d postgres",
            "starting a project-specific database is exactly the instruction that caused the incident");
    }

    /// <summary>
    /// The unreachable-database message explains the likely causes instead of dumping a driver trace.
    /// </summary>
    [Fact]
    public void UnreachableDatabaseMessageExplainsTheLikelyCauses()
    {
        var vMessage = TfLensOptions.UnreachableDatabaseMessage(
            new InvalidOperationException("Failed to connect to 127.0.0.1:59999"));

        // Same rewrite, same reason: the causes it lists are now about the developer's OWN server.
        vMessage.Should().Contain("Failed to connect", "the underlying cause must survive, not be swallowed");
        vMessage.Should().Contain("not running", "the commonest cause must be named first");
        vMessage.Should().Contain("does not exist", "a missing database is the second commonest cause");
        vMessage.Should().Contain("credentials", "wrong credentials must be named");
        vMessage.Should().NotContain("docker compose up -d postgres",
            "the message must not send a developer to create a second database for this project");
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

    /// <summary>
    /// The project declares a <c>UserSecretsId</c>, so the documented F5 path can actually read a secret.
    /// </summary>
    /// <remarks>
    /// Without this property, Visual Studio's *Manage User Secrets* has nowhere to write and
    /// <c>CreateBuilder</c> loads no secrets file — the app still starts (the Development connection
    /// fallback covers the database), so the breakage is silent: the AppManager pair simply never
    /// arrives and password reset dies with a 400 that names nothing. Deleting one line of the csproj
    /// must not be able to do that quietly (REQ-NFR-011, BRD-8).
    /// </remarks>
    [Fact]
    public void UserSecretsIdIsDeclaredSoTheF5PathKeepsWorking()
    {
        var vPath = Path.Combine(RepositoryRoot(), "src", "TfLens", "TfLens.csproj");

        File.Exists(vPath).Should().BeTrue($"{vPath} must exist");

        File.ReadAllText(vPath).Should().Contain(
            "<UserSecretsId>tflens-dev-secrets</UserSecretsId>",
            "user secrets are the documented local-development secrets path; without this id the " +
            "csproj silently stops loading secrets.json and the AppManager pair never reaches the app");
    }

    /// <summary>
    /// The committed user-secrets template holds placeholders only — never a real value.
    /// </summary>
    /// <remarks>
    /// The template exists so a developer can paste a correctly-keyed block into their own
    /// <c>secrets.json</c>. Its whole safety property is that it is empty: the moment someone fills it
    /// in "just to test", a live AppManager key is committed. Asserted on the key prefixes AppManager
    /// actually issues rather than on emptiness alone, so a placeholder like <c>ak_live_...</c> that
    /// looks harmless but trains the wrong habit also fails.
    /// </remarks>
    [Fact]
    public void UserSecretsTemplateContainsNoRealCredential()
    {
        var vPath = Path.Combine(RepositoryRoot(), "src", "TfLens", "secrets.example.json");

        File.Exists(vPath).Should().BeTrue(
            $"{vPath} is what the Developer Guide and README tell a developer to copy");

        var vText = File.ReadAllText(vPath);

        using var vDocument = JsonDocument.Parse(vText);

        foreach (var vProperty in vDocument.RootElement.EnumerateObject())
        {
            if (vProperty.Name.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            vProperty.Value.GetString().Should().BeEmpty(
                $"'{vProperty.Name}' is a template slot — a value here would be committed to git");
        }

        vText.Should().NotContain("ak_live_", "a committed file must not carry an AppManager key");
        vText.Should().NotContain("sk_live_", "a committed file must not carry an AppManager secret");
        vText.Should().NotContain("ghp_", "a committed file must not carry a GitHub PAT");
    }

    /// <summary>
    /// The Developer Guide names user secrets as the local-development path, not <c>.env</c>.
    /// </summary>
    /// <remarks>
    /// This is the documentation half of REQ-NFR-011, and it exists because the guide got it wrong.
    /// It opened with <c>copy .env.example .env</c>, which reads as "this is where the app's settings
    /// live" — but nothing in <c>Program.cs</c> parses <c>.env</c>, so every edit a developer made to
    /// it for an F5 run did nothing at all. The guide must name the mechanism that actually works and
    /// must say plainly that <c>.env</c> belongs to Compose.
    /// </remarks>
    [Fact]
    public void DeveloperGuideNamesUserSecretsAsTheLocalDevelopmentPath()
    {
        var vText = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "TfLens-DevGuide.md"));

        vText.Should().Contain("Manage User Secrets",
            "the Visual Studio gesture is the one a developer on Windows actually uses");
        vText.Should().Contain("dotnet user-secrets set",
            "the shell equivalent must be given too");
        vText.Should().Contain("secrets.example.json",
            "the guide must point at the template it tells the developer to copy");
        vText.Should().Contain("docker compose",
            "the guide must say which tool .env actually belongs to");
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
