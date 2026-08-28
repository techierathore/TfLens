using System.Text.RegularExpressions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-014 (BRD-139) — the upload surface is the app's <b>only</b> inbound path, and there is no
/// unauthenticated endpoint and no machine-to-machine ingest API.
/// </summary>
/// <remarks>
/// <para>
/// This enumerates the routes from the source rather than from a built pipeline, for the same reason
/// every other guardrail here does: the claim is a negative — "no such endpoint exists" — and a
/// negative cannot be demonstrated by exercising the endpoints that do exist. The check is therefore
/// over every <c>Map*</c> call in the web head, and it fails on a route that is neither explicitly
/// authorized nor on the closed anonymous list.
/// </para>
/// <para>
/// The anonymous list is read from <c>AnonymousRoutes.cs</c> rather than duplicated, so adding an
/// anonymous route is a visible edit to the one file BRD-2 makes the register of them.
/// </para>
/// </remarks>
public sealed class ImportSurfaceTests
{
    /// <summary>Matches a minimal-API mapping and captures its route literal when it has one.</summary>
    private static readonly Regex MapCall = new(
        @"\bMap(?<Verb>Get|Post|Put|Patch|Delete|Methods)\s*\(\s*(?<Route>[A-Za-z0-9_.""/-]+)",
        RegexOptions.Compiled);

    /// <summary>Words that would name a capture layer or a machine-to-machine ingest surface.</summary>
    private static readonly string[] MachineIngestWords =
        ["ingest", "capture", "collect", "otlp", "webhook", "/v1/metrics", "/v1/traces", "/v1/logs", "apikey"];

    /// <summary>
    /// Every mapped route either requires authorization or is on the closed anonymous list.
    /// </summary>
    [Fact]
    public void EveryMappedRouteIsAuthorizedOrExplicitlyAnonymous()
    {
        var vAnonymous = AnonymousRouteLiterals();
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", Path.Combine("src", "TfLens")))
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (IsComment(vLine) || !MapCall.IsMatch(vLine))
                {
                    continue;
                }

                // The framework's own component and health mappings are named, not routed.
                if (vLine.Contains("MapRazorComponents", StringComparison.Ordinal)
                    || vLine.Contains("MapHealthEndpoint", StringComparison.Ordinal)
                    || vLine.Contains("MapAuthEndpoints", StringComparison.Ordinal)
                    || vLine.Contains("MapExportEndpoints", StringComparison.Ordinal)
                    || vLine.Contains("MapImportEndpoints", StringComparison.Ordinal))
                {
                    continue;
                }

                var vRoute = MapCall.Match(vLine).Groups["Route"].Value.Trim('"');
                var vIsAuthorized = vLine.Contains("RequireAuthorization", StringComparison.Ordinal);
                var vIsAnonymous = vLine.Contains("AllowAnonymous", StringComparison.Ordinal)
                                   || vAnonymous.Contains(vRoute, StringComparer.OrdinalIgnoreCase);

                if (!vIsAuthorized && !vIsAnonymous)
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLine.Trim()}");
                }
            }
        }

        Assert.True(
            vFindings.Count == 0,
            "REQ-NFR-014 — every mapped route must call RequireAuthorization() or be on the closed "
            + "anonymous list in AnonymousRoutes.cs. Unclassified:\n  " + string.Join("\n  ", vFindings));
    }

    /// <summary>
    /// The two import routes require authentication, and neither takes a user id.
    /// </summary>
    /// <remarks>
    /// A route that took a user id would be a route another account could be reached through; the
    /// isolation has to be the shape of the endpoint, not a check somebody remembered (ADR-013).
    /// </remarks>
    [Fact]
    public void TheImportRoutesAreAuthenticatedAndTakeNoUserId()
    {
        var vEndpoints = File.ReadAllText(ImportEndpointsPath);

        Assert.Contains("MapPost(PreviewRoute, PreviewAsync).RequireAuthorization()", vEndpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(CommitRoute, CommitAsync).RequireAuthorization()", vEndpoints, StringComparison.Ordinal);
        Assert.Contains("RequireUserId()", vEndpoints, StringComparison.Ordinal);

        Assert.DoesNotContain("\"userId\"", vEndpoints, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AllowAnonymous", vEndpoints, StringComparison.Ordinal);
    }

    /// <summary>
    /// No route anywhere in the head names a capture layer or a machine-to-machine ingest surface.
    /// </summary>
    /// <remarks>
    /// BRD §3, as narrowed on 2026-08-28: the import file picker is the only inbound path, and it is a
    /// human signing in and choosing a file. Nothing pushes into TfLens, and no endpoint accepts an
    /// unauthenticated post.
    /// </remarks>
    [Fact]
    public void NoRouteNamesAMachineToMachineIngestSurface()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (IsComment(vLine) || !MapCall.IsMatch(vLine))
                {
                    continue;
                }

                if (MachineIngestWords.Any(aWord => vLine.Contains(aWord, StringComparison.OrdinalIgnoreCase)))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLine.Trim()}");
                }
            }
        }

        Assert.True(
            vFindings.Count == 0,
            "REQ-NFR-014 — no machine-to-machine ingest endpoint may exist. Found:\n  "
            + string.Join("\n  ", vFindings));
    }

    /// <summary>
    /// No uploaded content is ever rendered as HTML.
    /// </summary>
    /// <remarks>
    /// The import endpoints answer JSON built from counts, stream names, hashes and TfLens's own
    /// sentences. A raw-HTML sink anywhere in the import area would be the one way an uploaded byte
    /// could reach a browser as markup, so the area is checked for one.
    /// </remarks>
    [Fact]
    public void NothingInTheImportPathRendersRawHtml()
    {
        var vFindings = new List<string>();

        var vSinks = new[] { "MarkupString", "Results.Content(", "Results.Text(", "@((MarkupString)" };

        var vFiles = RepoTree.Files("*.cs", Path.Combine("src", "TfLens", "Services", "Import"))
            .Concat(RepoTree.Files("*.cs", Path.Combine("src", "TfLens.Core", "Import")));

        foreach (var vPath in vFiles)
        {
            var vText = File.ReadAllText(vPath);

            foreach (var vSink in vSinks.Where(aS => vText.Contains(aS, StringComparison.Ordinal)))
            {
                vFindings.Add($"{RepoTree.Relative(vPath)} — {vSink}");
            }
        }

        Assert.True(
            vFindings.Count == 0,
            "REQ-NFR-014 — no uploaded content may be rendered as HTML. Found:\n  "
            + string.Join("\n  ", vFindings));
    }

    /// <summary>
    /// Nothing uploaded is executed.
    /// </summary>
    /// <remarks>
    /// The import area starts no process, loads no assembly and evaluates nothing. This is the check
    /// that keeps that true as the area grows.
    /// </remarks>
    [Fact]
    public void NothingInTheImportPathExecutesAnything()
    {
        var vFindings = new List<string>();

        var vExecutors = new[]
        {
            "Process.Start", "ProcessStartInfo", "Assembly.Load", "Assembly.LoadFrom",
            "AppDomain.CurrentDomain.Load", "CSharpScript"
        };

        var vFiles = RepoTree.Files("*.cs", Path.Combine("src", "TfLens", "Services", "Import"))
            .Concat(RepoTree.Files("*.cs", Path.Combine("src", "TfLens.Core", "Import")));

        foreach (var vPath in vFiles)
        {
            var vText = File.ReadAllText(vPath);

            foreach (var vExecutor in vExecutors.Where(aE => vText.Contains(aE, StringComparison.Ordinal)))
            {
                vFindings.Add($"{RepoTree.Relative(vPath)} — {vExecutor}");
            }
        }

        Assert.True(
            vFindings.Count == 0,
            "REQ-NFR-014 — nothing uploaded may be executed. Found:\n  " + string.Join("\n  ", vFindings));
    }

    /// <summary>The path to the import endpoint file every check above reads.</summary>
    private static string ImportEndpointsPath => Path.Combine(
        RepoTree.Root.FullName, "src", "TfLens", "Services", "Import", "ImportEndpoints.cs");

    /// <summary>
    /// Reads the closed anonymous-route list out of the file BRD-2 makes its register.
    /// </summary>
    /// <returns>Every route literal on the list, plus the framework prefixes.</returns>
    private static IReadOnlyCollection<string> AnonymousRouteLiterals()
    {
        var vPath = Path.Combine(
            RepoTree.Root.FullName, "src", "TfLens", "Services", "Auth", "AnonymousRoutes.cs");

        var vText = File.ReadAllText(vPath);

        return Regex.Matches(vText, "\"(?<Route>/[^\"]*)\"")
            .Select(aM => aM.Groups["Route"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Tests whether a line is a comment and therefore not a mapping.</summary>
    /// <param name="aLine">The source line.</param>
    /// <returns><c>true</c> when the line begins a comment.</returns>
    private static bool IsComment(string aLine) =>
        aLine.TrimStart().StartsWith("//", StringComparison.Ordinal);
}
