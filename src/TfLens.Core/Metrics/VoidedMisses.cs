using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The miss stream with its withdrawals applied, and what that removed (SCHEMA.md §5.5.11, REQ-FN-143).
/// </summary>
/// <param name="Misses">Every <c>miss</c> no void names.</param>
/// <param name="Fixes">Every <c>miss-fix</c> whose miss was not withdrawn.</param>
/// <param name="Amends">Every <c>miss-amend</c> whose miss was not withdrawn.</param>
/// <param name="Voided">
/// One line per withdrawn miss, <c>"&lt;miss_id&gt; — &lt;reason&gt;"</c> — parity key
/// <c>misses_voided</c>; its count is <c>misses_voided_n</c>.
/// </param>
/// <param name="OrphanedN">
/// Voids naming no miss on their repository's stream — parity key <c>miss_voids_orphaned_n</c>.
/// </param>
public sealed record VoidedMisses(
    IReadOnlyList<MissRecord> Misses,
    IReadOnlyList<MissFixRecord> Fixes,
    IReadOnlyList<MissAmendRecord> Amends,
    IReadOnlyList<string> Voided,
    int OrphanedN)
{
    /// <summary>Misses a void took out of every figure — parity key <c>misses_voided_n</c>.</summary>
    public int VoidedN => Voided.Count;

    /// <summary>
    /// Stamps the withdrawal counts onto a miss analysis computed over <see cref="Misses"/>.
    /// </summary>
    /// <param name="aAnalysis">The analysis computed from this result's kept records.</param>
    /// <returns>The same analysis carrying the three withdrawal figures.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aAnalysis"/> is <c>null</c>.</exception>
    public MissAnalysis Stamp(MissAnalysis aAnalysis)
    {
        ArgumentNullException.ThrowIfNull(aAnalysis);

        return aAnalysis with
        {
            MissesVoidedN = VoidedN,
            MissesVoided = Voided,
            MissVoidsOrphanedN = OrphanedN
        };
    }
}
