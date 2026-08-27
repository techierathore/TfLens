using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The in-process memoisation of <see cref="AnalysisResult"/>, backed by <see cref="IMemoryCache"/>.
/// </summary>
/// <remarks>
/// Entries carry a per-user expiration token so <see cref="Invalidate"/> can drop every framework and
/// every sync version a user holds without enumerating the cache — the pattern
/// <see cref="IMemoryCache"/> supports for keys the caller cannot list. Nothing derived is written
/// anywhere but here, and this is discarded, never persisted (REQ-FN-046).
/// </remarks>
public sealed class MemoryAnalysisCache : IAnalysisCache, IDisposable
{
    private const string KeyPrefix = "TfLens.Analysis";
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(12);

    private readonly IMemoryCache objCache;
    private readonly ConcurrentDictionary<int, CancellationTokenSource> objUserTokens = new();
    private bool objIsDisposed;

    /// <summary>
    /// Creates the cache over the host's memory cache.
    /// </summary>
    /// <param name="aCache">The shared memory cache.</param>
    /// <exception cref="ArgumentNullException"><paramref name="aCache"/> is <c>null</c>.</exception>
    public MemoryAnalysisCache(IMemoryCache aCache)
    {
        ArgumentNullException.ThrowIfNull(aCache);
        objCache = aCache;
    }

    /// <inheritdoc />
    public async Task<AnalysisResult> GetOrCreateAsync(
        int aUserId,
        string aFramework,
        string aSyncVersion,
        Func<CancellationToken, Task<AnalysisResult>> aFactory,
        CancellationToken aCancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aFramework);
        ArgumentNullException.ThrowIfNull(aFactory);

        var vKey = $"{KeyPrefix}|{aUserId}|{aFramework}|{aSyncVersion}";
        if (objCache.TryGetValue(vKey, out AnalysisResult? vCached) && vCached is not null)
        {
            return vCached;
        }

        var vResult = await aFactory(aCancellationToken).ConfigureAwait(false);

        using var vEntry = objCache.CreateEntry(vKey);
        vEntry.AbsoluteExpirationRelativeToNow = EntryLifetime;
        vEntry.AddExpirationToken(new CancellationChangeToken(TokenFor(aUserId).Token));
        vEntry.Value = vResult;

        return vResult;
    }

    /// <inheritdoc />
    public void Invalidate(int aUserId)
    {
        if (objUserTokens.TryRemove(aUserId, out var vSource))
        {
            vSource.Cancel();
            vSource.Dispose();
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        foreach (var vUserId in objUserTokens.Keys.ToList())
        {
            Invalidate(vUserId);
        }
    }

    /// <summary>Cancels and releases every outstanding per-user expiration token.</summary>
    public void Dispose()
    {
        if (objIsDisposed)
        {
            return;
        }

        objIsDisposed = true;
        InvalidateAll();
    }

    /// <summary>
    /// The expiration token every entry for one user shares.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <returns>The user's current token source, created on first use.</returns>
    private CancellationTokenSource TokenFor(int aUserId) =>
        objUserTokens.GetOrAdd(aUserId, static aKey => new CancellationTokenSource());
}
