using TfLens.Core.Abstractions;

namespace TfLens.Services.Sync;

/// <summary>
/// Drops a user's memoised analysis after a sync has changed the rows underneath it.
/// </summary>
/// <remarks>
/// <para>
/// BRD-18 / REQ-FN-026 — a report page opened after a sync must recompute rather than serve the
/// pre-sync figures, and the invalidation is scoped to the user whose rows moved.
/// </para>
/// <para>
/// The cache is an optional dependency: the constructor's default keeps the sync path working in a
/// build or a test where no cache is registered, and an invalidation failure is logged rather than
/// thrown, because the rows are already written and the worst outcome of a missed drop is a stale
/// figure — never a lost sync.
/// </para>
/// </remarks>
public sealed class AnalysisCacheInvalidator
{
    private readonly ILogger<AnalysisCacheInvalidator> objLogger;
    private readonly IAnalysisCache? objCache;

    /// <summary>
    /// Creates the invalidator.
    /// </summary>
    /// <param name="aLogger">Logger; it records the user id and whether a cache was found.</param>
    /// <param name="aCache">The memoisation to drop, when one is registered.</param>
    public AnalysisCacheInvalidator(ILogger<AnalysisCacheInvalidator> aLogger, IAnalysisCache? aCache = null)
    {
        objLogger = aLogger;
        objCache = aCache;
    }

    /// <summary>True when a cache is present for this invalidator to drop.</summary>
    public bool HasCache => objCache is not null;

    /// <summary>
    /// Invalidates one user's memoised analysis.
    /// </summary>
    /// <param name="aUserId">The user whose figures must be recomputed.</param>
    /// <returns><c>true</c> when a cache was found and invalidated.</returns>
    public bool Invalidate(int aUserId)
    {
        if (objCache is null)
        {
            objLogger.LogDebug("No analysis cache registered; nothing to invalidate for user {UserId}", aUserId);
            return false;
        }

        try
        {
            objCache.Invalidate(aUserId);
            objLogger.LogInformation("Analysis cache invalidated for user {UserId}", aUserId);
            return true;
        }
        catch (Exception vEx)
        {
            objLogger.LogWarning(vEx, "Analysis cache invalidation failed for user {UserId}", aUserId);
            return false;
        }
    }

    /// <summary>
    /// Invalidates every user's memoised analysis, as a whole-estate rebuild requires.
    /// </summary>
    /// <returns><c>true</c> when a cache was found and invalidated.</returns>
    public bool InvalidateAll()
    {
        if (objCache is null)
        {
            objLogger.LogDebug("No analysis cache registered; nothing to invalidate.");
            return false;
        }

        try
        {
            objCache.InvalidateAll();
            objLogger.LogInformation("Analysis cache invalidated for every user.");
            return true;
        }
        catch (Exception vEx)
        {
            objLogger.LogWarning(vEx, "Analysis cache invalidation failed.");
            return false;
        }
    }
}
