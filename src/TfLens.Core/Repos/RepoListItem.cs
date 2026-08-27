using TfLens.Core.Contracts;

namespace TfLens.Core.Repos;

/// <summary>
/// One row of the Repos grid: a connected repository joined to its sync bookkeeping (BRD-98).
/// </summary>
/// <remarks>
/// The join lives here rather than in the page so the record counts the grid shows come from the same
/// user-scoped read as the repository row itself — <c>UserId</c> is a parameter of both store calls,
/// so a row for another user cannot reach this shape (ADR-013). <see cref="Sync"/> is <c>null</c>
/// until the repository's first sync completes, which is what <see cref="Status"/> reports as
/// <see cref="RepoSyncStatuses.Pending"/>.
/// </remarks>
/// <param name="Repo">The connected repository row.</param>
/// <param name="Sync">The repository's sync state, or <c>null</c> when it has never synced.</param>
public sealed record RepoListItem(UserRepo Repo, SyncState? Sync)
{
    /// <summary>Records stored across every stream for this user and repository.</summary>
    public int RecordCount => Sync is null
        ? 0
        : Sync.RunsCount + Sync.GatesCount + Sync.SessionsCount + Sync.CommitsCount + Sync.EventsCount;

    /// <summary>The status badge text: pending, error or synced.</summary>
    public string Status => Sync is null
        ? RepoSyncStatuses.Pending
        : Sync.LastError is not null
            ? RepoSyncStatuses.Error
            : RepoSyncStatuses.Synced;

    /// <summary>ISO-8601 timestamp of the last completed sync attempt, or <c>null</c>.</summary>
    public string? LastSyncTs => Sync?.LastSyncTs;

    /// <summary>Redacted message from the last failed sync, or <c>null</c> when the last sync succeeded.</summary>
    public string? LastError => Sync?.LastError;

    /// <summary>Newest telemetry SHA synced, or <c>null</c> when the repository has never synced.</summary>
    public string? LastSha => Sync?.LastSha;
}

/// <summary>The status vocabulary the Repos grid renders as a badge.</summary>
public static class RepoSyncStatuses
{
    /// <summary>Connected, but the first sync has not completed yet.</summary>
    public const string Pending = "pending";

    /// <summary>The last sync completed without an error.</summary>
    public const string Synced = "synced";

    /// <summary>The last sync failed; the reason is in <see cref="RepoListItem.LastError"/>.</summary>
    public const string Error = "error";
}
