namespace TfLens.Core.Metrics;

/// <summary>
/// One model's line on the operator-editable rate card — a PRICE, never a MEASUREMENT.
/// </summary>
/// <remarks>
/// SCHEMA.md §4 and ADR-009: <c>cost_usd</c> is measured only where a harness reports it (OpenCode).
/// Everything computed from this type is <b>tokens × rate card</b>, and every figure derived from it is
/// labelled <see cref="RateCard.EstimateLabel"/> in the UI, in <c>snapshot.md</c> and in
/// <c>tflens.json</c>. There is deliberately no member on this type that returns "spend"; the one
/// calculation it offers is named <see cref="EstimateUsd"/> so a caller cannot forget what it is.
/// </remarks>
/// <param name="InputPerMillion">USD per 1,000,000 input tokens.</param>
/// <param name="OutputPerMillion">USD per 1,000,000 output tokens.</param>
/// <param name="CacheReadPerMillion">USD per 1,000,000 cache-read tokens.</param>
/// <param name="CacheWritePerMillion">USD per 1,000,000 cache-write (cache-creation) tokens.</param>
public sealed record ModelRate(
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal CacheReadPerMillion,
    decimal CacheWritePerMillion)
{
    /// <summary>Tokens the per-million rates are quoted against.</summary>
    private const decimal TokensPerMillion = 1_000_000m;

    /// <summary>
    /// Prices a token mix at this rate card line, to full precision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result is an <b>estimate</b>. It is what the tokens would have cost at the rates an operator
    /// typed into <c>data/prices.json</c>; it is not what anybody was billed, and it must never be
    /// rendered or exported without <see cref="RateCard.EstimateLabel"/> beside it.
    /// </para>
    /// <para>
    /// The value is deliberately <b>not</b> rounded. Rounding here and then summing across runs
    /// accumulates a per-run error — on the fixture set it inflated the actual-mix estimate from
    /// $5.08 to $5.10 — and, worse, made the two repricing figures incomparable, because the
    /// counterfactual prices one pooled token total (one rounding) while the actual mix summed seven
    /// separately-rounded runs. The delta between them is the headline "what routing saved" number, so
    /// that asymmetry corrupted the figure the page exists to show. Callers round **once**, at the
    /// point the figure is presented.
    /// </para>
    /// </remarks>
    /// <param name="aTokensIn">Input tokens.</param>
    /// <param name="aTokensOut">Output tokens.</param>
    /// <param name="aTokensCacheRead">Cache-read tokens.</param>
    /// <param name="aTokensCacheWrite">Cache-write tokens.</param>
    /// <returns>The estimated USD figure, unrounded.</returns>
    public decimal EstimateUsd(long aTokensIn, long aTokensOut, long aTokensCacheRead, long aTokensCacheWrite) =>
        (aTokensIn * InputPerMillion
         + aTokensOut * OutputPerMillion
         + aTokensCacheRead * CacheReadPerMillion
         + aTokensCacheWrite * CacheWritePerMillion) / TokensPerMillion;
}
