using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-UI-078 — the Estimates page prints every figure exactly as <c>tf-metrics.sh</c> prints it, so
/// its two formatters are pinned against the oracle's own <c>_k</c> and <c>_hms</c>.
/// </summary>
/// <remarks>
/// Every expected string below was produced by running the oracle's <c>_k</c> / <c>_hms</c> source in
/// Python 3 on 2026-11-12, never typed by hand. The tie cases are the point: Python's <c>%.1f</c>
/// rounds the exact binary value half to even, so 1250 prints <c>1.2k</c> and 1350 prints
/// <c>1.4k</c> (1.35 is stored a hair above 1.35), and 2550000 prints <c>2.5M</c> (2.55 is stored a
/// hair below).
/// </remarks>
public sealed class OracleTextTests
{
    /// <summary>
    /// A token count reads as the oracle's <c>_k</c> prints it, ties included (REQ-UI-078).
    /// </summary>
    /// <param name="aValue">The count.</param>
    /// <param name="aExpected">What the oracle prints.</param>
    [Theory(DisplayName = "REQ-UI-078 — token counts print exactly as the oracle's _k")]
    [InlineData(0d, "0")]
    [InlineData(999d, "999")]
    [InlineData(1000d, "1.0k")]
    [InlineData(1150d, "1.1k")]
    [InlineData(1250d, "1.2k")]
    [InlineData(1350d, "1.4k")]
    [InlineData(2250d, "2.2k")]
    [InlineData(42000d, "42.0k")]
    [InlineData(55500d, "55.5k")]
    [InlineData(104500d, "104.5k")]
    [InlineData(999950d, "1000.0k")]
    [InlineData(1000000d, "1.0M")]
    [InlineData(2450000d, "2.5M")]
    [InlineData(2550000d, "2.5M")]
    [InlineData(1234567d, "1.2M")]
    public void KiloMatchesTheOracle(double aValue, string aExpected)
    {
        Assert.Equal(aExpected, OracleText.Kilo(aValue));
    }

    /// <summary>
    /// A duration reads as the oracle's <c>_hms</c> prints it, and no time at all is a dash
    /// (REQ-UI-078).
    /// </summary>
    /// <param name="aSeconds">The wall seconds.</param>
    /// <param name="aExpected">What the oracle prints.</param>
    [Theory(DisplayName = "REQ-UI-078 — durations print exactly as the oracle's _hms")]
    [InlineData(0d, "—")]
    [InlineData(1d, "0m01s")]
    [InlineData(59d, "0m59s")]
    [InlineData(60d, "1m00s")]
    [InlineData(600d, "10m00s")]
    [InlineData(2300d, "38m20s")]
    [InlineData(2600d, "43m20s")]
    [InlineData(3600d, "1h00m")]
    [InlineData(4400d, "1h13m")]
    [InlineData(6300d, "1h45m")]
    [InlineData(7200d, "2h00m")]
    [InlineData(86399d, "23h59m")]
    [InlineData(90061d, "25h01m")]
    public void HmsMatchesTheOracle(double aSeconds, string aExpected)
    {
        Assert.Equal(aExpected, OracleText.Hms(aSeconds));
    }

    /// <summary>
    /// A missing figure prints the oracle's dash, never a zero (REQ-UI-078, BRD-216).
    /// </summary>
    [Fact(DisplayName = "REQ-UI-078 — a missing figure prints a dash, never a zero")]
    public void MissingFigurePrintsDash()
    {
        Assert.Equal([OracleText.Dash, OracleText.Dash], [OracleText.Kilo(null), OracleText.Hms(null)]);
    }
}
