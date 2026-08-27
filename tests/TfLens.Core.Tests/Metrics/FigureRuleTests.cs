using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-050 and REQ-NFR-009 — the minimum-n floor and the reference's <c>pct()</c> contract.
/// </summary>
public sealed class FigureRuleTests
{
    /// <summary>The floor is the reference's constant, and it is a constant rather than a setting.</summary>
    [Fact]
    public void MinimumSupportingRecordsIsThree()
    {
        Assert.Equal(3, MetricsConstants.MinN);
    }

    /// <summary>A figure cannot be built with a value below the floor, so no code path can leak one.</summary>
    [Fact]
    public void ValueBelowTheFloorCannotBeConstructed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Figure.Value(50d, 2, "50%"));
    }

    /// <summary>An insufficient-data figure carries the count and refuses to hand out a number.</summary>
    [Fact]
    public void InsufficientDataCarriesTheCountAndNoValue()
    {
        var vFigure = Figure.InsufficientData(2);

        Assert.False(vFigure.TryGetValue(out _));
        Assert.Equal(2, vFigure.SupportingRecords);
        Assert.Equal("insufficient data (n=2)", vFigure.Display());
    }

    /// <summary>A zero denominator prints an em dash rather than a rate, exactly as the reference does.</summary>
    [Fact]
    public void PercentOnAZeroDenominatorIsAnEmDash()
    {
        Assert.Equal("—", MetricsConstants.Pct(0, 0));
        Assert.Equal("—", MetricsConstants.Pct(5, 0));
    }

    /// <summary>A whole percentage is rendered the way the reference's <c>%.0f%%</c> renders it.</summary>
    /// <param name="aNumerator">The numerator.</param>
    /// <param name="aDenominator">The denominator.</param>
    /// <param name="aExpected">The rendered value.</param>
    [Theory]
    [InlineData(3, 7, "43%")]
    [InlineData(2, 3, "67%")]
    [InlineData(1, 4, "25%")]
    [InlineData(4, 4, "100%")]
    public void PercentRendersWholeNumbers(int aNumerator, int aDenominator, string aExpected)
    {
        Assert.Equal(aExpected, MetricsConstants.Pct(aNumerator, aDenominator));
    }

    /// <summary>Below the floor, a first-pass rate refuses to be a number no matter how flattering it would be.</summary>
    [Fact]
    public void FirstPassRateRefusesBelowTheFloor()
    {
        Assert.Equal("insufficient data (n=2)", FirstPassRate.Compute(2, 2).Display());
        Assert.Equal("100%", FirstPassRate.Compute(3, 3).Display());
    }

    /// <summary>Below the floor, an escape rate refuses to be a number too.</summary>
    [Fact]
    public void EscapeRateRefusesBelowTheFloor()
    {
        Assert.Equal("insufficient data (n=1)", EscapeRate.Compute(1, 1).Display());
        Assert.Equal("33%", EscapeRate.Compute(1, 3).Display());
    }

    /// <summary>The median is the reference's: the middle value, or the mean of the middle two.</summary>
    [Fact]
    public void MedianMatchesTheReference()
    {
        Assert.Equal(3d, MetricsConstants.Median([1d, 3d, 5d]));
        Assert.Equal(3.5d, MetricsConstants.Median([2d, 3d, 4d, 6d]));
        Assert.Null(MetricsConstants.Median([]));
    }
}
