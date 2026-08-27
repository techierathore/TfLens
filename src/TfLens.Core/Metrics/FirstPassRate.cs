using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of the reference's <c>first_pass_rate</c> field.
/// </summary>
/// <remarks>
/// <c>pct(len(first_pass), len(reqs)) if len(reqs) &gt;= MIN_N else "insufficient data (n=…)"</c>.
/// The denominator is distinct eligible REQ IDs — on a live segment, eligible excludes every REQ in
/// the taint set (REQ-FN-049) — and the numerator is the subset that was <c>Verified</c> on
/// <c>attempt == 1</c>.
/// </remarks>
public static class FirstPassRate
{
    /// <summary>
    /// Computes a first-pass rate, or refuses to.
    /// </summary>
    /// <param name="aFirstPassCount">Distinct REQs verified on their first attempt.</param>
    /// <param name="aReqsScored">Distinct eligible REQs — the denominator.</param>
    /// <returns>The percentage as a <see cref="Figure"/>, or <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> REQs.</returns>
    public static Figure Compute(int aFirstPassCount, int aReqsScored) =>
        aReqsScored < MetricsConstants.MinN
            ? Figure.InsufficientData(aReqsScored)
            : Figure.Value(
                100.0 * aFirstPassCount / aReqsScored,
                aReqsScored,
                MetricsConstants.Pct(aFirstPassCount, aReqsScored));
}
