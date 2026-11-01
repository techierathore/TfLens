using System.Text;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Memoises <see cref="MetricsEngine"/> per <c>(userId, framework, repository, sync version)</c>.
/// </summary>
/// <remarks>
/// The decorator holds the caching so <see cref="MetricsEngine"/> stays a pure port of
/// <c>analyse()</c> with nothing between it and the reference. The sync version is built from the
/// user's <c>SyncState</c> rows — the SHA and timestamp each repository was last synced at — so new
/// telemetry produces a new key on its own, and <see cref="IAnalysisCache.Invalidate"/> covers the
/// cases a version stamp cannot see, such as a rebuild that replays the same SHAs (REQ-FN-026).
/// </remarks>
public sealed class CachingMetricsEngine : IMetricsEngine
{
    private readonly MetricsEngine objEngine;
    private readonly IAnalysisCache objCache;
    private readonly ITelemetryStore objStore;

    /// <summary>
    /// Creates the decorator.
    /// </summary>
    /// <param name="aEngine">The engine that does the arithmetic.</param>
    /// <param name="aCache">The memoisation the result is held in.</param>
    /// <param name="aStore">The store the sync version is read from.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public CachingMetricsEngine(MetricsEngine aEngine, IAnalysisCache aCache, ITelemetryStore aStore)
    {
        ArgumentNullException.ThrowIfNull(aEngine);
        ArgumentNullException.ThrowIfNull(aCache);
        ArgumentNullException.ThrowIfNull(aStore);

        objEngine = aEngine;
        objCache = aCache;
        objStore = aStore;
    }

    /// <inheritdoc />
    public async Task<AnalysisResult> AnalyseAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aFramework);

        var vSyncVersion = await SyncVersionAsync(aUserId, aCancellationToken).ConfigureAwait(false);

        // The repository is part of the key (REQ-UI-077): a one-repo analysis and the all-repos analysis
        // are different answers and must never be served for each other. The all-repos key is unchanged.
        var vKey = aRepo is null ? vSyncVersion : $"repo={aRepo}|{vSyncVersion}";

        return await objCache.GetOrCreateAsync(
            aUserId,
            aFramework,
            vKey,
            aToken => objEngine.AnalyseAsync(aUserId, aFramework, aRepo, aToken),
            aCancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stamps the data the analysis would be computed from.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aCancellationToken">Cancels the read.</param>
    /// <returns>A stable string that changes whenever any of the user's repositories syncs new content.</returns>
    private async Task<string> SyncVersionAsync(int aUserId, CancellationToken aCancellationToken)
    {
        var vStates = await objStore.ReadSyncStateAsync(aUserId, aCancellationToken).ConfigureAwait(false);
        var vVersion = new StringBuilder();

        foreach (var vState in vStates.OrderBy(aState => aState.Repo, StringComparer.Ordinal))
        {
            vVersion.Append(vState.Repo).Append(':')
                .Append(vState.LastSha ?? "-").Append(':')
                .Append(vState.LastSyncTs ?? "-").Append(';');
        }

        return vVersion.Length == 0 ? "empty" : vVersion.ToString();
    }
}
