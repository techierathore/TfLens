namespace TfLens.Core.Metrics;

/// <summary>
/// Which provenance bucket a set of gate records belongs to.
/// </summary>
/// <remarks>
/// The reference iterates <c>(("live", live), ("backfilled", back))</c> and applies one rule
/// differently per bucket — the backfill taint exclusion is a live-only rule. This enum names that
/// bucket. It selects rules <em>within</em> a segment; it can never merge two segments, because
/// <see cref="Contracts.AnalysisResult"/> keeps the two dictionaries apart (REQ-FN-047, REQ-NFR-009).
/// </remarks>
public enum Provenance
{
    /// <summary>Records written at the moment of the event.</summary>
    Live = 0,

    /// <summary>Records reconstructed after the fact; context and volume only (SCHEMA.md §7).</summary>
    Backfilled = 1
}
