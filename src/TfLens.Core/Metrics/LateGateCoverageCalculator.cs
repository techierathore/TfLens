using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of the reference's <c>late_gate_coverage</c> block.
/// </summary>
/// <remarks>
/// A gate that entered the enum mid-stream is structurally understated in a raw distribution, so its
/// share there is never its catch rate (SCHEMA.md §3.5, REQ-FN-052). This reports <c>ran</c> — records
/// whose <c>gates_run</c> contains the gate — beside <c>caught</c>, and derives the rate from
/// <c>ran</c> alone. There is no code path here that divides <c>caught</c> by the distribution total.
/// </remarks>
public static class LateGateCoverageCalculator
{
    /// <summary>
    /// Computes coverage for every gate in <see cref="MetricsConstants.LateGates"/>.
    /// </summary>
    /// <param name="aSegmentRecords">Every gate record in the segment — not only the failures.</param>
    /// <param name="aFailureCounts">The gate distribution counts from <see cref="GateDistribution.Count"/>.</param>
    /// <returns>One entry per late gate, in the table's declared order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aSegmentRecords"/> or <paramref name="aFailureCounts"/> is <c>null</c>.</exception>
    public static IReadOnlyList<LateGateCoverage> Compute(
        IReadOnlyList<GateRecord> aSegmentRecords,
        IReadOnlyDictionary<string, int> aFailureCounts)
    {
        ArgumentNullException.ThrowIfNull(aSegmentRecords);
        ArgumentNullException.ThrowIfNull(aFailureCounts);

        var vCoverage = new List<LateGateCoverage>();
        foreach (var vLateGate in MetricsConstants.LateGates)
        {
            var vRan = aSegmentRecords.Count(aRecord => GatesRun.Contains(aRecord, vLateGate.Key));
            var vCaught = aFailureCounts.GetValueOrDefault(vLateGate.Key);
            vCoverage.Add(new LateGateCoverage(
                vLateGate.Key,
                vRan,
                vCaught,
                vLateGate.Value,
                CatchRate(vRan, vCaught)));
        }

        return vCoverage;
    }

    /// <summary>
    /// The catch rate a late gate has earned the right to state.
    /// </summary>
    /// <param name="aRan">Records whose <c>gates_run</c> contains the gate.</param>
    /// <param name="aCaught">Failures the gate caught.</param>
    /// <returns><see cref="FigureKind.NotApplicable"/> when the gate has not run at all, <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> runs, else the rate.</returns>
    private static Figure CatchRate(int aRan, int aCaught)
    {
        if (aRan == 0 && aCaught == 0)
        {
            return Figure.NotApplicable();
        }

        return aRan < MetricsConstants.MinN
            ? Figure.InsufficientData(aRan)
            : Figure.Value(100.0 * aCaught / aRan, aRan, MetricsConstants.Pct(aCaught, aRan));
    }
}
