namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-FN-149 (BRD-213, SCHEMA.md §6 fourth separation, ADR-034) — the <c>Estimate</c> table is read
/// only by the reconciliation, the store and the exporter, and is never joined to <c>Run</c> in SQL.
/// </summary>
/// <remarks>
/// The shape checks (no member that could hold a ratio, a difference or a blended figure) live in
/// <c>EstimateReconciliationTests.ResultTypesCannotExpressAPooledFigure</c>, where the types are. What
/// only a source scan can prove is the negative: that NO OTHER reader exists — a page, an engine or a
/// future export that reaches for the forecast table directly and sets a figure of its own beside it.
/// A new reader is a new place the two streams can meet, so the allow-list is the guard.
/// </remarks>
public sealed class FourthSeparationReaderTests
{
    /// <summary>
    /// The only files in <c>src/</c> that may call the estimate read: the interface that declares it,
    /// the store that implements it, and the reconciliation that is the one door on the figure path.
    /// </summary>
    private static readonly string[] ReaderAllowList =
    [
        "src/TfLens.Core/Abstractions/Interfaces.cs",
        "src/TfLens.Core/Storage/PostgresStore.cs",
        "src/TfLens.Core/Metrics/EstimateMetrics.cs"
    ];

    /// <summary>
    /// Nothing in <c>src/</c> outside the allow-list calls <c>ReadEstimatesAsync</c> — the Estimate
    /// table has exactly one reader on the figure path (REQ-FN-149).
    /// </summary>
    [Fact(DisplayName = "REQ-FN-149 — only EstimateMetrics, the store and the exporter read Estimate")]
    public void OnlyTheReconciliationTheStoreAndTheInterfaceReadEstimates()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            var vRelative = RepoTree.Relative(vPath);

            if (ReaderAllowList.Contains(vRelative, StringComparer.Ordinal))
            {
                continue;
            }

            var vText = File.ReadAllText(vPath);

            if (vText.Contains("ReadEstimatesAsync", StringComparison.Ordinal))
            {
                vFindings.Add(vRelative);
            }
        }

        Assert.True(
            vFindings.Count == 0,
            "BRD-213 — the Estimate table is read only by EstimateMetrics, the store and the "
            + "interface that joins them. New readers found:" + Environment.NewLine
            + string.Join(Environment.NewLine, vFindings));
    }

    /// <summary>
    /// No SQL joins <c>Estimate</c> to <c>Run</c> (or to anything else): the two streams meet in the
    /// reconciliation's C#, where the fourth separation is enforced by shape, never in a query
    /// (ADR-034).
    /// </summary>
    [Fact(DisplayName = "REQ-FN-149 — no SQL ever joins the Estimate table to a run")]
    public void NoSqlEverJoinsTheEstimateTable()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src"))
        {
            var vRelative = RepoTree.Relative(vPath);
            var vLines = File.ReadAllLines(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (vLine.Contains("JOIN", StringComparison.OrdinalIgnoreCase)
                    && vLine.Contains("Estimate", StringComparison.Ordinal))
                {
                    vFindings.Add($"{vRelative}:{vIndex + 1}");
                }
            }
        }

        Assert.True(
            vFindings.Count == 0,
            "ADR-034 — Estimate is never joined in SQL; the reconciliation pairs the two streams in "
            + "result types that cannot express a pooled figure. Findings:" + Environment.NewLine
            + string.Join(Environment.NewLine, vFindings));
    }
}
