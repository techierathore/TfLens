using System.Globalization;
using System.Numerics;

namespace TfLens.Core.Metrics;

/// <summary>
/// The oracle's two number formatters, <c>_k</c> and <c>_hms</c> in <c>tf-metrics.sh</c>, ported
/// exactly — so a figure on a page reads character for character as the oracle prints it
/// (REQ-UI-078, BRD-214; UIDesign phase 4: "formatted as the oracle prints them").
/// </summary>
/// <remarks>
/// <para>
/// <c>_k</c> prints <c>"%.1f%s" % (n / div, unit)</c>. Python's <c>%.1f</c> rounds the EXACT binary
/// value of the double, half to even: <c>1250 / 1e3</c> is exactly 1.25 and prints <c>1.2k</c>, while
/// <c>1150 / 1e3</c> is 1.149999… and prints <c>1.1k</c>. .NET's <c>"0.0"</c> rounds a tie away from
/// zero (<c>1.3k</c>) and <c>decimal</c> rounding sees 1.15 as a tie (<c>1.2k</c>), so neither is the
/// oracle. <see cref="RoundTenthsHalfEven"/> does the oracle's arithmetic on the exact value instead.
/// </para>
/// <para>
/// <c>_hms</c> prints <c>—</c> for no time at all (<c>if not s</c>), truncates to whole seconds
/// (<c>int(s)</c>) and splits with Python's flooring <c>divmod</c>.
/// </para>
/// <para>Pure functions over a number: no state, no I/O, no model call (BRD-219).</para>
/// </remarks>
public static class OracleText
{
    /// <summary>What the oracle prints for a missing figure or no time at all.</summary>
    public const string Dash = "—";

    /// <summary>
    /// The oracle's <c>_k</c>: <c>2.5M</c>, <c>42.0k</c>, or the plain whole count below a thousand.
    /// </summary>
    /// <param name="aValue">The count, or <c>null</c>.</param>
    /// <returns>The text the oracle prints for it.</returns>
    public static string Kilo(double? aValue)
    {
        if (aValue is not { } vValue)
        {
            return Dash;
        }

        if (Math.Abs(vValue) >= 1e6)
        {
            return OneDecimal(vValue / 1e6) + "M";
        }

        if (Math.Abs(vValue) >= 1e3)
        {
            return OneDecimal(vValue / 1e3) + "k";
        }

        return ((long)Math.Truncate(vValue)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The oracle's <c>_hms</c>: <c>1h13m</c> from an hour up, <c>38m20s</c> below it, <c>—</c> for none.
    /// </summary>
    /// <param name="aSeconds">The wall seconds, or <c>null</c>.</param>
    /// <returns>The text the oracle prints for it.</returns>
    public static string Hms(double? aSeconds)
    {
        if (aSeconds is not { } vSeconds || vSeconds == 0d)
        {
            return Dash;
        }

        var vWhole = (long)Math.Truncate(vSeconds);
        var vHours = FloorDiv(vWhole, 3600L);
        var vRest = vWhole - (vHours * 3600L);
        var vMinutes = FloorDiv(vRest, 60L);
        var vSecondsLeft = vRest - (vMinutes * 60L);

        return vHours != 0L
            ? string.Create(CultureInfo.InvariantCulture, $"{vHours}h{vMinutes:00}m")
            : string.Create(CultureInfo.InvariantCulture, $"{vMinutes}m{vSecondsLeft:00}s");
    }

    /// <summary>
    /// Python's <c>"%.1f" % aValue</c>: one decimal, the exact binary value rounded half to even.
    /// </summary>
    /// <param name="aValue">A finite double.</param>
    /// <returns>The value with one decimal, a leading minus kept even on <c>-0.0</c>.</returns>
    private static string OneDecimal(double aValue)
    {
        var vTenths = RoundTenthsHalfEven(Math.Abs(aValue));
        var vSign = double.IsNegative(aValue) ? "-" : string.Empty;

        return vSign
               + (vTenths / 10).ToString(CultureInfo.InvariantCulture)
               + "."
               + (vTenths % 10).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Rounds <c>aValue × 10</c> to a whole number, half to even, on the double's exact binary value.
    /// </summary>
    /// <remarks>
    /// A finite double is exactly <c>mantissa × 2^exponent</c>, so <c>aValue × 10</c> is the exact
    /// fraction <c>10·mantissa / 2^-exponent</c>; the quotient and remainder of that division decide
    /// the rounding with no floating-point step in between.
    /// </remarks>
    /// <param name="aValue">A finite, non-negative double.</param>
    /// <returns>The rounded count of tenths.</returns>
    private static BigInteger RoundTenthsHalfEven(double aValue)
    {
        var vBits = BitConverter.DoubleToInt64Bits(aValue);
        var vExponentBits = (int)((vBits >> 52) & 0x7FF);
        var vFraction = vBits & 0xFFFFFFFFFFFFFL;

        var vMantissa = vExponentBits == 0 ? vFraction : vFraction | (1L << 52);
        var vExponent = (vExponentBits == 0 ? 1 : vExponentBits) - 1075;

        var vNumerator = new BigInteger(vMantissa) * 10;
        if (vExponent >= 0)
        {
            return vNumerator << vExponent;
        }

        var vDenominator = BigInteger.One << -vExponent;
        var vQuotient = BigInteger.DivRem(vNumerator, vDenominator, out var vRemainder);
        var vTwice = vRemainder * 2;

        if (vTwice > vDenominator || (vTwice == vDenominator && !vQuotient.IsEven))
        {
            vQuotient += 1;
        }

        return vQuotient;
    }

    /// <summary>Python's flooring integer division, for <c>divmod</c>.</summary>
    /// <param name="aDividend">The dividend.</param>
    /// <param name="aDivisor">A positive divisor.</param>
    /// <returns>The quotient rounded toward negative infinity.</returns>
    private static long FloorDiv(long aDividend, long aDivisor)
    {
        var vQuotient = aDividend / aDivisor;
        return (aDividend % aDivisor != 0) && (aDividend < 0) ? vQuotient - 1 : vQuotient;
    }
}
