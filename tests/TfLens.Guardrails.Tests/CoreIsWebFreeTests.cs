using System.Text;
using TfLens.Core;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-008 (BRD-88, ADR-006) — the engine and parser live in <c>TfLens.Core</c> with no web
/// dependency, so they are driven by the CLI verbs and unit tests without a browser.
/// </summary>
/// <remarks>
/// Three independent checks, because each one alone has a hole: the project file says what was
/// intended, the compiled reference set says what actually got linked, and the source says what
/// someone is about to link. A regression trips at least one of them.
/// </remarks>
public sealed class CoreIsWebFreeTests
{
    /// <summary>Assembly-name prefixes that mean "this is a web or UI dependency".</summary>
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.JSInterop",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Http",
        "TrBlazeUI",
        "Serilog.AspNetCore"
    ];

    private static string CoreProjectPath =>
        Path.Combine(RepoTree.Root.FullName, "src", "TfLens.Core", "TfLens.Core.csproj");

    /// <summary><c>TfLens.Core</c> builds on the plain SDK, not the Web SDK.</summary>
    /// <remarks>
    /// <c>Microsoft.NET.Sdk.Web</c> adds an implicit <c>FrameworkReference</c> to
    /// <c>Microsoft.AspNetCore.App</c>, which would make the whole of ASP.NET reachable from the
    /// engine without a single <c>PackageReference</c> to show for it.
    /// </remarks>
    [Fact]
    public void CoreUsesThePlainSdkNotTheWebSdk()
    {
        var vProject = File.ReadAllText(CoreProjectPath);

        Assert.Contains("Sdk=\"Microsoft.NET.Sdk\"", vProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.NET.Sdk.Web", vProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.NET.Sdk.Razor", vProject, StringComparison.Ordinal);
        Assert.DoesNotContain("FrameworkReference", vProject, StringComparison.Ordinal);
    }

    /// <summary><c>TfLens.Core</c> declares no web or UI package, and references no web project.</summary>
    [Fact]
    public void CoreDeclaresNoWebPackageReference()
    {
        var vProject = File.ReadAllText(CoreProjectPath);
        var vFindings = ForbiddenPrefixes
            .Where(aPrefix => vProject.Contains(aPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(vFindings.Count == 0, Report("web packages declared by TfLens.Core.csproj", vFindings));
        Assert.DoesNotContain("ProjectReference", vProject, StringComparison.Ordinal);
    }

    /// <summary>The compiled <c>TfLens.Core</c> assembly links nothing from ASP.NET.</summary>
    /// <remarks>
    /// This is the check that survives someone adding a package that pulls ASP.NET in transitively:
    /// the reference set is what the compiler actually emitted, not what the project file claims.
    /// </remarks>
    [Fact]
    public void CompiledCoreAssemblyReferencesNothingFromAspNet()
    {
        var vCore = typeof(TfLensOptions).Assembly;

        Assert.Equal("TfLens.Core", vCore.GetName().Name);

        var vFindings = vCore.GetReferencedAssemblies()
            .Select(aReference => aReference.Name ?? string.Empty)
            .Where(aName => ForbiddenPrefixes.Any(aPrefix =>
                aName.StartsWith(aPrefix, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(aName => aName, StringComparer.Ordinal)
            .ToList();

        Assert.True(vFindings.Count == 0, Report("assemblies TfLens.Core links against", vFindings));
    }

    /// <summary>No source file under <c>src/TfLens.Core</c> reaches for a web or UI namespace.</summary>
    [Fact]
    public void CoreSourceImportsNoWebNamespace()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", Path.Combine("src", "TfLens.Core")))
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vTrimmed = vLines[vIndex].TrimStart();

                if (!vTrimmed.StartsWith("using ", StringComparison.Ordinal))
                {
                    continue;
                }

                var vNamespace = vTrimmed[6..].TrimEnd(';', ' ');

                if (ForbiddenPrefixes.Any(aPrefix =>
                        vNamespace.StartsWith(aPrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vTrimmed}");
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report("web namespaces imported by TfLens.Core source", vFindings));
    }

    /// <summary>The fixture folder the parser and engine tests read from exists and is checked in.</summary>
    /// <remarks>
    /// BRD-88's second clause. This deliberately fails loudly while the fixtures are missing rather
    /// than quietly passing on an empty directory — a fixture-driven test suite with no fixtures
    /// proves nothing.
    /// </remarks>
    [Fact]
    [Trait("Category", "Blocked")]
    public void FixtureJsonlIsCheckedInUnderTests()
    {
        var vFixtures = RepoTree.Files("*.jsonl", "tests");

        Assert.True(
            vFixtures.Count > 0,
            "REQ-NFR-008 — no fixture .jsonl is checked in under tests/. The engine and parser tests " +
            "must be driven by real stream fixtures (BRD-88). Owned by the engine/parser cluster.");
    }

    /// <summary>Formats a finding list into a failure message someone can act on.</summary>
    /// <param name="aWhat">What was being looked for.</param>
    /// <param name="aFindings">The findings.</param>
    /// <returns>A multi-line report.</returns>
    private static string Report(string aWhat, IReadOnlyList<string> aFindings)
    {
        var vBuilder = new StringBuilder();
        vBuilder.AppendLine($"REQ-NFR-008 / ADR-006 — found {aFindings.Count} {aWhat}:");

        foreach (var vFinding in aFindings)
        {
            vBuilder.AppendLine($"  {vFinding}");
        }

        vBuilder.AppendLine("TfLens.Core must stay drivable by the CLI verbs and unit tests without a browser.");

        return vBuilder.ToString();
    }
}
