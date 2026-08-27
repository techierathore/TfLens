using System.Text;
using System.Text.RegularExpressions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-002 (BRD-83) — cookie auth on every page (HttpOnly, Secure, SameSite=Lax); antiforgery on
/// forms; secrets only via environment; the PAT is fine-grained contents-read; <b>no inbound API</b>;
/// HTTPS terminated by the VPS proxy with <c>ForwardedHeaders</c> configured accordingly.
/// </summary>
/// <remarks>
/// Middleware order has no runtime accessor — you cannot ask a built pipeline what came before what —
/// so the ordering clauses of the acceptance are checked against <c>Program.cs</c> itself. The flag
/// values are checked at runtime in the integration project, where a real container can be asked.
/// </remarks>
public sealed class SecurityPostureTests
{
    private static string ProgramPath =>
        Path.Combine(RepoTree.Root.FullName, "src", "TfLens", "Program.cs");

    /// <summary>The auth cookie carries all three flags the acceptance names.</summary>
    [Fact]
    public void TheAuthCookieCarriesAllThreeFlags()
    {
        var vProgram = File.ReadAllText(ProgramPath);

        Assert.Contains("Cookie.HttpOnly = true", vProgram, StringComparison.Ordinal);
        Assert.Contains("Cookie.SameSite = SameSiteMode.Lax", vProgram, StringComparison.Ordinal);
        Assert.Contains("CookieSecurePolicy.Always", vProgram, StringComparison.Ordinal);
    }

    /// <summary>The session is the sliding 12 hours BRD-93 fixes, not a week.</summary>
    [Fact]
    public void TheSessionSlidesForTwelveHours()
    {
        var vProgram = File.ReadAllText(ProgramPath);

        Assert.Contains("ExpireTimeSpan = TimeSpan.FromHours(12)", vProgram, StringComparison.Ordinal);
        Assert.Contains("SlidingExpiration = true", vProgram, StringComparison.Ordinal);
    }

    /// <summary>
    /// Forwarded headers are handled before authentication, and the proxy is actually trusted.
    /// </summary>
    /// <remarks>
    /// The second half matters as much as the first. <c>UseForwardedHeaders</c> defaults to trusting
    /// only loopback, so behind a proxy in another container it silently drops the headers — every
    /// request then looks like plain HTTP and a `Secure` cookie is never issued. Clearing the known
    /// networks is what makes the middleware do anything at all here.
    /// </remarks>
    [Fact]
    public void ForwardedHeadersRunBeforeAuthenticationAndTrustTheProxy()
    {
        var vProgram = File.ReadAllText(ProgramPath);

        var vForwarded = vProgram.IndexOf("UseForwardedHeaders", StringComparison.Ordinal);
        var vAuthentication = vProgram.IndexOf("UseAuthentication", StringComparison.Ordinal);

        Assert.True(vForwarded >= 0, "UseForwardedHeaders is not registered (BRD-83).");
        Assert.True(vAuthentication >= 0, "UseAuthentication is not registered.");
        Assert.True(
            vForwarded < vAuthentication,
            "REQ-NFR-002 — UseForwardedHeaders must run before UseAuthentication, or the scheme the " +
            "cookie policy sees is the container's plain HTTP rather than the proxy's HTTPS.");

        Assert.Contains("KnownIPNetworks.Clear()", vProgram, StringComparison.Ordinal);
        Assert.Contains("KnownProxies.Clear()", vProgram, StringComparison.Ordinal);
    }

    /// <summary>Antiforgery validation runs after authentication and authorization.</summary>
    /// <remarks>
    /// The documented order for Blazor: a token has to be validated against the identity that owns it,
    /// which is not established until authentication has run.
    /// </remarks>
    [Fact]
    public void AntiforgeryRunsAfterAuthenticationAndAuthorization()
    {
        var vProgram = File.ReadAllText(ProgramPath);

        var vAntiforgery = vProgram.IndexOf("UseAntiforgery", StringComparison.Ordinal);
        var vAuthentication = vProgram.IndexOf("UseAuthentication", StringComparison.Ordinal);
        var vAuthorization = vProgram.IndexOf("UseAuthorization", StringComparison.Ordinal);

        Assert.True(vAntiforgery >= 0, "UseAntiforgery is not registered (BRD-83).");
        Assert.True(
            vAntiforgery > vAuthentication && vAntiforgery > vAuthorization,
            "REQ-NFR-002 — UseAntiforgery must be registered after UseAuthentication and UseAuthorization.");
    }

    /// <summary>Every server-rendered form posts an antiforgery token.</summary>
    /// <remarks>
    /// <c>EditForm</c> emits one for itself under static server rendering; a hand-written
    /// <c>&lt;form method="post"&gt;</c> does not, and is the shape that ships a CSRF hole.
    /// </remarks>
    [Fact]
    public void EveryHandWrittenPostFormCarriesAnAntiforgeryToken()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.razor", "src"))
        {
            var vText = File.ReadAllText(vPath);

            foreach (Match vForm in Regex.Matches(
                         vText,
                         @"<form\b[^>]*method\s*=\s*[""']post[""'][^>]*>(?<body>.*?)</form>",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (!vForm.Groups["body"].Value.Contains("AntiforgeryToken", StringComparison.OrdinalIgnoreCase))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)} — a <form method=\"post\"> with no <AntiforgeryToken />");
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report("forms posting without an antiforgery token", vFindings));
    }

    /// <summary>
    /// The app exposes no ingestion, capture or telemetry-receiving endpoint of any kind.
    /// </summary>
    /// <remarks>
    /// This is the architectural promise of the whole product — TfLens <i>reads</i> what the frameworks
    /// already publish and receives nothing. An endpoint that accepted a stream would change what
    /// TfLens is, so the test looks for the shape rather than for a specific route.
    /// </remarks>
    [Fact]
    public void TheAppExposesNoIngestionEndpoint()
    {
        var vFindings = new List<string>();

        var vIngestionWords = new[]
        {
            "ingest", "capture", "collect", "otlp", "/telemetry", "/events", "/v1/metrics",
            "/v1/traces", "/v1/logs", "webhook", "/upload"
        };

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (vLine.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    || vLine.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                var vMapsAnEndpoint = Regex.IsMatch(vLine, @"\bMap(Get|Post|Put|Patch|Delete|Methods)\s*\(");

                if (!vMapsAnEndpoint)
                {
                    continue;
                }

                if (vIngestionWords.Any(aWord => vLine.Contains(aWord, StringComparison.OrdinalIgnoreCase)))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLine.Trim()}");
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report("endpoints that look like ingestion", vFindings));
    }

    /// <summary>
    /// The GitHub client is structurally read-only.
    /// </summary>
    /// <remarks>
    /// BRD-16 as a cross-cutting security property rather than a sync-cluster detail: TfLens must be
    /// incapable of writing to a user's repository, not merely uninclined to.
    /// </remarks>
    [Fact]
    public void NothingIssuesAWriteVerbAgainstGitHub()
    {
        var vFindings = new List<string>();

        // An HTTP write, not merely a method whose name starts with a verb: `IAuthSessionStore.
        // DeleteAsync` deletes a database row and has nothing to do with GitHub.
        var vHttpWrite = new Regex(
            @"HttpMethod\s*\.\s*(?:Post|Put|Patch|Delete)" +
            @"|\b\w*[Cc]lient\s*\.\s*(?:Post|Put|Patch|Delete)(?:Async|AsJsonAsync)",
            RegexOptions.Compiled);

        foreach (var vPath in RepoTree.Files("*.cs", "src"))
        {
            var vText = File.ReadAllText(vPath);

            // Only files that actually address the GitHub API can violate this.
            if (!vText.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var vLines = vText.Split('\n');

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (vLine.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    || vLine.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (vHttpWrite.IsMatch(vLine))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLine.Trim()}");
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report("write verbs in a file that talks to GitHub", vFindings));
    }

    /// <summary>Security headers are emitted, and before anything can short-circuit the pipeline.</summary>
    [Fact]
    public void SecurityHeadersAreAddedEarlyInThePipeline()
    {
        var vProgram = File.ReadAllText(ProgramPath);

        var vHeaders = vProgram.IndexOf("UseTfLensSecurityHeaders", StringComparison.Ordinal);
        var vStaticFiles = vProgram.IndexOf("UseStaticFiles", StringComparison.Ordinal);

        Assert.True(vHeaders >= 0, "REQ-NFR-002 — the security-header middleware is not registered.");
        Assert.True(
            vStaticFiles < 0 || vHeaders < vStaticFiles,
            "REQ-NFR-002 — the security headers must be added before UseStaticFiles, or a static " +
            "response is served without them.");
    }

    /// <summary>No service other than the four AppManager services TfLens is allowed to call.</summary>
    /// <remarks>REQ-FN-008 read as a security property: an unused capability is still an available one.</remarks>
    [Fact]
    public void NoLicenceFeaturePaymentOrIssueServiceIsCalled()
    {
        var vForbidden = new[] { "LicenseSvc", "LicenceSvc", "FeatureSvc", "PaymentSvc", "IssueSvc" };
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (vLine.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    || vLine.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (vForbidden.Any(aName => vLine.Contains(aName, StringComparison.Ordinal)))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLine.Trim()}");
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report("references to a forbidden AppManager service", vFindings));
    }

    /// <summary>Formats a finding list into a failure message someone can act on.</summary>
    /// <param name="aWhat">What was being looked for.</param>
    /// <param name="aFindings">The findings.</param>
    /// <returns>A multi-line report.</returns>
    private static string Report(string aWhat, IReadOnlyList<string> aFindings)
    {
        var vBuilder = new StringBuilder();
        vBuilder.AppendLine($"REQ-NFR-002 / BRD-83 — found {aFindings.Count} {aWhat}:");

        foreach (var vFinding in aFindings)
        {
            vBuilder.AppendLine($"  {vFinding}");
        }

        return vBuilder.ToString();
    }
}
