using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Repos;

/// <summary>
/// Records the first sync the registry queues, so a test can prove connect handed the repository on.
/// </summary>
/// <remarks>
/// The registry queues the first sync on a background task so the Connect dialog is not held open for
/// the length of a GitHub round trip; <see cref="FirstCall"/> lets a test await that hand-off instead
/// of sleeping.
/// </remarks>
public sealed class StubRepoSyncRunner : IRepoSyncRunner
{
    private readonly TaskCompletionSource objFirstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> objSynced = [];

    /// <summary>Completes as soon as the registry asks for a repository to be synced.</summary>
    public Task FirstCall => objFirstCall.Task;

    /// <summary>Every <c>userId:repo</c> the registry asked to sync, in order.</summary>
    public IReadOnlyList<string> Synced
    {
        get
        {
            lock (objSynced)
            {
                return objSynced.ToList();
            }
        }
    }

    /// <inheritdoc />
    public Task<RepoSyncResult> SyncRepoAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default)
    {
        lock (objSynced)
        {
            objSynced.Add($"{aUserId}:{aRepo}");
        }

        objFirstCall.TrySetResult();
        return Task.FromResult(new RepoSyncResult(aRepo, SyncOutcome.Skipped, null, 0, null));
    }

    /// <inheritdoc />
    public Task<SyncReport> SyncAsync(int? aUserId = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The registry never runs a whole pass; that is the poller's job.");
}
