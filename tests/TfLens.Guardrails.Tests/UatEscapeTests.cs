using System.Text.Json;
using FluentAssertions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// The defects the owner found in the 2026-08-28 UAT session, pinned so they cannot come back.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these got past the full gate set — acceptance, data-render, visual-truth, standards,
/// parity and perf all passed on the same build the owner then found four defects in. That is the
/// point worth keeping: none of them were subtle at runtime, they were simply things no gate was
/// looking at. Each test below is the cheapest possible check that would have caught one.
/// </para>
/// <para>
/// The two UI halves are pinned in <c>tests/verify/asset-integrity.spec.ts</c>, which needs a browser.
/// These are the file-level halves, which need nothing and therefore always run.
/// </para>
/// </remarks>
public sealed class UatEscapeTests
{
    /// <summary>
    /// REQ-NFR-016 — build output is ignored, so machine-absolute manifests cannot be committed.
    /// </summary>
    /// <remarks>
    /// <c>bin/Debug/net10.0/TfLens.staticwebassets.runtime.json</c> carries ABSOLUTE static-web-asset
    /// content roots. A WSL build writes <c>/mnt/c/…</c> and <c>/home/&lt;user&gt;/.nuget/…</c>; a
    /// Windows build rewrites the same file to <c>C:\1MyCode\…</c> and
    /// <c>C:\Users\&lt;user&gt;\.nuget\…</c> — both were captured on 2026-08-28. Committing that ships
    /// one machine's absolute paths to another, which is how a static web asset can resolve on one
    /// developer's box and 404 on the next. Asserted on <c>.gitignore</c> rather than by shelling out
    /// to git so the test is hermetic and runs anywhere.
    /// </remarks>
    [Fact]
    public void BuildOutputIsGitIgnored()
    {
        var vText = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));

        var vLines = vText
            .Split('\n')
            .Select(aLine => aLine.Trim())
            .Where(aLine => aLine.Length > 0 && !aLine.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

        vLines.Overlaps(["bin/", "[Bb]in/"]).Should().BeTrue(
            ".gitignore must ignore bin/ — committed build output carries machine-absolute static-web-asset paths");

        vLines.Overlaps(["obj/", "[Oo]bj/"]).Should().BeTrue(
            ".gitignore must ignore obj/ — it holds the scoped-CSS bundle and the compressed asset copies");
    }

    /// <summary>
    /// REQ-NFR-011 — the user-secrets template is the WHOLE local configuration surface.
    /// </summary>
    /// <remarks>
    /// The owner's report was that configuration is scattered: the AppManager pair and the GitHub token
    /// were in user secrets, the connection string was a hardcoded C# constant, the ApplicationId was a
    /// code default, and the container password was in <c>.env</c> — four places to read before you know
    /// how the app is configured. The template now lists every setting, secret or not, so one file
    /// answers the question. A setting added to <see cref="TfLens.Core.TfLensOptions"/> and not listed
    /// here re-opens exactly that gap.
    /// </remarks>
    [Fact]
    public void UserSecretsTemplateListsEverySetting()
    {
        var vPath = Path.Combine(RepositoryRoot(), "src", "TfLens", "secrets.example.json");
        var vText = File.ReadAllText(vPath);

        using var vDocument = JsonDocument.Parse(vText);

        var vKeys = vDocument.RootElement
            .EnumerateObject()
            .Select(aProperty => aProperty.Name)
            .Where(aName => !aName.StartsWith("//", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        string[] vExpected =
        [
            "TfLens:DbConnection",
            "TfLens:AppManagerApiKey",
            "TfLens:AppManagerApiSecret",
            "TfLens:AppManagerAppId",
            "TfLens:AppManagerBaseUrl",
            "TfLens:GitHubToken",
            "TfLens:DataRoot",
            "TfLens:PollIntervalMinutes",
            "TfLens:StalenessDays"
        ];

        foreach (var vSetting in vExpected)
        {
            vKeys.Should().Contain(vSetting,
                $"'{vSetting}' is part of the configuration surface and the template is meant to be all of it");
        }
    }

    /// <summary>
    /// REQ-NFR-011 — the Compose password and the code's Development fallback still agree.
    /// </summary>
    /// <remarks>
    /// <c>docker compose up -d postgres</c> interpolates <c>TfLensDbPassword</c> from <c>.env</c>, while
    /// <c>dotnet run</c> falls back to <see cref="TfLens.Core.TfLensOptions.LocalDevelopmentConnection"/>.
    /// If the two disagree, Compose brings up a database the app cannot authenticate against, and the
    /// error names the database rather than the file that caused it.
    /// </remarks>
    [Fact]
    public void ComposePasswordMatchesTheDevelopmentFallback()
    {
        var vEnvExample = File.ReadAllText(Path.Combine(RepositoryRoot(), ".env.example"));

        var vDeclared = vEnvExample
            .Split('\n')
            .Select(aLine => aLine.Trim())
            .FirstOrDefault(aLine => aLine.StartsWith("TfLensDbPassword=", StringComparison.Ordinal));

        vDeclared.Should().NotBeNull(".env.example must declare TfLensDbPassword for the compose service");

        var vPassword = vDeclared!["TfLensDbPassword=".Length..].Split('#')[0].Trim();

        TfLens.Core.TfLensOptions.LocalDevelopmentConnection.Should().Contain(
            $"Password={vPassword}",
            "the compose password and the app's Development fallback must be the same value, or the container and the app disagree");
    }

    /// <summary>
    /// REQ-NFR-017 — the Developer Guide leads with the screens, not with setup.
    /// </summary>
    /// <remarks>
    /// The guide's purpose is debugging a screen. It had grown to open with ~300 lines of setup,
    /// configuration and troubleshooting — including four separate "how to run it" variants — and named
    /// the screen-by-screen reference once, on its last line. The owner opened it to debug screens and
    /// never reached that material. This asserts the ordering, which is the part that actually failed.
    /// </remarks>
    [Fact]
    public void DeveloperGuideLeadsWithTheScreens()
    {
        var vText = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "TfLens-DevGuide.md"));

        var vScreens = vText.IndexOf("## Screen-by-screen reference", StringComparison.Ordinal);
        var vSetup = vText.IndexOf("## Running TfLens locally", StringComparison.Ordinal);

        vScreens.Should().BeGreaterThan(-1, "the guide must carry a screen-by-screen section");
        vSetup.Should().BeGreaterThan(-1, "the guide must still tell a new developer how to run it");

        vScreens.Should().BeLessThan(vSetup,
            "the screen-by-screen reference must come BEFORE the setup instructions — a developer opens this guide to debug a screen");

        // The reference is only useful if the guide names the screens; a bare link is what it had before.
        foreach (var vRoute in new[] { "/login", "/repos", "/misses", "/export" })
        {
            vText.Should().Contain(vRoute,
                "the screen index must name each route so a developer can find their screen without opening the other file");
        }
    }

    /// <summary>
    /// REQ-UI-001 / REQ-NFR-015 — the anonymous layout is not delivered by the scoped-CSS bundle.
    /// </summary>
    /// <remarks>
    /// The whole of <c>/login</c>'s layout used to live in <c>AuthLayout.razor.css</c>, i.e. in
    /// <c>TfLens.styles.css</c>. When that one file did not arrive, the page collapsed into an unstyled
    /// single column — reproduced pixel-for-pixel against the owner's screenshot — with no console
    /// error and no log line. The rules now live in <c>wwwroot/app.css</c>, which is served straight
    /// from the web root and does not go through the static-web-assets manifest.
    /// </remarks>
    [Fact]
    public void AnonymousLayoutStylesAreNotInTheScopedBundle()
    {
        var vLayoutDirectory = Path.Combine(RepositoryRoot(), "src", "TfLens", "Components", "Layout");

        File.Exists(Path.Combine(vLayoutDirectory, "AuthLayout.razor.css")).Should().BeFalse(
            "the auth layout must not be delivered by the scoped-CSS bundle — that is the single point of failure REQ-UI-001 escaped through");

        var vAppCss = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TfLens", "wwwroot", "app.css"));

        foreach (var vRule in new[] { ".auth-split", ".auth-brand", ".auth-right", ".auth-panel", ".auth-bullets" })
        {
            vAppCss.Should().Contain(vRule,
                $"'{vRule}' lays out every anonymous screen and must ship in app.css, which is served from wwwroot");
        }

        vAppCss.Should().Contain("flex-direction: row",
            "the two-column split at >=768px is the layout's whole point and must survive in app.css");
    }

    /// <summary>
    /// REQ-UI-044 — the source flows are routes, and no modal dialog creeps back into them.
    /// </summary>
    /// <remarks>
    /// The owner reported that deleting and adding a source could leave the whole page dimmed and
    /// dead — a mounted backdrop with no panel on it. Nine reproduction attempts in headless Chromium
    /// could not produce that state, and a construct whose failure mode the harness cannot reproduce
    /// is one the harness cannot sign off either. So the construct is gone: Add source, Re-import and
    /// Remove are routed pages. The behavioural half is asserted against the running app in
    /// <c>tests/verify/ui-auth-shell.spec.ts</c> (<c>expectNoOverlay</c>); this is the structural half,
    /// which needs no browser and therefore always runs.
    /// </remarks>
    [Fact]
    public void SourceFlowsAreRoutesAndNotDialogs()
    {
        var vPages = Path.Combine(RepositoryRoot(), "src", "TfLens", "Components", "Pages");

        foreach (var (vFile, vRoute) in new[]
        {
            ("AddSource.razor", "@page \"/repos/add\""),
            ("RemoveSource.razor", "@page \"/repos/remove/{Source}\"")
        })
        {
            var vPath = Path.Combine(vPages, vFile);

            File.Exists(vPath).Should().BeTrue($"{vFile} is the routed page that replaced a modal dialog");
            File.ReadAllText(vPath).Should().Contain(vRoute, $"{vFile} must be reachable at its own URL");
        }

        var vRepos = File.ReadAllText(Path.Combine(vPages, "Repos.razor"));

        foreach (var vDialog in new[] { "<Dialog", "<AlertDialog", "DialogContent", "AlertDialogContent" })
        {
            vRepos.Should().NotContain(vDialog,
                $"Repos.razor is the grid only — '{vDialog}' means a source flow moved back into an overlay (REQ-UI-044)");
        }

        // The Escape watcher existed ONLY because TrBlazeUI's AlertDialog ships none (TR-014). If it
        // is back, an overlay is back with it.
        File.Exists(Path.Combine(vPages, "Repos.razor.js")).Should().BeFalse(
            "Repos.razor.js held the dialog-only Escape watcher; the import half lives in AddSource.razor.js now");
    }

    /// <summary>Walks up from the test assembly until the repository root is found.</summary>
    private static string RepositoryRoot()
    {
        var vDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (vDirectory is not null && !File.Exists(Path.Combine(vDirectory.FullName, "TfLens.slnx")))
        {
            vDirectory = vDirectory.Parent;
        }

        vDirectory.Should().NotBeNull("the tests must run from inside the repository");

        return vDirectory!.FullName;
    }
}
