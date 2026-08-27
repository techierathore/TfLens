using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of the reference's <c>escape_rate</c> field.
/// </summary>
/// <remarks>
/// REQs carrying a <c>gate == "escaped"</c> record over REQs carrying any failure record — "which
/// gate caught it: none of them". Both sides are counted over the whole segment, taint included,
/// exactly as the reference does; the taint exclusion is a first-pass-rate rule only.
/// </remarks>
public static class EscapeRate
{
    /// <summary>
    /// Computes an escape rate, or refuses to.
    /// </summary>
    /// <param name="aEscapedReqs">Distinct REQs with an <c>escaped</c> record.</param>
    /// <param name="aFailedReqs">Distinct REQs with any failure record — the denominator.</param>
    /// <returns>The percentage as a <see cref="Figure"/>, or <see cref="FigureKind.InsufficientData"/> below <see cref="MetricsConstants.MinN"/> failing REQs.</returns>
    public static Figure Compute(int aEscapedReqs, int aFailedReqs) =>
        aFailedReqs < MetricsConstants.MinN
            ? Figure.InsufficientData(aFailedReqs)
            : Figure.Value(
                100.0 * aEscapedReqs / aFailedReqs,
                aFailedReqs,
                MetricsConstants.Pct(aEscapedReqs, aFailedReqs));
}
