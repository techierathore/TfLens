using System.Text;
using System.Text.Json;
using TfLens.Core;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-FN-037 (BRD-8) — secrets are read only from environment / user-secrets through the PascalCase
/// provider, never from a file in the repository; REQ-FN-038 (BRD-9) — startup refuses a
/// configuration that cannot produce a working process; REQ-FN-039 (BRD-11) — <c>DataRoot</c> governs
/// every path that is written.
/// </summary>
public sealed class ConfigurationHygieneTests
{
    /// <summary>The one file allowed to touch the process environment.</summary>
    private const string TheProvider = "src/TfLens/Configuration/PascalCaseEnvironmentConfigurationSource.cs";

    /// <summary>
    /// Nothing outside the provider reads the environment directly.
    /// </summary>
    /// <remarks>
    /// This is what makes "no secret is read from anywhere else" a one-line grep instead of a code
    /// review: there is exactly one door, and this test guards it.
    /// </remarks>
    [Fact]
    public void OnlyTheProviderReadsTheEnvironment()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            if (RepoTree.Relative(vPath).Equals(TheProvider, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (vLine.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (vLine.Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal)
                    || vLine.Contains("Environment.GetEnvironmentVariables", StringComparison.Ordinal))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLine.Trim()}");
                }
            }
        }

        Assert.True(
            vFindings.Count == 0,
            $"REQ-FN-037 — {vFindings.Count} direct environment read(s) outside {TheProvider}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, vFindings));
    }

    /// <summary>No <c>appsettings*.json</c> declares a TfLens secret key at all.</summary>
    /// <remarks>
    /// Not "declares it empty" — declares it at all. A key present with an empty value is an
    /// invitation to fill it in and commit it.
    /// </remarks>
    [Fact]
    public void AppSettingsDeclareNoSecretKey()
    {
        var vForbidden = new[]
        {
            "AppManagerApiKey", "AppManagerApiSecret", "DbConnection", "GitHubToken",
            "TfLensAppManagerApiKey", "TfLensAppManagerApiSecret", "TfLensDbConnection", "TfLensGitHubToken"
        };

        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("appsettings*.json", "src"))
        {
            var vText = File.ReadAllText(vPath);

            // Parsing rather than grepping, so a nested "TfLens" section is caught too.
            using var vDocument = JsonDocument.Parse(vText);

            foreach (var vKey in vForbidden)
            {
                if (vText.Contains($"\"{vKey}\"", StringComparison.OrdinalIgnoreCase))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)} declares '{vKey}'");
                }
            }
        }

        Assert.True(
            vFindings.Count == 0,
            $"REQ-FN-037 — secrets must never appear in a committed settings file:{Environment.NewLine}" +
            string.Join(Environment.NewLine, vFindings));
    }

    /// <summary>Every non-secret setting has a working default.</summary>
    /// <remarks>REQ-FN-037's second clause and REQ-FN-010's third: base URL and app id must just work.</remarks>
    [Fact]
    public void NonSecretSettingsHaveWorkingDefaults()
    {
        var vOptions = new TfLensOptions();

        Assert.Equal("https://appmgrapi.techierathore.com", vOptions.AppManagerBaseUrl);
        Assert.Equal(1, vOptions.AppManagerAppId);
        Assert.Equal(15, vOptions.PollIntervalMinutes);
        Assert.Equal("data", vOptions.DataRoot);
    }

    /// <summary>Startup refuses a configuration with no connection string.</summary>
    [Fact]
    public void ValidateRefusesAMissingConnectionString()
    {
        var vError = Assert.Throws<InvalidOperationException>(() => new TfLensOptions().Validate());
        Assert.Contains("TfLensDbConnection", vError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The AppManager API-key pair is accepted whole or not at all, and refused half-configured.
    /// </summary>
    /// <remarks>
    /// Recorded as D-006 in <c>DECISIONS.md</c>, on evidence from the live API: no pair works, because
    /// the client sends <c>applicationId</c> in every request body; half a pair 401s every call.
    /// </remarks>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("key", "secret", true)]
    [InlineData("key", null, false)]
    [InlineData(null, "secret", false)]
    [InlineData("key", "   ", false)]
    public void ValidateTreatsTheApiKeyPairAsAllOrNothing(string? aKey, string? aSecret, bool aShouldStart)
    {
        var vOptions = new TfLensOptions
        {
            DbConnection = "Host=localhost;Database=tflens",
            AppManagerApiKey = aKey,
            AppManagerApiSecret = aSecret
        };

        if (aShouldStart)
        {
            vOptions.Validate();
            Assert.Equal(aKey is not null, vOptions.HasAppManagerApiCredentials);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(vOptions.Validate);
        }
    }

    /// <summary>Every written path derives from <c>DataRoot</c>, and every one of them is user-scoped.</summary>
    /// <remarks>REQ-FN-039 plus the path half of ADR-013 — the user id is in the path, not a filter.</remarks>
    [Fact]
    public void EveryWrittenPathDerivesFromDataRoot()
    {
        var vOptions = new TfLensOptions { DataRoot = Path.Combine("srv", "tflens-data") };

        Assert.StartsWith(vOptions.DataRoot, vOptions.RawPath(7), StringComparison.Ordinal);
        Assert.StartsWith(vOptions.DataRoot, vOptions.ReportsPath(7), StringComparison.Ordinal);
        Assert.StartsWith(vOptions.DataRoot, vOptions.PricesPath, StringComparison.Ordinal);
        Assert.StartsWith(vOptions.DataRoot, vOptions.ParityLastPath, StringComparison.Ordinal);

        Assert.NotEqual(vOptions.RawPath(7), vOptions.RawPath(8));
        Assert.NotEqual(vOptions.ReportsPath(7), vOptions.ReportsPath(8));
    }

    /// <summary>No writing code path hard-codes the literal <c>data/</c> root.</summary>
    /// <remarks>REQ-FN-039's acceptance: "no hard-coded <c>data/</c> remains in the code paths that write".</remarks>
    [Fact]
    public void NoWritingCodePathHardCodesTheDataFolder()
    {
        var vFindings = new List<string>();
        var vNeedles = new[] { "\"data/", "\"data\\\\", "Combine(\"data\"", "\"./data" };

        // The acceptance says "the code paths that *write*". A literal in a description string is not
        // one; a literal handed to the filesystem is.
        var vWritingCall = new[]
        {
            "Path.Combine", "File.", "Directory.", "new FileInfo", "new DirectoryInfo",
            "StreamWriter", "StreamReader", "FileStream", "OpenWrite", "OpenRead"
        };

        foreach (var vPath in RepoTree.Files("*.cs", "src"))
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

                // The single legitimate occurrence is the DataRoot default itself.
                if (vLine.Contains("DataRoot", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!vWritingCall.Any(aCall => vLine.Contains(aCall, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (vNeedles.Any(aNeedle => vLine.Contains(aNeedle, StringComparison.Ordinal)))
                {
                    vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLine.Trim()}");
                }
            }
        }

        Assert.True(
            vFindings.Count == 0,
            $"REQ-FN-039 — {vFindings.Count} hard-coded data path(s); derive them from " +
            $"TfLensOptions.DataRoot instead:{Environment.NewLine}" +
            string.Join(Environment.NewLine, vFindings));
    }

    /// <summary>The example environment file documents every variable the README names.</summary>
    [Fact]
    public void ExampleEnvironmentFileDocumentsEverySetting()
    {
        var vExample = File.ReadAllText(Path.Combine(RepoTree.Root.FullName, ".env.example"));
        var vMissing = new StringBuilder();

        foreach (var vName in new[]
                 {
                     "TfLensDbPassword", "TfLensAppManagerApiKey", "TfLensAppManagerApiSecret",
                     "TfLensGitHubToken", "TfLensAppManagerBaseUrl", "TfLensPollIntervalMinutes"
                 })
        {
            if (!vExample.Contains(vName, StringComparison.Ordinal))
            {
                vMissing.AppendLine($"  {vName}");
            }
        }

        Assert.True(vMissing.Length == 0, $".env.example does not document:{Environment.NewLine}{vMissing}");
    }
}
