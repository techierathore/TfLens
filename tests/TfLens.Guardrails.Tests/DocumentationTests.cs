using System.Text;
using System.Text.RegularExpressions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-FN-042 (BRD-79) — the README states the BRD §3 out-of-scope list <b>verbatim</b> and documents
/// the run / rebuild / sync / export commands; REQ-FN-043 (BRD-80) — <c>DECISIONS.md</c> exists from
/// the first commit that ships code and covers all five required categories.
/// </summary>
/// <remarks>
/// "Verbatim" is exactly the kind of claim that rots the moment the BRD is amended, so it is checked
/// against the BRD rather than trusted. The bullets are compared word for word.
/// </remarks>
public sealed class DocumentationTests
{
    private static string ReadmePath => Path.Combine(RepoTree.Root.FullName, "README.md");
    private static string DecisionsPath => Path.Combine(RepoTree.Root.FullName, "DECISIONS.md");
    private static string BrdPath => Path.Combine(RepoTree.Root.FullName, "docs", "TfLens-BRD.md");

    /// <summary>The README reproduces every BRD §3 out-of-scope bullet, word for word.</summary>
    [Fact]
    public void ReadmeCarriesTheOutOfScopeListVerbatim()
    {
        var vExpected = OutOfScopeBulletsFromBrd();

        Assert.True(vExpected.Count >= 5, $"Only {vExpected.Count} out-of-scope bullets parsed from the BRD.");

        var vReadme = File.ReadAllText(ReadmePath);
        var vMissing = new StringBuilder();

        foreach (var vBullet in vExpected)
        {
            if (!vReadme.Contains(vBullet, StringComparison.Ordinal))
            {
                vMissing.AppendLine($"  - {vBullet}");
            }
        }

        Assert.True(
            vMissing.Length == 0,
            $"REQ-FN-042 — README.md does not carry these BRD §3 out-of-scope bullets verbatim " +
            $"(no paraphrasing, no re-wrapping):{Environment.NewLine}{vMissing}");
    }

    /// <summary>The README documents all four invocations with their real command lines.</summary>
    [Fact]
    public void ReadmeDocumentsEveryCommandVerb()
    {
        var vReadme = File.ReadAllText(ReadmePath);
        var vMissing = new StringBuilder();

        foreach (var vNeedle in new[]
                 {
                     "docker compose up",
                     "dotnet run --project src/TfLens",
                     "dotnet TfLens.dll sync",
                     "dotnet TfLens.dll rebuild",
                     "dotnet TfLens.dll export",
                     "docker exec tflens"
                 })
        {
            if (!vReadme.Contains(vNeedle, StringComparison.Ordinal))
            {
                vMissing.AppendLine($"  {vNeedle}");
            }
        }

        Assert.True(
            vMissing.Length == 0,
            $"REQ-FN-042 — README.md is missing these invocations:{Environment.NewLine}{vMissing}");
    }

    /// <summary>The README documents every environment variable and the parity procedure.</summary>
    [Fact]
    public void ReadmeDocumentsConfigurationAndParity()
    {
        var vReadme = File.ReadAllText(ReadmePath);
        var vMissing = new StringBuilder();

        foreach (var vNeedle in new[]
                 {
                     "TfLensDbConnection", "TfLensAppManagerApiKey", "TfLensAppManagerApiSecret",
                     "TfLensGitHubToken", "TfLensAppManagerBaseUrl", "TfLensDataRoot",
                     "TfLensPollIntervalMinutes",
                     "tf-metrics.sh", "tools/parity-compare.py", "/healthz"
                 })
        {
            if (!vReadme.Contains(vNeedle, StringComparison.Ordinal))
            {
                vMissing.AppendLine($"  {vNeedle}");
            }
        }

        Assert.True(
            vMissing.Length == 0,
            $"REQ-FN-042 — README.md is missing:{Environment.NewLine}{vMissing}");
    }

    /// <summary><c>DECISIONS.md</c> covers all five categories BRD-80 names.</summary>
    [Fact]
    public void DecisionsCoversEveryRequiredCategory()
    {
        Assert.True(File.Exists(DecisionsPath), "REQ-FN-043 — DECISIONS.md does not exist.");

        var vDecisions = File.ReadAllText(DecisionsPath);
        var vMissing = new StringBuilder();

        var vCategories = new (string Category, string[] Evidence)[]
        {
            ("storage choice", ["PostgreSQL", "Dapper", "SQLite"]),
            ("dedupe keys", ["Dedupe keys", "UcCommitUserRepoSha", "UcSessionUserRepoId", "UcRunIdentity", "UcGateIdentity"]),
            ("parser version scheme", ["Parser version", "ParserVersion.Current"]),
            ("cut for the timebox", ["Cut for the timebox"]),
            ("parity runs", ["Parity runs", "parity-last.json", "tf-metrics.sh"])
        };

        foreach (var (vCategory, vEvidence) in vCategories)
        {
            foreach (var vNeedle in vEvidence)
            {
                if (!vDecisions.Contains(vNeedle, StringComparison.OrdinalIgnoreCase))
                {
                    vMissing.AppendLine($"  {vCategory}: expected '{vNeedle}'");
                }
            }
        }

        Assert.True(
            vMissing.Length == 0,
            $"REQ-FN-043 — DECISIONS.md is missing:{Environment.NewLine}{vMissing}");
    }

    /// <summary>
    /// <c>DECISIONS.md</c> keeps the append points the other clusters were told to write into.
    /// </summary>
    /// <remarks>
    /// The parity record (REQ-FN-063) and the Playbook schema discovery (REQ-FN-068) are appended by
    /// other work. If a rewrite removes their sections, those requirements lose their home silently.
    /// </remarks>
    [Fact]
    public void DecisionsKeepsItsAppendPoints()
    {
        var vDecisions = File.ReadAllText(DecisionsPath);

        Assert.Contains("§6 Parity runs", vDecisions, StringComparison.Ordinal);
        Assert.Contains("§7 Playbook schema discovery", vDecisions, StringComparison.Ordinal);
        Assert.Contains("APPEND ONE ENTRY PER PASSING RUN", vDecisions, StringComparison.Ordinal);
        Assert.Contains("APPEND ONE ENTRY PER events.ndjson", vDecisions, StringComparison.Ordinal);
    }

    /// <summary>Reads the BRD §3 out-of-scope bullets.</summary>
    /// <returns>The bullet text, without the leading marker.</returns>
    /// <exception cref="InvalidOperationException">The out-of-scope heading is not in the BRD.</exception>
    private static IReadOnlyList<string> OutOfScopeBulletsFromBrd()
    {
        var vLines = File.ReadAllLines(BrdPath);
        var vStart = Array.FindIndex(vLines, aLine =>
            Regex.IsMatch(aLine, @"^\*\*Out of scope", RegexOptions.IgnoreCase));

        if (vStart < 0)
        {
            throw new InvalidOperationException("The BRD has no '**Out of scope' heading in §3.");
        }

        var vBullets = new List<string>();

        for (var vIndex = vStart + 1; vIndex < vLines.Length; vIndex++)
        {
            var vLine = vLines[vIndex];

            if (vLine.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (vLine.StartsWith("- ", StringComparison.Ordinal))
            {
                vBullets.Add(vLine[2..].TrimEnd());
            }
        }

        return vBullets;
    }
}
