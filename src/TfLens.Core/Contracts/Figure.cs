using System.Globalization;

namespace TfLens.Core.Contracts;

/// <summary>
/// A reportable number, or an honest statement of why there is no number.
/// </summary>
/// <remarks>
/// ADR-007: the provenance rules are enforced by the shape of the result, not by discipline. A
/// <see cref="Figure"/> is one of three cases — a value, <c>insufficient data (n=…)</c> below
/// <see cref="MetricsConstants.MinN"/> supporting records, or not applicable — and a page binding a
/// figure cannot print a number for the second or third case because no number exists on them. There
/// is deliberately no <c>Value</c> accessor that returns a default: read it through
/// <see cref="TryGetValue"/> or render it through <see cref="Display"/>.
/// </remarks>
public readonly record struct Figure
{
    private readonly double objValue;
    private readonly string? objRendered;

    private Figure(FigureKind aKind, double aValue, int aSupportingRecords, string? aRendered)
    {
        Kind = aKind;
        objValue = aValue;
        SupportingRecords = aSupportingRecords;
        objRendered = aRendered;
    }

    /// <summary>Which of the three cases this figure is.</summary>
    public FigureKind Kind { get; }

    /// <summary>
    /// How many records supported (or failed to support) the figure.
    /// </summary>
    /// <remarks>
    /// Carried on every case so the UI can show <c>n</c> beside a value as readily as inside an
    /// <see cref="FigureKind.InsufficientData"/> message.
    /// </remarks>
    public int SupportingRecords { get; }

    /// <summary>True when the figure carries a number that may be rendered.</summary>
    public bool HasValue => Kind == FigureKind.Value;

    /// <summary>
    /// Builds a figure that carries a number.
    /// </summary>
    /// <param name="aValue">The computed value.</param>
    /// <param name="aSupportingRecords">Records the value was computed from; must be at least <see cref="MetricsConstants.MinN"/>.</param>
    /// <param name="aRendered">The exact display string the reference implementation would print, e.g. <c>67%</c>.</param>
    /// <returns>A <see cref="FigureKind.Value"/> figure.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Fewer supporting records than the minimum — call <see cref="InsufficientData"/> instead.</exception>
    public static Figure Value(double aValue, int aSupportingRecords, string? aRendered = null)
    {
        if (aSupportingRecords < MetricsConstants.MinN)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aSupportingRecords),
                aSupportingRecords,
                $"A figure with fewer than {MetricsConstants.MinN} supporting records must be InsufficientData.");
        }

        return new Figure(FigureKind.Value, aValue, aSupportingRecords, aRendered);
    }

    /// <summary>
    /// Builds a figure that refuses to be a number because too few records support it.
    /// </summary>
    /// <param name="aSupportingRecords">How many records there were.</param>
    /// <returns>An <see cref="FigureKind.InsufficientData"/> figure.</returns>
    public static Figure InsufficientData(int aSupportingRecords) =>
        new(FigureKind.InsufficientData, 0d, aSupportingRecords, null);

    /// <summary>
    /// Builds a figure for a metric that does not apply — a zero denominator, or a measurement that
    /// does not exist for this segment (measured dollars outside OpenCode, for instance).
    /// </summary>
    /// <returns>A <see cref="FigureKind.NotApplicable"/> figure.</returns>
    public static Figure NotApplicable() => new(FigureKind.NotApplicable, 0d, 0, null);

    /// <summary>
    /// Reads the number out of a <see cref="FigureKind.Value"/> figure.
    /// </summary>
    /// <param name="aValue">Receives the value when this figure carries one.</param>
    /// <returns><c>true</c> when a number was available.</returns>
    public bool TryGetValue(out double aValue)
    {
        aValue = objValue;
        return Kind == FigureKind.Value;
    }

    /// <summary>
    /// The display string for the figure, exactly as the reference implementation would print it.
    /// </summary>
    /// <returns>The rendered value, <c>insufficient data (n=…)</c>, or <c>—</c>.</returns>
    public string Display() => Kind switch
    {
        FigureKind.Value => objRendered ?? objValue.ToString(CultureInfo.InvariantCulture),
        FigureKind.InsufficientData => $"insufficient data (n={SupportingRecords})",
        _ => "—"
    };

    /// <summary>Renders the figure through <see cref="Display"/>.</summary>
    /// <returns>The display string.</returns>
    public override string ToString() => Display();
}

/// <summary>The three cases a <see cref="Figure"/> can be.</summary>
public enum FigureKind
{
    /// <summary>A real number, backed by at least <see cref="MetricsConstants.MinN"/> records.</summary>
    Value = 0,

    /// <summary>Too few records to state a number honestly; the count is carried instead.</summary>
    InsufficientData = 1,

    /// <summary>The metric does not apply to this segment — rendered as an em dash.</summary>
    NotApplicable = 2
}

/// <summary>
/// The constants the reference implementation fixes, carried here so a port cannot drift from it.
/// </summary>
/// <remarks>
/// None of these is a configuration key. <see cref="MinN"/> in particular has no switch: the brief's
/// requirement is that no flag can relax the provenance rules (BRD-89).
/// </remarks>
public static class MetricsConstants
{
    /// <summary>Fewer supporting records than this yields <c>insufficient data</c>, never a number.</summary>
    public const int MinN = 3;

    /// <summary>The segment key used when <c>project_type</c> was inferred rather than declared.</summary>
    public const string Unclassified = "unclassified";

    /// <summary>The gate bucket used when a failure record names no gate.</summary>
    public const string Unattributed = "unattributed";

    /// <summary>The gate value meaning no gate caught the defect; reported as its own row.</summary>
    public const string Escaped = "escaped";

    /// <summary>Gate names in the reference's report order; <see cref="Unattributed"/> follows them.</summary>
    public static readonly IReadOnlyList<string> GateOrder =
        ["build", "acceptance", "render", "visual", "perf", "standards", Escaped];

    /// <summary>
    /// Gates that entered the enum after collection started, with the date they were added.
    /// </summary>
    /// <remarks>
    /// Their share of a raw distribution is structurally understated, so they are reported as
    /// <c>ran</c> beside <c>caught</c> rather than as a share (SCHEMA.md §3.5). Keep in sync with
    /// <c>LATE_GATES</c> in <c>tf-metrics.sh</c>.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> LateGates =
        new Dictionary<string, string> { ["perf"] = "2026-08-10" };

    /// <summary>
    /// Optional fields that entered a stream after collection started, with the date they were added.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same rule as <see cref="LateGates"/>, one table over.</b> A gate added mid-stream is
    /// structurally understated in a raw distribution; a <i>field</i> added mid-stream is structurally
    /// understated in a raw denominator, because the records written before it existed had no field to
    /// fill. Both are read against what could have been observed, never against the total, and both
    /// report the excluded count rather than dropping it silently (SCHEMA.md §3.5, §5.5.6).
    /// </para>
    /// <para>
    /// A miss written before <c>why_missed</c> shipped is therefore <b>not</b> an unassessed miss: it
    /// leaves that field's denominator entirely and is reported as <c>why_missed_predates_field</c>
    /// beside <c>why_missed_eligible</c>. Keep in step with <c>FIELD_SINCE</c> in <c>tf-metrics.sh</c>,
    /// and add a row here whenever an optional field is added to any stream (REQ-FN-076, BRD-117).
    /// </para>
    /// <para>
    /// <b>Extended 2026-09-01 (REQ-FN-091, BRD-148) with the three SCHEMA §2.6 <c>runs</c> fields</b>,
    /// all at <c>2026-08-31</c> — the date the producer began emitting them. This is the same table and
    /// the same code path as <c>why_missed</c> on purpose: a second table would be a second place for
    /// the floor to be forgotten. The consequence matters for <c>/effort</c>, because a run written
    /// before that date is not a run with no sub-agents — it is a run that could not have said, and
    /// <c>unobserved_predates_field</c> is therefore a permanent exclusion rather than a gap that a
    /// later sync might fill (ADR-026).
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> FieldSince =
        new Dictionary<string, string>
        {
            ["why_missed"] = "2026-08-28",
            ["subagent_runs"] = "2026-08-31",
            ["tokens_out_subagents"] = "2026-08-31",
            ["model_tokens_out"] = "2026-08-31"
        };

    /// <summary>Verdicts that are not failures, and so do not enter the gate distribution.</summary>
    public static readonly IReadOnlyList<string> NonFailureVerdicts = ["Verified", "Done (pre-existing)"];

    /// <summary>
    /// The reference's <c>pct()</c>: an em dash on a zero denominator, otherwise a whole percentage.
    /// </summary>
    /// <param name="aNumerator">The numerator.</param>
    /// <param name="aDenominator">The denominator.</param>
    /// <returns><c>—</c> when the denominator is zero, else e.g. <c>67%</c>.</returns>
    public static string Pct(int aNumerator, int aDenominator) =>
        aDenominator == 0
            ? "—"
            : (100.0 * aNumerator / aDenominator).ToString("F0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// The reference's <c>pct()</c> over the long totals a token or duration block deals in.
    /// </summary>
    /// <remarks>
    /// The same rule and the same rendering as <see cref="Pct(int, int)"/>, widened because a phase's
    /// cache-read total runs to tens of millions per repository and would overflow an <c>int</c> across a
    /// year of streams. It exists as an overload rather than a second helper so there is exactly one
    /// implementation of the em dash and of the rounding: <c>share_of_*</c> is diffed against the oracle
    /// as a <b>string</b> (BRD-152), and two spellings of "no denominator" would be two ways to fail that.
    /// </remarks>
    /// <param name="aNumerator">The numerator.</param>
    /// <param name="aDenominator">The denominator.</param>
    /// <returns><c>—</c> when the denominator is zero, else e.g. <c>87%</c>.</returns>
    public static string Pct(long aNumerator, long aDenominator) =>
        aDenominator == 0
            ? "—"
            : (100.0 * aNumerator / aDenominator).ToString("F0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// The reference's <c>median()</c>: the middle value, or the mean of the middle two.
    /// </summary>
    /// <param name="aValues">The values to take the median of.</param>
    /// <returns>The median, or <c>null</c> when the sequence is empty.</returns>
    public static double? Median(IEnumerable<double> aValues)
    {
        var vSorted = aValues.OrderBy(aX => aX).ToList();
        if (vSorted.Count == 0)
        {
            return null;
        }

        var vN = vSorted.Count;
        return vN % 2 == 1 ? vSorted[vN / 2] : (vSorted[vN / 2 - 1] + vSorted[vN / 2]) / 2.0;
    }
}
