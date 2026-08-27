using System.Text;
using System.Text.RegularExpressions;
using TfLens.Core;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-003 (BRD-10) — the AppManager secret, the database connection string, the GitHub PAT and
/// every AppManager token are never logged, displayed or exported.
/// </summary>
/// <remarks>
/// This is a static proof rather than a behavioural one. "No log line can carry a token" is a
/// statement about every code path, including the ones a test never reaches, so it is checked against
/// the source. A line that genuinely needs an exception carries a trailing <c>// NFR-003-OK: reason</c>
/// comment, which makes every deliberate exception visible in one grep.
/// </remarks>
public sealed class SecretHygieneTests
{
    /// <summary>
    /// Identifiers whose value must never reach a log, a rendered page or an export file.
    /// </summary>
    /// <remarks>
    /// Bare <c>token</c> is deliberately absent: <c>CancellationToken</c>, <c>TokenExpiresAt</c> and
    /// the antiforgery token would drown the signal. The token *kinds* that matter are named
    /// explicitly instead.
    /// </remarks>
    private static readonly Regex SecretIdentifier = new(
        @"\b\w*(?:api_?key|api_?secret|access_?token|refresh_?token|bearer_?token|reset_?token|" +
        @"github_?token|db_?connection|connection_?string|password|passwd|pwd|secret|credential)\w*\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Call shapes that emit text somewhere a human or a file can read it.</summary>
    private static readonly Regex EmittingCall = new(
        @"\b(?:Log|Logger|objLogger|_logger|vLogger)\s*\.\s*(?:Verbose|Debug|Information|Warning|Error|Fatal)\b" +
        @"|\bLog(?:Trace|Debug|Information|Warning|Error|Critical)\s*\(" +
        @"|\bConsole\s*\.\s*(?:Write|WriteLine|Error)\b",
        RegexOptions.Compiled);

    /// <summary>The documented escape hatch, so every deliberate exception is greppable.</summary>
    private const string Waiver = "NFR-003-OK";

    /// <summary>
    /// Suffixes that turn a secret-shaped name into something that is demonstrably not the value.
    /// </summary>
    /// <remarks>
    /// <c>objPasswordError</c> is a validation message; <c>PasswordStrength</c> is a meter;
    /// <c>TokenExpiresAt</c> is a timestamp. Without this list the scan drowns the real findings in
    /// names that merely contain the word — and a check nobody trusts is a check nobody reads.
    /// </remarks>
    private static readonly string[] BenignSuffixes =
    [
        "Error", "Errors", "Message", "Messages", "Label", "Hint", "Rule", "Rules", "Strength",
        "Score", "Meter", "Visible", "Shown", "Required", "Valid", "Invalid", "Mismatch",
        "Placeholder", "Text", "Caption", "Title", "Prompt", "Field", "Id", "Kind", "Type", "Name",
        "ExpiresAt", "Expiry", "Policy", "Mode", "State", "Status", "Class", "Style", "Icon", "Css",
        "Count", "Length", "Changed", "Confirm", "Confirmation", "Reset", "Sent", "Ok", "Failed",
        "Path", "Variable", "HeaderName", "Provider", "Source", "Section"
    ];

    /// <summary>No logging or console statement may carry a secret-bearing value.</summary>
    [Fact]
    public void NoLogStatementCanCarryASecret()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (vLine.Contains(Waiver, StringComparison.Ordinal) || IsComment(vLine))
                {
                    continue;
                }

                if (!EmittingCall.IsMatch(vLine))
                {
                    continue;
                }

                var vResidue = RepoTree.StripLiterals(vLine);

                foreach (Match vMatch in SecretIdentifier.Matches(vResidue))
                {
                    if (IsBenign(vMatch.Value))
                    {
                        continue;
                    }

                    vFindings.Add(
                        $"{RepoTree.Relative(vPath)}:{vIndex + 1} emits '{vMatch.Value}' — {vLine.Trim()}");
                    break;
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report("log statements that can carry a secret", vFindings));
    }

    /// <summary>No Razor markup may render a secret-bearing value.</summary>
    /// <remarks>
    /// Quoted attribute values (<c>@bind-Value="objModel.Password"</c>) are legitimate — a password
    /// box has to bind to something. What is forbidden is an unquoted render expression, which puts
    /// the value into the DOM.
    /// </remarks>
    [Fact]
    public void NoRenderedMarkupCanCarryASecret()
    {
        var vFindings = new List<string>();
        var vRenderExpression = new Regex(@"@\(?[A-Za-z_][A-Za-z0-9_.\[\]()]*", RegexOptions.Compiled);

        foreach (var vPath in RepoTree.Files("*.razor", "src"))
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (vLine.Contains(Waiver, StringComparison.Ordinal) || IsComment(vLine))
                {
                    continue;
                }

                var vResidue = RepoTree.StripLiterals(vLine);

                foreach (Match vExpression in vRenderExpression.Matches(vResidue))
                {
                    var vMatch = SecretIdentifier.Match(vExpression.Value);

                    if (vMatch.Success && !IsBenign(vMatch.Value))
                    {
                        vFindings.Add(
                            $"{RepoTree.Relative(vPath)}:{vIndex + 1} renders '{vExpression.Value}' — {vLine.Trim()}");
                    }
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report("markup that can render a secret", vFindings));
    }

    /// <summary>
    /// The types that legitimately hold a credential, each with the reason it may.
    /// </summary>
    /// <remarks>
    /// Every one of these is transport or storage that exists precisely so the value stays on the
    /// server. None of them is ever exported or rendered — which is what the reachability test below
    /// proves rather than assumes.
    /// </remarks>
    private static readonly Dictionary<string, string> CredentialCarryingTypes = new(StringComparer.Ordinal)
    {
        ["AuthResponseData"] = "the AppManager wire response; its tokens go straight into AuthSession, server-side",
        ["AuthSessionRow"] = "the server-side session row; the browser only ever holds the session id",
        ["RegisterRequest"] = "the inbound registration form; the password is RSA-encrypted before it leaves the process"
    };

    /// <summary>
    /// Nothing a page renders or an export writes can reach a credential-carrying property.
    /// </summary>
    /// <remarks>
    /// Walks the property graph outward from every type the reports and the export are built from. A
    /// secret can only be exported if some path leads to it, so following every path is the check that
    /// cannot be defeated by adding one more level of nesting.
    /// </remarks>
    [Fact]
    public void NoExportedOrRenderedTypeCanReachASecret()
    {
        var vAssembly = typeof(TfLensOptions).Assembly;

        var vRootNames = new[]
        {
            "AnalysisResult", "PerRepoFacts", "SegmentFigures", "PooledMetrics", "PlaybookAnalysis",
            "SnapshotResult", "HarnessComparison", "RoutingAnalysis", "RebuildReport", "SyncReport",
            "RepoValidation", "UserRepo", "SyncState", "UserProfile",
            "RunRecord", "GateRecord", "SessionRecord", "CommitRecord", "PbEventRecord"
        };

        var vRoots = vRootNames
            .Select(aName => vAssembly.GetExportedTypes()
                .FirstOrDefault(aType => aType.Name == aName && aType.Namespace == "TfLens.Core.Contracts"))
            .Where(aType => aType is not null)
            .Select(aType => aType!)
            .ToList();

        Assert.True(
            vRoots.Count >= 10,
            $"Only {vRoots.Count} of {vRootNames.Length} report/export root types were found — the " +
            "reachability walk would prove nothing. Update the root list.");

        var vFindings = new List<string>();
        var vSeen = new HashSet<Type>();

        foreach (var vRoot in vRoots)
        {
            Walk(vRoot, vRoot.Name, vSeen, vFindings);
        }

        Assert.True(vFindings.Count == 0, Report("export/render paths that reach a secret", vFindings));
    }

    /// <summary>The credential-carrying allowlist has not silently grown.</summary>
    [Fact]
    public void TheCredentialCarryingTypeListIsExact()
    {
        var vAssembly = typeof(TfLensOptions).Assembly;
        var vFindings = new List<string>();

        foreach (var vType in vAssembly.GetExportedTypes())
        {
            if (vType.Namespace != "TfLens.Core.Contracts")
            {
                continue;
            }

            var vCarries = vType.GetProperties().Any(aProperty =>
                SecretIdentifier.IsMatch(aProperty.Name) && !IsBenign(aProperty.Name));

            if (vCarries && !CredentialCarryingTypes.ContainsKey(vType.Name))
            {
                vFindings.Add(
                    $"{vType.Name} carries a credential property but is not on the justified list — " +
                    "add it with a reason, or remove the property");
            }
        }

        Assert.True(vFindings.Count == 0, Report("unjustified credential-carrying contract types", vFindings));
    }

    /// <summary>Walks a type's property graph looking for a reachable secret.</summary>
    /// <param name="aType">The type to walk.</param>
    /// <param name="aPath">The property path taken to reach it, for the failure message.</param>
    /// <param name="aSeen">Types already walked, so a cycle terminates.</param>
    /// <param name="aFindings">Accumulated findings.</param>
    private static void Walk(Type aType, string aPath, HashSet<Type> aSeen, List<string> aFindings)
    {
        if (!aSeen.Add(aType))
        {
            return;
        }

        foreach (var vProperty in aType.GetProperties())
        {
            var vPath = $"{aPath}.{vProperty.Name}";

            if (SecretIdentifier.IsMatch(vProperty.Name) && !IsBenign(vProperty.Name))
            {
                aFindings.Add(vPath);
                continue;
            }

            var vNext = Unwrap(vProperty.PropertyType);

            if (vNext.Namespace == "TfLens.Core.Contracts")
            {
                Walk(vNext, vPath, aSeen, aFindings);
            }
        }
    }

    /// <summary>Unwraps nullables and collections to the type actually carried.</summary>
    /// <param name="aType">A property type.</param>
    /// <returns>The element type when the property is a collection, otherwise the type itself.</returns>
    private static Type Unwrap(Type aType)
    {
        var vUnderlying = Nullable.GetUnderlyingType(aType) ?? aType;

        if (vUnderlying.IsArray)
        {
            return vUnderlying.GetElementType() ?? vUnderlying;
        }

        if (vUnderlying.IsGenericType)
        {
            var vArguments = vUnderlying.GetGenericArguments();
            return vArguments.Length > 0 ? vArguments[^1] : vUnderlying;
        }

        return vUnderlying;
    }

    /// <summary>
    /// <c>TfLensOptions</c> must not have a <c>ToString</c> that could print its own secrets.
    /// </summary>
    /// <remarks>
    /// A record would generate one automatically and print every property — including
    /// <c>DbConnection</c> — the first time anyone interpolated the options object into a message.
    /// </remarks>
    [Fact]
    public void OptionsDoNotStringifyTheirOwnSecrets()
    {
        var vOptions = new TfLensOptions
        {
            DbConnection = "Host=h;Username=u;Password=leaked-connection-string",
            AppManagerApiKey = "leaked-api-key",
            AppManagerApiSecret = "leaked-api-secret",
            GitHubToken = "leaked-github-pat"
        };

        var vRendered = vOptions.ToString() ?? string.Empty;

        Assert.DoesNotContain("leaked-", vRendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The startup failure messages name which setting is missing without printing its value.
    /// </summary>
    /// <remarks>BRD-9's acceptance: the log names the setting, never the value or the connection string.</remarks>
    [Fact]
    public void StartupValidationMessagesNameTheSettingNotItsValue()
    {
        var vMissingDb = Assert.Throws<InvalidOperationException>(() => new TfLensOptions().Validate());

        Assert.Contains("TfLensDbConnection", vMissingDb.Message, StringComparison.Ordinal);

        var vHalfPair = Assert.Throws<InvalidOperationException>(() => new TfLensOptions
        {
            DbConnection = "Host=h;Username=u;Password=super-secret-value",
            AppManagerApiKey = "half-a-pair-key-value"
        }.Validate());

        Assert.DoesNotContain("super-secret-value", vHalfPair.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("half-a-pair-key-value", vHalfPair.Message, StringComparison.Ordinal);
        Assert.Contains("TfLensAppManagerApiKey", vHalfPair.Message, StringComparison.Ordinal);
    }

    /// <summary>No committed configuration file may carry a secret value.</summary>
    /// <remarks>
    /// <c>.env.example</c> is a template: its secret keys must be present and <b>empty</b>, which is
    /// how it documents the variable without shipping a value.
    /// </remarks>
    [Fact]
    public void NoCommittedConfigurationFileCarriesASecretValue()
    {
        var vFindings = new List<string>();

        var vCandidates = RepoTree.Files("appsettings*.json", "src")
            .Concat(RepoTree.Files("*.yml"))
            .Concat(RepoTree.Files("*.yaml"))
            .Concat([Path.Combine(RepoTree.Root.FullName, ".env.example")])
            .Where(File.Exists)
            .Distinct();

        // A value made only of a placeholder, an interpolation or an empty string is documentation.
        var vPlaceholder = new Regex(
            @"^\s*(?:""""|''|\$\{[^}]*\}|<[^>]*>|change-me|changeme|your-[\w-]*|xxx+|\.\.\.|""?\$\{[^}]*\}""?)\s*,?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (var vPath in vCandidates)
        {
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (IsComment(vLine) || vLine.Contains(Waiver, StringComparison.Ordinal))
                {
                    continue;
                }

                var vSeparator = vLine.IndexOfAny([':', '=']);
                if (vSeparator < 0)
                {
                    continue;
                }

                var vKey = vLine[..vSeparator];
                var vValue = vLine[(vSeparator + 1)..].Trim();

                if (!SecretIdentifier.IsMatch(vKey))
                {
                    continue;
                }

                if (vValue.Length == 0 || vPlaceholder.IsMatch(vValue))
                {
                    continue;
                }

                // A connection string whose password half is an interpolation is a template too.
                if (vValue.Contains("${", StringComparison.Ordinal))
                {
                    continue;
                }

                vFindings.Add($"{RepoTree.Relative(vPath)}:{vIndex + 1} — {vLine.Trim()}");
            }
        }

        Assert.True(vFindings.Count == 0, Report("committed configuration carrying a secret value", vFindings));
    }

    /// <summary>
    /// Tells whether a secret-shaped identifier is demonstrably not the secret itself.
    /// </summary>
    /// <param name="aIdentifier">The matched identifier.</param>
    /// <returns><c>true</c> when the name ends in one of the benign suffixes.</returns>
    private static bool IsBenign(string aIdentifier) =>
        BenignSuffixes.Any(aSuffix => aIdentifier.EndsWith(aSuffix, StringComparison.Ordinal));

    /// <summary>Tells whether a source line is entirely a comment.</summary>
    /// <param name="aLine">One source line.</param>
    /// <returns><c>true</c> when the line starts a comment and therefore carries no value.</returns>
    private static bool IsComment(string aLine)
    {
        var vTrimmed = aLine.TrimStart();

        return vTrimmed.StartsWith("//", StringComparison.Ordinal)
            || vTrimmed.StartsWith("///", StringComparison.Ordinal)
            || vTrimmed.StartsWith("*", StringComparison.Ordinal)
            || vTrimmed.StartsWith("#", StringComparison.Ordinal)
            || vTrimmed.StartsWith("--", StringComparison.Ordinal)
            || vTrimmed.StartsWith("@*", StringComparison.Ordinal)
            || vTrimmed.StartsWith("<!--", StringComparison.Ordinal);
    }

    /// <summary>Formats a finding list into a failure message someone can act on.</summary>
    /// <param name="aWhat">What was being looked for.</param>
    /// <param name="aFindings">The findings.</param>
    /// <returns>A multi-line report.</returns>
    private static string Report(string aWhat, IReadOnlyList<string> aFindings)
    {
        var vBuilder = new StringBuilder();
        vBuilder.AppendLine($"REQ-NFR-003 — found {aFindings.Count} {aWhat}:");

        foreach (var vFinding in aFindings)
        {
            vBuilder.AppendLine($"  {vFinding}");
        }

        vBuilder.AppendLine(
            "Redact the value, or annotate the line with `// NFR-003-OK: <reason>` if it genuinely cannot leak.");

        return vBuilder.ToString();
    }
}
