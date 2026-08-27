using TfLens.Core.Contracts;

namespace TfLens.Core.Abstractions;

/// <summary>
/// Runs one sync pass over connected repositories.
/// </summary>
/// <remarks>
/// The background poller, the header's <c>Sync now</c> button and the <c>sync</c> command verb all
/// enter through this one method, so there is a single code path to reason about. Failures are
/// contained per repository: one repository's 401 never stops the others (BRD-15).
/// </remarks>
public interface IRepoSyncRunner
{
    /// <summary>
    /// Syncs a user's repositories, or every user's.
    /// </summary>
    /// <param name="aUserId">One user — as <c>Sync now</c> passes — or <c>null</c> for the poller's pass over all users (BRD-103).</param>
    /// <param name="aCancellationToken">Cancels the pass.</param>
    /// <returns>One line per repository attempted, with the pass's start and end timestamps.</returns>
    Task<SyncReport> SyncAsync(int? aUserId = null, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Syncs exactly one repository.
    /// </summary>
    /// <param name="aUserId">The AppManager user who connected it.</param>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <param name="aCancellationToken">Cancels the sync.</param>
    /// <returns>What happened to that repository.</returns>
    Task<RepoSyncResult> SyncRepoAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default);
}
