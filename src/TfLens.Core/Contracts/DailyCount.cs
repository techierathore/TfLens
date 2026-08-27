namespace TfLens.Core.Contracts;

/// <summary>
/// How many records of one stream carry a given day's date, for one user and framework.
/// </summary>
/// <remarks>
/// This exists to back the KPI sparklines, and its shape is deliberately narrow. A sparkline may only
/// ever plot <b>the same quantity its tile states</b> — a count tile gets a count series. Rate tiles
/// (first-pass rate, escape rate) get no sparkline at all, because a rate computed over a single day
/// is almost always below <see cref="MetricsConstants.MinN"/>, and a line drawn through a run of
/// <c>insufficient data</c> points would be exactly the plausible-looking fabrication this product
/// exists to prevent.
/// </remarks>
/// <param name="Day">The UTC date the records fall on.</param>
/// <param name="Count">How many records carry it.</param>
public sealed record DailyCount(DateOnly Day, int Count);

/// <summary>
/// A named daily series, plus what it counts, so a caller can label it honestly.
/// </summary>
/// <param name="Points">The days present, oldest first. Days with no records are included as zero.</param>
/// <param name="Label">A human sentence naming exactly what is plotted, used as the sparkline's title.</param>
public sealed record DailySeries(IReadOnlyList<DailyCount> Points, string Label)
{
    /// <summary>An empty series; renders no sparkline.</summary>
    public static readonly DailySeries Empty = new([], string.Empty);

    /// <summary>The counts alone, in order, for the sparkline geometry.</summary>
    public IReadOnlyList<double> Values => Points.Select(aP => (double)aP.Count).ToList();

    /// <summary>
    /// True when there is enough real history to draw a line that means something.
    /// </summary>
    /// <remarks>
    /// Two points would draw a straight segment between whatever two days happen to exist, which reads
    /// as a trend without being one. <see cref="MetricsConstants.MinN"/> is reused rather than invented
    /// so the threshold matches every other "too little data to say" decision in the product.
    /// </remarks>
    public bool IsPlottable => Points.Count >= MetricsConstants.MinN;
}
