using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// An in-memory <see cref="ITelemetryStore"/> that records what the Playbook adapter wrote through it.
/// </summary>
/// <remarks>
/// Deliberately minimal: only the Playbook members do anything. The rest throw, so a test that
/// accidentally exercises a TechieFlow read through the Playbook path fails loudly rather than passing
/// on an empty list.
/// </remarks>
public sealed class CapturingStore : ITelemetryStore
{
    /// <summary>Every parse result handed to <see cref="UpsertAsync"/>, in order.</summary>
    public List<ParseResult> Parsed { get; } = [];

    /// <summary>The Playbook rows held, as the adapter wrote them.</summary>
    public List<PbEventRecord> PbEvents { get; } = [];

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken aCancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken aCancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<int> UpsertAsync(ParseResult aParsed, CancellationToken aCancellationToken = default)
    {
        Parsed.Add(aParsed);
        PbEvents.AddRange(aParsed.PbEvents);
        return Task.FromResult(aParsed.RecordCount);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PbEventRecord>> ReadPbEventsAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PbEventRecord>>(
            PbEvents.Where(aE => aE.UserId == aUserId && (aRepo is null || aE.Repo == aRepo)).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<RunRecord>> ReadRunsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(NotPlaybook);

    /// <inheritdoc />
    public Task<IReadOnlyList<GateRecord>> ReadGatesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(NotPlaybook);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> ReadSessionsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(NotPlaybook);

    /// <inheritdoc />
    public Task<IReadOnlyList<CommitRecord>> ReadCommitsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(NotPlaybook);

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncState>> ReadSyncStateAsync(
        int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SyncState>>([]);

    /// <inheritdoc />
    public Task WriteSyncStateAsync(SyncState aState, CancellationToken aCancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadUserReposAsync(
        int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserRepo>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadAllUserReposAsync(CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserRepo>>([]);

    /// <inheritdoc />
    public Task WriteUserRepoAsync(UserRepo aRepo, CancellationToken aCancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteRepoDataAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<RebuildReport> RebuildAsync(
        int? aUserId = null, CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(NotPlaybook);

    /// <summary>The message a non-Playbook read fails with.</summary>
    private const string NotPlaybook = "This double serves the Playbook path only.";
}
