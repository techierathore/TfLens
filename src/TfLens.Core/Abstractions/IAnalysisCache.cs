using TfLens.Core.Contracts;

namespace TfLens.Core.Abstractions;

/// <summary>
/// Memoises the computed <see cref="AnalysisResult"/> and exposes the hook that throws it away.
/// </summary>
/// <remarks>
/// Every figure is arithmetic done at request time over the stream tables, never a stored derived
/// value (REQ-FN-046, SCHEMA.md §8) — so the only safe place to keep one is process memory, keyed by
/// the data it was computed from. The key is <c>(userId, framework, sync version)</c>: new data
/// produces a new version and therefore a new entry, and <see cref="Invalidate"/> is the hook a
/// completed sync or rebuild calls to drop what a user has cached (REQ-FN-026).
/// </remarks>
public interface IAnalysisCache
{
    /// <summary>
    /// Returns the memoised analysis for a key, computing it once if it is not already held.
    /// </summary>
    /// <param name="aUserId">The AppManager user id — part of the key, so no user can read another's figures (ADR-013).</param>
    /// <param name="aFramework">The provenance axis — part of the key, so frameworks never share an entry (ADR-016).</param>
    /// <param name="aSyncVersion">An opaque stamp of the data the analysis would be computed from.</param>
    /// <param name="aFactory">Computes the analysis when the entry is absent.</param>
    /// <param name="aCancellationToken">Cancels the computation.</param>
    /// <returns>The cached or freshly computed analysis.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aFactory"/> is <c>null</c>.</exception>
    Task<AnalysisResult> GetOrCreateAsync(
        int aUserId,
        string aFramework,
        string aSyncVersion,
        Func<CancellationToken, Task<AnalysisResult>> aFactory,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Drops every memoised analysis for one user.
    /// </summary>
    /// <param name="aUserId">The AppManager user id whose entries are now stale.</param>
    /// <remarks>Called on a completed sync or rebuild; safe to call when nothing is cached.</remarks>
    void Invalidate(int aUserId);

    /// <summary>
    /// Drops every memoised analysis for every user.
    /// </summary>
    /// <remarks>Called after a rebuild that replayed the whole raw archive rather than one user's.</remarks>
    void InvalidateAll();
}
