using Microsoft.Extensions.DependencyInjection;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Services.Ui;

/// <summary>
/// The per-circuit view of the signed-in user's repositories and sync bookkeeping that the shell shows.
/// </summary>
/// <remarks>
/// The sidebar repo badge, the header's Framework-switch counts, the header's last-sync badge and the Repos
/// page all read the same two lists; holding them here means connecting or removing a repository updates
/// every one of them in place, without a full page reload (REQ-UI-007, REQ-UI-012). The data services are
/// resolved lazily rather than injected so the shell still renders while a parallel cluster's registration
/// is still landing — a missing service degrades to an empty workspace, never to a startup failure.
/// </remarks>
public sealed class ShellState
{
    private readonly IServiceProvider objServices;

    /// <summary>
    /// Creates the state holder.
    /// </summary>
    /// <param name="aServices">The circuit's scoped service provider, used to resolve the data services lazily.</param>
    public ShellState(IServiceProvider aServices)
    {
        objServices = aServices;
    }

    /// <summary>Raised whenever the loaded data changes, so the header and sidebar re-render.</summary>
    public event Action? Changed;

    /// <summary>The signed-in user's connected repositories, newest load wins.</summary>
    public IReadOnlyList<UserRepo> Repos { get; private set; } = [];

    /// <summary>The sync bookkeeping row per connected repository.</summary>
    public IReadOnlyList<SyncState> SyncStates { get; private set; } = [];

    /// <summary>True once a load has completed, so the shell can tell "no repos" from "not read yet".</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The message from the last failed load, or <c>null</c> when the last load succeeded.</summary>
    public string? LoadError { get; private set; }

    /// <summary>The user the loaded data belongs to.</summary>
    public int? UserId { get; private set; }

    /// <summary>How many repositories the user has connected — the sidebar badge.</summary>
    public int RepoCount => Repos.Count;

    /// <summary>
    /// How many of the user's repositories belong to one framework — the Framework-switch trigger badges.
    /// </summary>
    /// <param name="aFramework">One of <see cref="FrameworkNames.TechieFlow"/> or <see cref="FrameworkNames.Playbook"/>.</param>
    /// <returns>The count for that framework.</returns>
    public int RepoCountFor(string aFramework) =>
        Repos.Count(aRepo => string.Equals(aRepo.Framework, aFramework, StringComparison.OrdinalIgnoreCase));

    /// <summary>Records stored across every stream of every connected repository.</summary>
    public int RecordCount =>
        SyncStates.Sum(aState =>
            aState.RunsCount + aState.GatesCount + aState.SessionsCount + aState.CommitsCount + aState.EventsCount);

    /// <summary>The newest successful sync across the user's repositories, or <c>null</c> when nothing has synced.</summary>
    public DateTimeOffset? LastSyncUtc =>
        SyncStates
            .Where(aState => aState.LastError is null)
            .Select(aState => RelativeTime.Parse(aState.LastSyncTs))
            .Where(aWhen => aWhen is not null)
            .DefaultIfEmpty(null)
            .Max();

    /// <summary>
    /// Finds one repository's sync bookkeeping.
    /// </summary>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <returns>The row, or <c>null</c> when the repository has never been synced.</returns>
    public SyncState? SyncStateFor(string aRepo) =>
        SyncStates.FirstOrDefault(aState => string.Equals(aState.Repo, aRepo, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Loads the user's workspace once per circuit.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the data is present.</returns>
    public async Task EnsureLoadedAsync(int aUserId, CancellationToken aCancellationToken = default)
    {
        if (IsLoaded && UserId == aUserId)
        {
            return;
        }

        await RefreshAsync(aUserId, aCancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-reads the user's repositories and sync bookkeeping and notifies every subscriber.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes once the shell has the fresh data.</returns>
    public async Task RefreshAsync(int aUserId, CancellationToken aCancellationToken = default)
    {
        UserId = aUserId;

        try
        {
            var vRegistry = objServices.GetService<IRepoRegistry>();
            var vStore = objServices.GetService<ITelemetryStore>();

            Repos = vRegistry is null
                ? []
                : await vRegistry.ListAsync(aUserId, aCancellationToken).ConfigureAwait(false);

            SyncStates = vStore is null
                ? []
                : await vStore.ReadSyncStateAsync(aUserId, aCancellationToken).ConfigureAwait(false);

            LoadError = null;
        }
        catch (Exception vEx)
        {
            Repos = [];
            SyncStates = [];
            LoadError = vEx.Message;
        }

        IsLoaded = true;
        NotifyChanged();
    }

    /// <summary>
    /// Tells every subscriber the shell data changed, without re-reading it.
    /// </summary>
    public void NotifyChanged() => Changed?.Invoke();
}
