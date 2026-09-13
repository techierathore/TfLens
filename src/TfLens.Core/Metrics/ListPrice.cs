using System.Globalization;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// What a run's measured tokens would cost at the published rate — a <b>price, never a bill</b>
/// (SCHEMA.md §2.5b, REQ-FN-136).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Tokens are the unit that works everywhere, and they are what every
/// figure here is built from — but tokens are hard to talk about. A list price is the only figure that
/// lets a subscription phase be compared with a metered one, because it asks the same question of both:
/// what would these tokens have cost, bought at the published rate?
/// </para>
/// <para>
/// <b>It is not money that was spent.</b> It is a rate card applied to a measurement, so it is kept on
/// its own line, apart from <c>money_usd</c> (what a metered provider actually billed) and from
/// <c>plan_allowance_usd</c> (what a monthly plan's allowance was consumed by). Nothing priced is ever
/// written to a record: the framework stores what it measured, and pricing happens at read time. That is
/// deliberate — when a rate changes, every run ever recorded re-prices and no record was ever wrong.
/// </para>
/// <para>
/// <b>Two things it does not do.</b> It uses the base rate, so a long-context surcharge is never added;
/// and a model the card has no rate for is <b>left out rather than counted as free</b>, with the count
/// of those published beside the total (<c>list_usd_unpriced_n</c>).
/// </para>
/// </remarks>
public static class ListPrice
{
    /// <summary>Tokens the per-million rates are quoted against.</summary>
    private const double TokensPerMillion = 1_000_000d;

    /// <summary>Decimal places a list price is rounded to, matching the reference.</summary>
    private const int PriceDigits = 6;

    /// <summary>
    /// Decimal places the exact expansion is taken to before rounding.
    /// </summary>
    /// <remarks>
    /// Twenty, which is comfortably past the point where a double's true value stops mattering at six
    /// places and comfortably inside what <c>decimal</c> can hold for any figure a token bill produces.
    /// </remarks>
    private const int ExpansionDigits = 20;

    /// <summary>
    /// Prices one run's measured tokens, or <c>null</c> when nothing prices its model.
    /// </summary>
    /// <remarks>
    /// Output tokens are exact per model when the record carries <c>model_tokens_out</c>. Input and
    /// cache tokens are counted for the window as a whole, so where a window ran several models they are
    /// shared out by each model's output share — that is arithmetic rather than measurement, and it is
    /// stated here rather than hidden.
    /// </remarks>
    /// <param name="aRun">The run.</param>
    /// <param name="aPrices">The rate card.</param>
    /// <returns>The price in USD, or <c>null</c> when no model on the run could be priced.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public static double? For(RunRecord aRun, RateCard aPrices)
    {
        ArgumentNullException.ThrowIfNull(aRun);

        return Of(
            SplitOf(aRun.ModelTokensOut, aRun.Model, aRun.TokensOut),
            aRun.TokensIn,
            aRun.TokensCacheRead,
            aRun.TokensCacheWrite,
            aPrices);
    }

    /// <summary>
    /// Prices one repair's measured tokens, on the same terms as a run's.
    /// </summary>
    /// <remarks>
    /// This is the only money-shaped figure a Claude Code repair can have: a flat monthly subscription
    /// bills nothing per token, so its <c>cost_usd</c> is a true zero and says nothing about what the
    /// work was worth. The list price asks the answerable question instead.
    /// </remarks>
    /// <param name="aFix">The repair record.</param>
    /// <param name="aPrices">The rate card.</param>
    /// <returns>The price in USD, or <c>null</c> when nothing prices its model.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public static double? For(MissFixRecord aFix, RateCard aPrices)
    {
        ArgumentNullException.ThrowIfNull(aFix);

        return Of(
            SplitOf(null, aFix.Model, aFix.TokensOut),
            aFix.TokensIn,
            aFix.TokensCacheRead,
            aFix.TokensCacheWrite,
            aPrices);
    }

    /// <summary>
    /// Prices one measured token window over a per-model output split.
    /// </summary>
    /// <param name="aSplit">Model to output tokens, or <c>null</c> when nothing can be priced.</param>
    /// <param name="aTokensIn">Input tokens for the window as a whole.</param>
    /// <param name="aCacheReadTokens">Cache-read tokens for the window as a whole.</param>
    /// <param name="aCacheWriteTokens">Cache-write tokens for the window as a whole.</param>
    /// <param name="aPrices">The rate card.</param>
    /// <returns>The price in USD, or <c>null</c> when no model could be priced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aPrices"/> is <c>null</c>.</exception>
    private static double? Of(
        IReadOnlyDictionary<string, long>? aSplit,
        int? aTokensIn,
        int? aCacheReadTokens,
        int? aCacheWriteTokens,
        RateCard aPrices)
    {
        ArgumentNullException.ThrowIfNull(aPrices);

        var vSplit = aSplit;

        if (vSplit is null)
        {
            return null;
        }

        // Binary floating point, deliberately, and this is the one place in the product where that is
        // the right choice. The figure has to agree with the reference to the last published digit, and
        // the reference computes it in float64; doing it in decimal here produced answers that were more
        // exact and therefore WRONG against the gate, differing by one unit in the sixth place on about
        // a third of the phases. The result is rounded to six places immediately and carried as decimal
        // everywhere else, so nothing downstream inherits the float.
        var vTotalOut = vSplit.Values.Sum();
        var vIn = (double)(aTokensIn ?? 0);
        var vCacheRead = (double)(aCacheReadTokens ?? 0);
        var vCacheWrite = (double)(aCacheWriteTokens ?? 0);

        var vUsd = 0d;
        var vPricedModels = 0;

        foreach (var vEntry in vSplit)
        {
            var vRate = aPrices.Find(vEntry.Key);

            if (vRate is null)
            {
                continue;
            }

            var vShare = vTotalOut == 0 ? 0d : (double)vEntry.Value / vTotalOut;

            vUsd += ((vIn * vShare * (double)vRate.InputPerMillion)
                     + (vEntry.Value * (double)vRate.OutputPerMillion)
                     + (vCacheRead * vShare * (double)vRate.CacheReadPerMillion)
                     + (vCacheWrite * vShare * (double)vRate.CacheWritePerMillion))
                    / TokensPerMillion;

            vPricedModels++;
        }

        return vPricedModels == 0 ? null : (double?)Round(vUsd, PriceDigits);
    }

    /// <summary>
    /// Rounds a price to a number of decimal places <b>the way the reference does</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Math.Round(double, digits)</c> cannot be used here. It scales the value before comparing, so a
    /// number whose true binary value sits just above a decimal midpoint can look like an exact tie and
    /// be rounded to even — down — where the reference rounds it up. That is not hypothetical: a
    /// <c>triage-issues</c> run prices at <c>15.8516545</c>, whose nearest double is fractionally ABOVE
    /// that midpoint, and the two implementations published 15.851654 and 15.851655 for the same run.
    /// </para>
    /// <para>
    /// Formatting with <c>F20</c> is what settles it. <c>G17</c> is <b>not</b> enough: it round-trips
    /// the double but stops at seventeen significant digits, which renders that same run as exactly
    /// <c>15.85165450000000000</c> — a tie that then rounds to even and back to the wrong answer. The
    /// true value is <c>15.85165450000000042508</c>, and only a longer expansion shows it. A genuine tie
    /// still rounds to even, which is what the reference does too.
    /// </para>
    /// </remarks>
    /// <param name="aValue">The price.</param>
    /// <param name="aDigits">Decimal places to keep.</param>
    /// <returns>The rounded price.</returns>
    public static double Round(double aValue, int aDigits)
    {
        try
        {
            var vExact = decimal.Parse(
                aValue.ToString("F" + ExpansionDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);

            return (double)Math.Round(vExact, aDigits, MidpointRounding.ToEven);
        }
        catch (OverflowException)
        {
            // A price too large for `decimal` to hold at this expansion. Nothing in a token bill comes
            // near it, and falling back keeps the method total rather than throwing inside a report.
            return Math.Round(aValue, aDigits, MidpointRounding.ToEven);
        }
    }

    /// <summary>
    /// The per-model output split this run should be priced over.
    /// </summary>
    /// <remarks>
    /// The recorded split is preferred whenever the record carries one, because a window that spent 90%
    /// of its output on one model and 10% on another is a different fact about cost from an even split,
    /// and the <c>model</c> label cannot tell them apart. Only where no split exists does the dominant
    /// label stand in, and then only for a run that measured its output at all.
    /// </remarks>
    /// <param name="aModelTokensOut">The recorded per-model split, when the record carries one.</param>
    /// <param name="aModel">The dominant model label, used only where no split exists.</param>
    /// <param name="aTokensOut">Output tokens the window measured.</param>
    /// <returns>Model to output tokens, or <c>null</c> when the record cannot be priced.</returns>
    private static IReadOnlyDictionary<string, long>? SplitOf(
        IReadOnlyDictionary<string, long>? aModelTokensOut,
        string? aModel,
        int? aTokensOut)
    {
        if (aModelTokensOut is { Count: > 0 })
        {
            return aModelTokensOut;
        }

        return string.IsNullOrWhiteSpace(aModel) || aTokensOut is null
            ? null
            : new Dictionary<string, long>(StringComparer.Ordinal) { [aModel] = aTokensOut.Value };
    }
}
