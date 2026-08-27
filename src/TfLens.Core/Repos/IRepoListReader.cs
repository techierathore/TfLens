namespace TfLens.Core.Repos;

/// <summary>
/// The Repos grid's read: one user's repositories with their per-repo record counts (REQ-FN-013).
/// </summary>
/// <remarks>
/// <see cref="Abstractions.IRepoRegistry.ListAsync"/> returns the stored rows alone; the grid also
/// needs the counts and the sync status, which live in <c>"SyncState"</c>. Rather than widen the
/// shared <c>UserRepo</c> shape — which the parser and the poller also use — the join is exposed
/// here, implemented by the same <see cref="RepoRegistry"/>. Like every other read in this cluster
/// the user id is a mandatory parameter, not an optional filter (ADR-013).
/// </remarks>
public interface IRepoListReader
{
    /// <summary>
    /// Lists one user's repositories with their record counts and sync status.
    /// </summary>
    /// <param name="aUserId">The AppManager user id; the read cannot be issued without one.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>One item per connected repository, in connection order; never another user's.</returns>
    Task<IReadOnlyList<RepoListItem>> ListWithCountsAsync(int aUserId, CancellationToken aCancellationToken = default);
}
