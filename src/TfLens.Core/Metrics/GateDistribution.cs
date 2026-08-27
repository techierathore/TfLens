using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of the reference's <c>gate_distribution</c> block — which gate caught each failure.
/// </summary>
/// <remarks>
/// Counted over records whose verdict is not in <see cref="MetricsConstants.NonFailureVerdicts"/>; a
/// failure naming no gate lands in <see cref="MetricsConstants.Unattributed"/>, and
/// <see cref="MetricsConstants.Escaped"/> is reported as its own row rather than folded into a gate's
/// share (SCHEMA.md §3.2). Rows are emitted in <see cref="MetricsConstants.GateOrder"/>, and a gate
/// with no failures is omitted — exactly as the reference's <c>if dist.get(g)</c> does.
/// </remarks>
public static class GateDistribution
{
    /// <summary>
    /// Counts failures by gate.
    /// </summary>
    /// <param name="aFailures">The segment's failure records.</param>
    /// <returns>The per-gate counts, keyed by gate name including <see cref="MetricsConstants.Unattributed"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aFailures"/> is <c>null</c>.</exception>
    public static Dictionary<string, int> Count(IEnumerable<GateRecord> aFailures)
    {
        ArgumentNullException.ThrowIfNull(aFailures);

        var vCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var vFailure in aFailures)
        {
            var vGate = string.IsNullOrEmpty(vFailure.Gate) ? MetricsConstants.Unattributed : vFailure.Gate;
            vCounts[vGate] = vCounts.GetValueOrDefault(vGate) + 1;
        }

        return vCounts;
    }

    /// <summary>
    /// Renders the counts as report rows in the reference's gate order.
    /// </summary>
    /// <param name="aCounts">The counts from <see cref="Count"/>.</param>
    /// <param name="aTotalFailures">Total failures in the segment — the share denominator.</param>
    /// <returns>One row per gate that caught at least one failure, in <see cref="MetricsConstants.GateOrder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aCounts"/> is <c>null</c>.</exception>
    public static IReadOnlyList<GateCount> Rows(IReadOnlyDictionary<string, int> aCounts, int aTotalFailures)
    {
        ArgumentNullException.ThrowIfNull(aCounts);

        var vRows = new List<GateCount>();
        foreach (var vGate in MetricsConstants.GateOrder.Append(MetricsConstants.Unattributed))
        {
            var vCount = aCounts.GetValueOrDefault(vGate);
            if (vCount > 0)
            {
                vRows.Add(new GateCount(vGate, vCount, MetricsConstants.Pct(vCount, aTotalFailures)));
            }
        }

        return vRows;
    }

    /// <summary>
    /// The reference's <c>gate_distribution_note</c> — set only when the shares cannot be read honestly.
    /// </summary>
    /// <param name="aTotalFailures">Total failures the distribution was counted over.</param>
    /// <returns><c>insufficient data (n=…)</c> below <see cref="MetricsConstants.MinN"/> failures, otherwise <c>null</c>.</returns>
    public static string? Note(int aTotalFailures) =>
        aTotalFailures < MetricsConstants.MinN
            ? Figure.InsufficientData(aTotalFailures).Display()
            : null;
}
