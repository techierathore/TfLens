namespace TfLens.Core.Contracts;

/// <summary>
/// What finding a defect cost, and each miss's discovery cost beside its latest fix's cost
/// (SCHEMA.md §5.5.12; BRD-220, REQ-FN-152).
/// </summary>
/// <remarks>
/// The shape of the six keys <c>analyse_misses</c> in <c>tf-metrics.sh</c> writes into the
/// <c>misses</c> block. The two means are <c>null</c> under <see cref="MetricsConstants.MinN"/>
/// measured misses — never a zero — and every divisor travels beside its figure.
/// </remarks>
/// <param name="TokensPerMiss">Mean discovery output tokens per measured miss, or <c>null</c>.</param>
/// <param name="MinutesPerMiss">Mean discovery minutes per measured miss, or <c>null</c>.</param>
/// <param name="MeasuredN">Misses whose discovery window was measured.</param>
/// <param name="NotMeasuredN">Misses whose window was marked <c>not-measured</c>.</param>
/// <param name="NotRecordedN">Misses written before the field existed — outside every divisor.</param>
/// <param name="PerMiss">One line per measured or not-measured miss, oldest first.</param>
public sealed record MissDiscoveryFigures(
    double? TokensPerMiss,
    double? MinutesPerMiss,
    int MeasuredN,
    int NotMeasuredN,
    int NotRecordedN,
    IReadOnlyList<MissCostLine> PerMiss)
{
    /// <summary>The figures over no miss at all.</summary>
    public static MissDiscoveryFigures Empty { get; } = new(null, null, 0, 0, 0, []);
}

/// <summary>
/// One miss's discovery cost beside its latest fix's cost — two columns, never one number
/// (SCHEMA.md §5.5.12).
/// </summary>
/// <param name="MissId">The miss.</param>
/// <param name="ReqId">Its row, when it named one.</param>
/// <param name="FoundPhase">Where it was found.</param>
/// <param name="IsMeasured"><c>true</c> when the discovery window was measured.</param>
/// <param name="DiscoveryTokensOut">The window's output tokens, when measured.</param>
/// <param name="DiscoverySeconds">The window's length, when measured and recorded.</param>
/// <param name="DiscoveryWhy">Why the window was not measured, when it was not.</param>
/// <param name="Fix"><c>no fix yet</c>, <c>not measured</c>, <c>measured</c> or <c>apportioned 1/n</c>.</param>
/// <param name="FixTokensOut">The latest fix's tokens, whole or its share; <c>null</c> when none.</param>
public sealed record MissCostLine(
    string? MissId,
    string? ReqId,
    string? FoundPhase,
    bool IsMeasured,
    double? DiscoveryTokensOut,
    double? DiscoverySeconds,
    string? DiscoveryWhy,
    string Fix,
    double? FixTokensOut);
