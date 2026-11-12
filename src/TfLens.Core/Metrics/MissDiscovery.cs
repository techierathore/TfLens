using System.Globalization;
using System.Text.Json;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Discovery cost — what finding one defect cost — and the per-miss cost list, ported field for field
/// from <c>analyse_misses</c> in <c>tf-metrics.sh</c> (SCHEMA.md §5.5.12; BRD-220, REQ-FN-152, ADR-036).
/// </summary>
/// <remarks>
/// <para>
/// A miss carries its own discovery window (<c>discovery_scope</c>, <c>discovery_tokens_out</c>,
/// <c>discovery_s</c>, <c>discovery_why</c>), which TfLens keeps in the record's <c>Overflow</c> as it
/// keeps every field without a column. A window is <b>measured</b> when its scope is set and not
/// <c>not-measured</c> and it carries tokens; <b>not measured</b> when the scope says so; and a miss
/// written before the field existed is <b>not recorded</b>, outside every divisor (§5.5.8).
/// </para>
/// <para>
/// Each listed miss shows its latest fix's tokens beside its discovery tokens: whole when the fix
/// was its run's only repair, its labelled share when the run repaired several — by the same recount
/// <see cref="MissFigures.SoleFixes"/> uses. Nothing adds the two together.
/// </para>
/// </remarks>
public static class MissDiscovery
{
    /// <summary>The scope a writer stamps on a window it could not measure.</summary>
    public const string NotMeasured = "not-measured";

    /// <summary>
    /// Computes the figures over withdrawn-miss-free records, as the reference reads them.
    /// </summary>
    /// <param name="aMisses">Every miss record, voids already applied.</param>
    /// <param name="aFixes">Every fix record, voids already applied.</param>
    /// <returns>The six figures.</returns>
    /// <exception cref="ArgumentNullException">A record list is <c>null</c>.</exception>
    public static MissDiscoveryFigures Compute(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes)
    {
        ArgumentNullException.ThrowIfNull(aMisses);
        ArgumentNullException.ThrowIfNull(aFixes);

        var vOpened = aMisses.Where(aMiss => aMiss.Backfilled != true).ToList();
        var vFixes = aFixes.Where(aFix => aFix.Backfilled != true).ToList();
        var vWindows = vOpened.Select(aMiss => (Miss: aMiss, Window: Window.Of(aMiss))).ToList();

        var vMeasured = vWindows.Where(aPair => aPair.Window.IsMeasured).ToList();
        var vNotMeasured = vWindows.Where(aPair => aPair.Window.IsNotMeasured).ToList();

        var vMinN = MetricsConstants.MinN;
        double? vTokens = vMeasured.Count >= vMinN
            ? Math.Round(vMeasured.Sum(aPair => aPair.Window.TokensOut!.Value) / vMeasured.Count, 1, MidpointRounding.ToEven)
            : null;
        double? vMinutes = vMeasured.Count >= vMinN
            ? Math.Round(vMeasured.Sum(aPair => aPair.Window.Seconds ?? 0d) / 60d / vMeasured.Count, 1, MidpointRounding.ToEven)
            : null;

        var vFixCost = FixCostOf(vOpened, vFixes);

        // The reference sorts measured-then-not-measured by ts, and its sort is stable.
        var vLines = vMeasured.Concat(vNotMeasured)
            .OrderBy(aPair => aPair.Miss.Ts ?? string.Empty, StringComparer.Ordinal)
            .Select(aPair => LineOf(aPair.Miss, aPair.Window, vFixCost(aPair.Miss.MissId)))
            .ToList();

        return new MissDiscoveryFigures(
            vTokens,
            vMinutes,
            vMeasured.Count,
            vNotMeasured.Count,
            vOpened.Count - vMeasured.Count - vNotMeasured.Count,
            vLines);
    }

    /// <summary>One per-miss line.</summary>
    /// <param name="aMiss">The miss.</param>
    /// <param name="aWindow">Its discovery window.</param>
    /// <param name="aFix">Its latest fix's cost.</param>
    /// <returns>The line.</returns>
    private static MissCostLine LineOf(MissRecord aMiss, Window aWindow, (string Fix, double? Tokens) aFix) => new(
        aMiss.MissId,
        aMiss.ReqId,
        aMiss.FoundPhase,
        aWindow.IsMeasured,
        aWindow.IsMeasured ? aWindow.TokensOut : null,
        aWindow.IsMeasured ? aWindow.Seconds : null,
        aWindow.IsMeasured ? null : aWindow.Why,
        aFix.Fix,
        aFix.Tokens);

    /// <summary>
    /// The latest fix's cost per miss — the reference's <c>_fix_cost</c>.
    /// </summary>
    /// <param name="aOpened">The live misses; fixes naming any other miss are orphans and ignored.</param>
    /// <param name="aFixes">The live fixes.</param>
    /// <returns>A function from miss id to the fix's label and tokens.</returns>
    private static Func<string?, (string Fix, double? Tokens)> FixCostOf(
        IReadOnlyList<MissRecord> aOpened,
        IReadOnlyList<MissFixRecord> aFixes)
    {
        var vKnown = aOpened.Select(aMiss => aMiss.MissId).ToHashSet(StringComparer.Ordinal);
        var vAttribution = MissFigures.AttributionOf(aFixes);
        var vLatest = aFixes
            .Where(aFix => vKnown.Contains(aFix.MissId))
            .GroupBy(aFix => aFix.MissId, StringComparer.Ordinal)
            .ToDictionary(
                aGroup => aGroup.Key,
                aGroup => aGroup.OrderBy(aFix => aFix.Ts ?? string.Empty, StringComparer.Ordinal).Last(),
                StringComparer.Ordinal);

        return aMissId =>
        {
            if (aMissId is null || !vLatest.TryGetValue(aMissId, out var vFix))
            {
                return ("no fix yet", null);
            }

            var vShare = vAttribution(vFix);
            if (vFix.TokensOut is not { } vTokens || vShare is null)
            {
                return ("not measured", null);
            }

            return vShare == 1
                ? ("measured", vTokens)
                : ("apportioned 1/" + vShare.Value.ToString(CultureInfo.InvariantCulture),
                    Math.Round((double)vTokens / vShare.Value, 1, MidpointRounding.ToEven));
        };
    }

    /// <summary>A miss's discovery window, read from the fields TfLens keeps in <c>Overflow</c>.</summary>
    /// <param name="Scope">The <c>discovery_scope</c>, or <c>null</c>.</param>
    /// <param name="TokensOut">The <c>discovery_tokens_out</c>, or <c>null</c>.</param>
    /// <param name="Seconds">The <c>discovery_s</c>, or <c>null</c>.</param>
    /// <param name="Why">The <c>discovery_why</c>, or <c>null</c>.</param>
    private sealed record Window(string? Scope, double? TokensOut, double? Seconds, string? Why)
    {
        /// <summary>A measured window: a scope other than not-measured, and tokens.</summary>
        public bool IsMeasured => Scope is not null && Scope != NotMeasured && TokensOut is not null;

        /// <summary>A window its writer marked as not measured.</summary>
        public bool IsNotMeasured => Scope == NotMeasured;

        /// <summary>Reads the window; a record with no or unreadable <c>Overflow</c> has none.</summary>
        /// <param name="aMiss">The miss.</param>
        /// <returns>The window.</returns>
        public static Window Of(MissRecord aMiss)
        {
            if (string.IsNullOrWhiteSpace(aMiss.Overflow))
            {
                return new Window(null, null, null, null);
            }

            try
            {
                using var vDocument = JsonDocument.Parse(aMiss.Overflow);
                var vRoot = vDocument.RootElement;

                return vRoot.ValueKind != JsonValueKind.Object
                    ? new Window(null, null, null, null)
                    : new Window(
                        Text(vRoot, "discovery_scope"),
                        Number(vRoot, "discovery_tokens_out"),
                        Number(vRoot, "discovery_s"),
                        Text(vRoot, "discovery_why"));
            }
            catch (JsonException)
            {
                return new Window(null, null, null, null);
            }
        }

        private static string? Text(JsonElement aRoot, string aName) =>
            aRoot.TryGetProperty(aName, out var vValue) && vValue.ValueKind == JsonValueKind.String
                ? vValue.GetString()
                : null;

        private static double? Number(JsonElement aRoot, string aName) =>
            aRoot.TryGetProperty(aName, out var vValue) && vValue.ValueKind == JsonValueKind.Number
                ? vValue.GetDouble()
                : null;
    }
}
