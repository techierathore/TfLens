using System.Globalization;
using System.Text.Json;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Playbook;

/// <summary>
/// The AI-First-Playbook edition's <b>own</b> miss reporting guards (REQ-FN-105, BRD-166, ADR-024).
/// </summary>
/// <remarks>
/// <para>
/// <b>These are stricter than the TechieFlow stream's and must never be relaxed to match it.</b>
/// <see cref="MissAttributionTaint"/> asks one question of a TechieFlow miss — is
/// <c>origin_confidence</c> <c>linked</c>? The Playbook producer asks three, because it publishes a
/// window and a data-quality block that TechieFlow does not: <c>linked</c> <b>and</b> a complete valid
/// source window <b>and</b> a non-<c>null</c> observed model. The headline cost guard is the same shape:
/// <c>cost_attribution:"sole"</c> <b>and</b> a complete valid window <b>and</b>
/// <c>data_quality.cost_status:"complete"</c>.
/// </para>
/// <para>
/// A future reviewer will notice the asymmetry and try to unify the two editions on one guard. Doing so
/// <b>downward</b> — dropping the window and the model check so the Playbook matches TechieFlow — would
/// publish a claim the producer explicitly refuses to make: it emits a data-quality block precisely so a
/// consumer can decline to compute, and a consumer that ignores it is asserting a confidence the
/// producer withheld. Doing so <b>upward</b> is not possible either: a TechieFlow record carries no
/// window and no <c>cost_status</c>, so the extra conditions would fail every row and silence a stream
/// that is honestly reportable under its own rules. Two editions, two guard sets, one set of tables
/// (ADR-024). There is no flag, parameter or overload here that relaxes either (REQ-NFR-013).
/// </para>
/// <para>
/// <b>An inferred or unknown origin never enters an "unknown model" performance bucket.</b> The model
/// distribution is computed over <see cref="PlaybookAttributionSplit.Attributed"/> alone, and every
/// attributed record carries a non-<c>null</c> model by construction, so no <c>unknown</c>,
/// <c>not-recorded</c> or <c>—</c> row can appear beside real model names. A bucket named for a model is
/// a claim about a model; the refused records are reported by <i>reason</i> instead
/// (<see cref="PlaybookGuardReasons"/>), which is a claim about the data.
/// </para>
/// <para>
/// <b>Where the window and the quality block are read from.</b> Cluster A's schema gives the miss tables
/// three Playbook columns — <c>ItemId</c>, <c>FoundPhaseGate</c> and <c>SourceLineHash</c> — and no
/// column for <c>data_quality</c> or the source window, because those are guard inputs rather than
/// reportable axes. <see cref="PlaybookMissNormalizer"/> preserves them verbatim in the record's
/// <c>Overflow</c> JSON, exactly as the parser preserves every other undocumented property, and
/// <see cref="QualityOf"/> reads them back. A rebuild from <c>data/raw/</c> therefore re-derives the same
/// verdicts, which is the same property <c>MissAmendFolder</c> exists to preserve.
/// </para>
/// </remarks>
public static class PlaybookMissGuards
{
    /// <summary>The one <c>data_quality.cost_status</c> that admits a record to a headline cost figure.</summary>
    public const string CostStatusComplete = "complete";

    /// <summary>Wire key of the producer's data-quality block.</summary>
    public const string DataQualityKey = "data_quality";

    /// <summary>Wire key of the producer's source-window block.</summary>
    public const string SourceWindowKey = "source_window";

    /// <summary>Wire suffix of every rate-card estimate; a key ending in this is never a measurement.</summary>
    /// <remarks>
    /// Measured <c>cost_usd</c> and rate-card <c>*_usd_estimate</c> values never share a series or a
    /// total (BRD-166). The rule is enforced at ingest — <see cref="PlaybookMissNormalizer"/> maps no key
    /// carrying this suffix into <c>MissFixRecord.CostUsd</c> — and again here, where the measured figure
    /// reads <c>CostUsd</c> and nothing else.
    /// </remarks>
    public const string UsdEstimateSuffix = "_usd_estimate";

    /// <summary>
    /// Decides whether one miss may support a model or tier attribution (BRD-166).
    /// </summary>
    /// <remarks>
    /// The three conditions are checked in the order a reader would ask them, and the <b>first</b>
    /// failure is the reported reason, so the diagnostic names the earliest thing that went wrong rather
    /// than the last thing checked.
    /// </remarks>
    /// <param name="aMiss">The miss, amendments already folded.</param>
    /// <returns><c>null</c> when the record may be attributed, else one of <see cref="PlaybookGuardReasons"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aMiss"/> is <c>null</c>.</exception>
    public static string? RefuseAttribution(MissRecord aMiss)
    {
        ArgumentNullException.ThrowIfNull(aMiss);

        if (!string.Equals(aMiss.OriginConfidence, MissAttributionTaint.Linked, StringComparison.Ordinal))
        {
            return PlaybookGuardReasons.NotLinked;
        }

        if (!QualityOf(aMiss.Overflow).IsCompleteValidWindow)
        {
            return PlaybookGuardReasons.WindowNotCompleteAndValid;
        }

        return string.IsNullOrEmpty(aMiss.OriginModel)
            ? PlaybookGuardReasons.NoObservedModel
            : null;
    }

    /// <summary>
    /// Places one fix record in exactly one cost cohort, or refuses it (BRD-166).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>sole</c>, <c>shared:&lt;n&gt;</c> and <c>none</c> are three cohorts and never one:
    /// <see cref="PlaybookCostCohort.Headline"/> is the only figure that may be called <i>the</i> cost of
    /// a repair, <see cref="PlaybookCostCohort.Apportioned"/> is arithmetic over a window that closed
    /// several misses and is labelled as such, and <see cref="PlaybookCostCohort.Excluded"/> is a count
    /// that can never be a divisor.
    /// </para>
    /// <para>
    /// <b>Chosen reading, logged as a specification gap.</b> BRD-166 states the window and
    /// <c>cost_status</c> conditions for the <i>headline</i> figure and says only that <c>shared:n</c> is
    /// "shown separately as apportioned". It does not say whether an apportioned figure may rest on an
    /// invalid window. This applies the window test to both cohorts and the <c>cost_status</c> test to
    /// the headline alone, because the phase contract is explicit that the producer may retain
    /// zero-valued totals on an invalid row — so a consumer that apportions an invalid window publishes a
    /// confident zero rather than an error. <c>cost_status</c> is deliberately <b>not</b> extended to the
    /// apportioned cohort: it qualifies a measured dollar figure, and an apportioned column is already
    /// labelled as an estimate of a share.
    /// </para>
    /// </remarks>
    /// <param name="aFix">The fix record.</param>
    /// <returns>The cohort, the refusal reason when there is one, and the divisor when the cohort has one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aFix"/> is <c>null</c>.</exception>
    public static PlaybookCostVerdict ClassifyCost(MissFixRecord aFix)
    {
        ArgumentNullException.ThrowIfNull(aFix);

        if (string.IsNullOrEmpty(aFix.CostAttribution)
            || string.Equals(aFix.CostAttribution, MissFigures.NoneAttribution, StringComparison.Ordinal))
        {
            return new PlaybookCostVerdict(PlaybookCostCohort.Excluded, PlaybookGuardReasons.NoneAttribution, null);
        }

        var vQuality = QualityOf(aFix.Overflow);

        if (string.Equals(aFix.CostAttribution, MissFigures.SoleAttribution, StringComparison.Ordinal))
        {
            if (!vQuality.IsCompleteValidWindow)
            {
                return new PlaybookCostVerdict(
                    PlaybookCostCohort.Refused, PlaybookGuardReasons.WindowNotCompleteAndValid, null);
            }

            return string.Equals(vQuality.CostStatus, CostStatusComplete, StringComparison.Ordinal)
                ? new PlaybookCostVerdict(PlaybookCostCohort.Headline, null, 1)
                : new PlaybookCostVerdict(
                    PlaybookCostCohort.Refused, PlaybookGuardReasons.CostStatusNotComplete, null);
        }

        if (SharedAcross(aFix.CostAttribution) is { } vAcross)
        {
            return vQuality.IsCompleteValidWindow
                ? new PlaybookCostVerdict(PlaybookCostCohort.Apportioned, null, vAcross)
                : new PlaybookCostVerdict(
                    PlaybookCostCohort.Refused, PlaybookGuardReasons.WindowNotCompleteAndValid, null);
        }

        return new PlaybookCostVerdict(PlaybookCostCohort.Refused, PlaybookGuardReasons.UnknownAttribution, null);
    }

    /// <summary>
    /// Reads the producer's window and data-quality block out of a record's preserved overflow JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Absent is never a pass.</b> A record carrying no window block, no <c>data_quality</c> block or
    /// malformed JSON yields <see cref="PlaybookMissQuality.Absent"/>, which fails every guard. Defaulting
    /// the other way would turn "the producer said nothing" into "the producer vouched for it", which is
    /// the exact substitution the guards exist to prevent.
    /// </para>
    /// <para>
    /// <b>Chosen reading, logged as a specification gap.</b> The producer contract defers the full field
    /// list to its own <c>Telemetry-Guide.md</c> §7, which TfLens does not hold. Two spellings are
    /// therefore accepted: a nested <c>source_window</c> object carrying <c>complete</c> and
    /// <c>valid</c>, and — as a fallback — the schema-2 phase spelling of a top-level <c>complete</c>
    /// beside <c>data_quality.valid</c>, which is the spelling the sibling
    /// <c>Phase-Efficiency-TfLens-Contract.md</c> publishes. <c>cost_status</c> is read from
    /// <c>data_quality</c> first and from the top level second. Nothing else is inferred.
    /// </para>
    /// </remarks>
    /// <param name="aOverflowJson">The record's <c>Overflow</c> JSON object, or <c>null</c>.</param>
    /// <returns>What the producer said about the window, or <see cref="PlaybookMissQuality.Absent"/>.</returns>
    public static PlaybookMissQuality QualityOf(string? aOverflowJson)
    {
        if (string.IsNullOrWhiteSpace(aOverflowJson))
        {
            return PlaybookMissQuality.Absent;
        }

        try
        {
            using var vDocument = JsonDocument.Parse(aOverflowJson);
            return vDocument.RootElement.ValueKind == JsonValueKind.Object
                ? ReadQuality(vDocument.RootElement)
                : PlaybookMissQuality.Absent;
        }
        catch (JsonException)
        {
            // A record TfLens cannot read is a record TfLens cannot vouch for. Counted by the caller as
            // a refusal, never thrown: one malformed overflow blob must not fail a whole report.
            return PlaybookMissQuality.Absent;
        }
    }

    /// <summary>
    /// How many ways a <c>shared:&lt;n&gt;</c> window splits.
    /// </summary>
    /// <param name="aAttribution">The stored <c>cost_attribution</c>.</param>
    /// <returns>The divisor when the value is a well-formed <c>shared:n</c> with <c>n</c> at least 1, else <c>null</c>.</returns>
    public static int? SharedAcross(string? aAttribution)
    {
        if (aAttribution is null
            || !aAttribution.StartsWith(MissFigures.SharedAttributionPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(
            aAttribution[MissFigures.SharedAttributionPrefix.Length..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var vAcross) && vAcross >= 1
            ? vAcross
            : null;
    }

    /// <summary>Reads the window and quality facts out of an already-parsed overflow object.</summary>
    /// <param name="aRoot">The overflow JSON object.</param>
    /// <returns>The quality block.</returns>
    private static PlaybookMissQuality ReadQuality(JsonElement aRoot)
    {
        var vDataQuality = Child(aRoot, DataQualityKey);
        var vWindow = Child(aRoot, SourceWindowKey);

        var vComplete = Flag(vWindow, "complete") ?? Flag(aRoot, "complete") ?? false;
        var vValid = Flag(vWindow, "valid") ?? Flag(vDataQuality, "valid") ?? false;
        var vCostStatus = Text(vDataQuality, "cost_status") ?? Text(aRoot, "cost_status");

        return new PlaybookMissQuality(vComplete, vValid, vCostStatus);
    }

    /// <summary>Returns a nested object property, or <c>null</c> when it is absent or not an object.</summary>
    /// <param name="aElement">The parent element, or <c>null</c>.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The child object, or <c>null</c>.</returns>
    private static JsonElement? Child(JsonElement? aElement, string aName) =>
        aElement is { } vParent
        && vParent.ValueKind == JsonValueKind.Object
        && vParent.TryGetProperty(aName, out var vChild)
        && vChild.ValueKind == JsonValueKind.Object
            ? vChild
            : null;

    /// <summary>Reads a boolean property, or <c>null</c> when it is absent or not a boolean.</summary>
    /// <param name="aElement">The element to read from, or <c>null</c>.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The flag, or <c>null</c> when the producer did not state it.</returns>
    private static bool? Flag(JsonElement? aElement, string aName)
    {
        if (aElement is not { } vParent
            || vParent.ValueKind != JsonValueKind.Object
            || !vParent.TryGetProperty(aName, out var vValue))
        {
            return null;
        }

        return vValue.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    /// <summary>Reads a string property, or <c>null</c> when it is absent or not a string.</summary>
    /// <param name="aElement">The element to read from, or <c>null</c>.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static string? Text(JsonElement? aElement, string aName)
    {
        if (aElement is not { } vParent
            || vParent.ValueKind != JsonValueKind.Object
            || !vParent.TryGetProperty(aName, out var vValue)
            || vValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return vValue.GetString();
    }
}

/// <summary>
/// Why the Playbook guards refused to let a record support a figure (REQ-FN-105, BRD-166).
/// </summary>
/// <remarks>
/// These strings are what Coverage and the export show. They name a property of the <i>data</i> — never
/// a model, an agent or an actor — which is what keeps a refusal from reading as a verdict on whoever
/// produced the record.
/// </remarks>
public static class PlaybookGuardReasons
{
    /// <summary><c>origin_confidence</c> is not <c>linked</c> — the originating run was guessed.</summary>
    public const string NotLinked = "origin-confidence-not-linked";

    /// <summary>The producer did not vouch for a complete, valid source window.</summary>
    public const string WindowNotCompleteAndValid = "source-window-not-complete-and-valid";

    /// <summary>No model was observed, so no bucket may be named for one.</summary>
    public const string NoObservedModel = "no-observed-model";

    /// <summary><c>cost_attribution</c> is <c>none</c> — a count, never a divisor.</summary>
    public const string NoneAttribution = "cost-attribution-none";

    /// <summary><c>data_quality.cost_status</c> is not <c>complete</c>.</summary>
    public const string CostStatusNotComplete = "cost-status-not-complete";

    /// <summary><c>cost_attribution</c> is outside the producer's vocabulary entirely.</summary>
    public const string UnknownAttribution = "cost-attribution-unrecognised";
}

/// <summary>
/// What the Playbook producer said about one record's source window and data quality (REQ-FN-105).
/// </summary>
/// <remarks>
/// Read out of the record's preserved <c>Overflow</c> JSON by
/// <see cref="PlaybookMissGuards.QualityOf"/>. There is no constructor path that produces a passing
/// block from absent input: <see cref="Absent"/> fails every guard, on purpose.
/// </remarks>
/// <param name="WindowComplete">The producer stated the source window closed.</param>
/// <param name="WindowValid">The producer stated the window's data survived its own invariants.</param>
/// <param name="CostStatus">The producer's <c>cost_status</c>, or <c>null</c> when it stated none.</param>
public sealed record PlaybookMissQuality(bool WindowComplete, bool WindowValid, string? CostStatus)
{
    /// <summary>Whether the producer vouched for the whole window — both halves, never either.</summary>
    public bool IsCompleteValidWindow => WindowComplete && WindowValid;

    /// <summary>What a record that said nothing yields; it fails every guard.</summary>
    public static PlaybookMissQuality Absent { get; } = new(false, false, null);
}

/// <summary>The three cost cohorts, plus the refusal that is none of them (REQ-FN-105, BRD-166).</summary>
/// <remarks>
/// They are an enum rather than three booleans so a record cannot be in two at once, and there is no
/// member meaning "headline or apportioned" — the whole point is that the two are never added.
/// </remarks>
public enum PlaybookCostCohort
{
    /// <summary><c>sole</c>, complete valid window, <c>cost_status:"complete"</c> — the only headline figure.</summary>
    Headline = 0,

    /// <summary><c>shared:&lt;n&gt;</c> over a complete valid window — arithmetic, reported separately.</summary>
    Apportioned = 1,

    /// <summary><c>none</c> — correct data that can carry no cost; a count, never a divisor.</summary>
    Excluded = 2,

    /// <summary>The guards declined the record; <see cref="PlaybookCostVerdict.Reason"/> says why.</summary>
    Refused = 3
}

/// <summary>One fix record's cost placement (REQ-FN-105).</summary>
/// <param name="Cohort">Which cohort the record belongs to.</param>
/// <param name="Reason">One of <see cref="PlaybookGuardReasons"/> when it was excluded or refused, else <c>null</c>.</param>
/// <param name="Across">The divisor: <c>1</c> for a headline record, <c>n</c> for an apportioned one, <c>null</c> otherwise.</param>
public sealed record PlaybookCostVerdict(PlaybookCostCohort Cohort, string? Reason, int? Across);

/// <summary>How many records one guard refusal reason held out of a figure (REQ-FN-105).</summary>
/// <remarks>
/// The refusal leaves the engine as data rather than as a log line, for the same reason
/// <see cref="MissAttributionSet"/> carries its exclusions: an exclusion the reader cannot see is
/// indistinguishable from a bug.
/// </remarks>
/// <param name="Reason">One of <see cref="PlaybookGuardReasons"/>.</param>
/// <param name="Records">How many records it refused.</param>
public sealed record PlaybookGuardRefusal(string Reason, int Records);
