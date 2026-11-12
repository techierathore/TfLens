namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-027 (BRD-219) — every number on the Estimates page is computed by code over the two
/// streams at request time: no model or AI call anywhere in the request path, no stored derived
/// value, no configuration key, query parameter or toggle that changes a figure, and Owner UAT time
/// is the fixed text <c>not measured</c> with no input that accepts a value for it.
/// </summary>
/// <remarks>
/// A static scan is the only way to prove the negative: a runtime test can show the figures that ARE
/// computed, never that no code path reaches for a model, a cache or a switch. The engine file, its
/// formatter and the page are the whole request path — the store reads rows and the shell renders the frame.
/// </remarks>
public sealed class EstimatesPagePurityTests
{
    /// <summary>The files that make up the request path for every figure on /estimates.</summary>
    private static readonly string[] RequestPathFiles =
    [
        "src/TfLens.Core/Metrics/EstimateMetrics.cs",
        "src/TfLens.Core/Metrics/OracleText.cs",
        "src/TfLens/Components/Pages/Estimates.razor"
    ];

    /// <summary>Tokens that mean "a model, an HTTP client or an AI SDK is reachable from here".</summary>
    private static readonly string[] ForbiddenTokens =
    [
        "HttpClient", "IHttpClientFactory", "HttpRequestMessage", "WebClient", "HttpMessageHandler",
        "OpenAi", "OpenAI", "Anthropic", "ChatCompletion", "ChatClient", "SemanticKernel",
        "TechieRag", "ILlmClient", "LanguageModel"
    ];

    /// <summary>Tokens that mean "a figure could be changed by configuration, a query or a toggle".</summary>
    private static readonly string[] SwitchTokens =
    [
        "IConfiguration", "TfLensOptions", "NavigationManager", "SupplyParameterFromQuery"
    ];

    /// <summary>Tokens that mean "something is written back or served from a stored derivation".</summary>
    private static readonly string[] WriteTokens =
    [
        "UpsertAsync", "INSERT INTO", "UPDATE \"", "WriteSyncState", "MemoryCache", "IAnalysisCache"
    ];

    /// <summary>
    /// The request path of /estimates holds no HTTP or AI client, no configuration switch and no
    /// write (REQ-NFR-027).
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-027 — the Estimates request path has no HTTP or AI client, no switch and no write")]
    public void TheEstimatesRequestPathHasNoModelCallNoSwitchAndNoWrite()
    {
        var vFindings = new List<string>();

        foreach (var vRelative in RequestPathFiles)
        {
            var vPath = Path.Combine(RepoTree.Root.FullName, vRelative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(vPath), $"{vRelative} is missing — the scan cannot prove anything.");

            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                foreach (var vToken in ForbiddenTokens.Concat(SwitchTokens).Concat(WriteTokens))
                {
                    if (vLine.Contains(vToken, StringComparison.Ordinal))
                    {
                        vFindings.Add($"{vRelative}:{vIndex + 1} — {vToken}");
                    }
                }
            }
        }

        Assert.True(
            vFindings.Count == 0,
            "BRD-219 — every figure on /estimates is code over the two streams at request time. "
            + "Findings:" + Environment.NewLine + string.Join(Environment.NewLine, vFindings));
    }

    /// <summary>
    /// Owner UAT time is the fixed text <c>not measured</c>, and nothing on the page accepts a value
    /// for it: no input, no select, no editable control of any kind (REQ-NFR-027).
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-027 — Owner UAT time is the fixed text not measured, with no input")]
    public void OwnerUatTimeIsFixedTextWithNoInput()
    {
        var vPagePath = Path.Combine(
            RepoTree.Root.FullName,
            "src", "TfLens", "Components", "Pages", "Estimates.razor");

        var vPage = File.ReadAllText(vPagePath);

        Assert.Contains("not measured", vPage, StringComparison.Ordinal);

        // The page has no form control at all — its only interactive members are the repo picker
        // (which narrows the scope, never a figure) and the two disclosures.
        foreach (var vToken in new[] { "<InputText", "<InputNumber", "<InputDate", "<InputSelect", "<Textarea", "<EditForm" })
        {
            Assert.DoesNotContain(vToken, vPage, StringComparison.Ordinal);
        }
    }
}
