using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Repos;

/// <summary>
/// The per-user list of connected public GitHub repositories — validate, connect, list, remove.
/// </summary>
/// <remarks>
/// <para>
/// Every method takes <c>aUserId</c> and passes it into every store call, into the raw-archive path
/// and into every duplicate check, so there is no overload through which one user could name, read or
/// delete another user's repository (BRD-102, ADR-013). "Not yours" and "not connected" are therefore
/// the same answer, which is also what a caller probing for another user's repositories sees.
/// </para>
/// <para>
/// The registry never writes to GitHub and never polls: it uses the GET-only
/// <see cref="IGitHubStreamFetcher"/> for its three connect-time checks, and hands the first sync to
/// <see cref="IRepoSyncRunner"/>, which is resolved lazily so the two registrations cannot form a
/// cycle at container-build time (BRD-16, BRD-103).
/// </para>
/// </remarks>
public sealed class RepoRegistry : IRepoRegistry, IRepoListReader
{
    /// <summary>The exact user-facing refusal for a private repository (BRD-100, REQ-UI-012).</summary>
    public const string PrivateRepoMessage = "Private repos aren't supported in this release";

    private readonly ITelemetryStore objStore;
    private readonly IGitHubStreamFetcher objFetcher;
    private readonly Func<IRepoSyncRunner?> objSyncRunnerFactory;
    private readonly TfLensOptions objOptions;
    private readonly ILogger<RepoRegistry> objLogger;

    /// <summary>
    /// Creates the registry.
    /// </summary>
    /// <param name="aStore">The store; every call made through it carries the user id.</param>
    /// <param name="aFetcher">The read-only GitHub client used for the three connect-time checks.</param>
    /// <param name="aSyncRunnerFactory">Resolves the sync runner at call time, so the registration cannot be circular; may return <c>null</c> when no runner is registered.</param>
    /// <param name="aOptions">Configuration, for <see cref="TfLensOptions.RawPath"/>.</param>
    /// <param name="aLogger">Logger; ids, counts and statuses only — never a token or a stream body.</param>
    public RepoRegistry(
        ITelemetryStore aStore,
        IGitHubStreamFetcher aFetcher,
        Func<IRepoSyncRunner?> aSyncRunnerFactory,
        IOptions<TfLensOptions> aOptions,
        ILogger<RepoRegistry> aLogger)
    {
        objStore = aStore;
        objFetcher = aFetcher;
        objSyncRunnerFactory = aSyncRunnerFactory;
        objOptions = aOptions.Value;
        objLogger = aLogger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRepo>> ListAsync(int aUserId, CancellationToken aCancellationToken = default) =>
        await objStore.ReadUserReposAsync(aUserId, aCancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepoListItem>> ListWithCountsAsync(
        int aUserId,
        CancellationToken aCancellationToken = default)
    {
        var vRepos = await objStore.ReadUserReposAsync(aUserId, aCancellationToken).ConfigureAwait(false);
        var vStates = await objStore.ReadSyncStateAsync(aUserId, aCancellationToken).ConfigureAwait(false);

        return vRepos
            .Select(aRepo => new RepoListItem(aRepo, FindState(vStates, aRepo.Repo)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<RepoValidation> ValidateAsync(
        int aUserId,
        string aInput,
        string? aBranch = null,
        CancellationToken aCancellationToken = default) =>
        await ValidateAsync(aUserId, aInput, aBranch, null, aCancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Checks a candidate repository, optionally forcing which framework's telemetry to look for.
    /// </summary>
    /// <param name="aUserId">The user connecting it, so a duplicate can be reported.</param>
    /// <param name="aInput">A GitHub URL or <c>owner/name</c>.</param>
    /// <param name="aBranch">Branch to read telemetry from; the default branch when omitted.</param>
    /// <param name="aKind">The Connect dialog's kind override — <c>techieflow</c> or <c>playbook</c> — or <c>null</c> to auto-detect.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>What the checks found — existence, visibility and telemetry path.</returns>
    /// <remarks>
    /// The shared <see cref="IRepoRegistry"/> carries no kind parameter yet, so the override REQ-FN-014
    /// and REQ-UI-012 call for lives on this overload until the contract gains it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aKind"/> names no known framework.</exception>
    public async Task<RepoValidation> ValidateAsync(
        int aUserId,
        string aInput,
        string? aBranch,
        string? aKind,
        CancellationToken aCancellationToken = default)
    {
        var vChecked = await CheckAsync(aUserId, aInput, aBranch, aKind, aCancellationToken).ConfigureAwait(false);
        return vChecked.Validation;
    }

    /// <inheritdoc />
    public async Task<UserRepo> ConnectAsync(
        int aUserId,
        string aInput,
        string? aBranch = null,
        CancellationToken aCancellationToken = default) =>
        await ConnectAsync(aUserId, aInput, aBranch, null, aCancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Connects a repository, optionally forcing which framework's telemetry to look for.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aInput">A GitHub URL or <c>owner/name</c>.</param>
    /// <param name="aBranch">Branch to read telemetry from; the default branch when omitted.</param>
    /// <param name="aKind">The Connect dialog's kind override, or <c>null</c> to auto-detect.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The stored row.</returns>
    /// <remarks>The validation is re-run here; a client-side result is never trusted.</remarks>
    /// <exception cref="InvalidOperationException">The repository is private, missing, carries no telemetry, or is already connected by this user.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aKind"/> names no known framework.</exception>
    public async Task<UserRepo> ConnectAsync(
        int aUserId,
        string aInput,
        string? aBranch,
        string? aKind,
        CancellationToken aCancellationToken = default)
    {
        var vChecked = await CheckAsync(aUserId, aInput, aBranch, aKind, aCancellationToken).ConfigureAwait(false);
        if (!vChecked.Validation.IsConnectable || vChecked.Info is null)
        {
            throw new InvalidOperationException(RefusalOf(vChecked.Validation));
        }

        var vRepo = ToUserRepo(aUserId, vChecked.Info, vChecked.Validation);
        await objStore.WriteUserRepoAsync(vRepo, aCancellationToken).ConfigureAwait(false);
        objLogger.LogInformation(
            "Connected repository {Repo} ({Framework}, branch {Branch}) for user {UserId}.",
            vRepo.Repo,
            vRepo.Framework,
            vRepo.Branch,
            aUserId);

        QueueFirstSync(aUserId, vRepo.Repo);
        return vRepo;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default)
    {
        if (!RepoInputParser.TryParse(aRepo, out var vRef, out var vError))
        {
            throw new InvalidOperationException(vError!);
        }

        var vRows = await objStore.ReadUserReposAsync(aUserId, aCancellationToken).ConfigureAwait(false);
        var vRow = vRows.FirstOrDefault(aRow => string.Equals(aRow.Repo, vRef!.Repo, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{vRef!.Repo} is not connected to your account.");

        await objStore.DeleteRepoDataAsync(aUserId, vRow.Repo, aCancellationToken).ConfigureAwait(false);
        DeleteRawArchive(aUserId, new RepoRef(vRow.Owner, vRow.Name));
        objLogger.LogInformation("Removed repository {Repo} and its archive for user {UserId}.", vRow.Repo, aUserId);
    }

    /// <summary>
    /// Runs the three connect-time checks once and returns both the dialog's view of them and the
    /// GitHub metadata the connect path needs.
    /// </summary>
    /// <param name="aUserId">The user the duplicate check is scoped to.</param>
    /// <param name="aInput">A GitHub URL or <c>owner/name</c>.</param>
    /// <param name="aBranch">The caller's branch, or <c>null</c> for the repository's default.</param>
    /// <param name="aKind">The caller's framework override, or <c>null</c> to auto-detect.</param>
    /// <param name="aCancellationToken">Cancels the checks.</param>
    /// <returns>The validation, plus the metadata when GitHub answered.</returns>
    /// <remarks>
    /// Connect re-enters here rather than trusting a validation the browser ran earlier: a client-side
    /// result can be stale, replayed, or simply fabricated.
    /// </remarks>
    private async Task<(RepoValidation Validation, GitHubRepoInfo? Info)> CheckAsync(
        int aUserId,
        string aInput,
        string? aBranch,
        string? aKind,
        CancellationToken aCancellationToken)
    {
        if (!RepoInputParser.TryParse(aInput, out var vRef, out var vError))
        {
            return (new RepoValidation(false, false, null, null, null, false, vError), null);
        }

        var vInfo = await objFetcher.GetRepoAsync(vRef!.Owner, vRef.Name, aCancellationToken).ConfigureAwait(false);
        if (vInfo is null)
        {
            var vMissing = $"{vRef.Repo} was not found on GitHub. Check the spelling — only public repositories are visible.";
            return (new RepoValidation(false, false, null, null, null, false, vMissing), null);
        }

        if (vInfo.IsPrivate)
        {
            return (new RepoValidation(true, false, null, null, null, false, PrivateRepoMessage), vInfo);
        }

        return (await DetectAsync(aUserId, vInfo, aBranch, aKind, aCancellationToken).ConfigureAwait(false), vInfo);
    }

    /// <summary>
    /// Resolves the branch, probes for a telemetry directory and reports whether this user already
    /// has the repository.
    /// </summary>
    /// <param name="aUserId">The user the duplicate check is scoped to.</param>
    /// <param name="aInfo">GitHub's metadata for a repository already known to exist and be public.</param>
    /// <param name="aBranch">The caller's branch, or <c>null</c> for the repository's default.</param>
    /// <param name="aKind">The caller's framework override, or <c>null</c> to auto-detect.</param>
    /// <param name="aCancellationToken">Cancels the probes.</param>
    /// <returns>The completed validation.</returns>
    private async Task<RepoValidation> DetectAsync(
        int aUserId,
        GitHubRepoInfo aInfo,
        string? aBranch,
        string? aKind,
        CancellationToken aCancellationToken)
    {
        var vBranch = string.IsNullOrWhiteSpace(aBranch) ? aInfo.DefaultBranch : aBranch.Trim();
        var vRepo = $"{aInfo.Owner}/{aInfo.Name}";
        var vIsConnected = await IsConnectedAsync(aUserId, vRepo, aCancellationToken).ConfigureAwait(false);
        var vFramework = await DetectFrameworkAsync(aInfo, vBranch, aKind, aCancellationToken).ConfigureAwait(false);

        if (vFramework is null)
        {
            return new RepoValidation(true, true, null, null, vBranch, vIsConnected, NoTelemetryMessage(vBranch, aKind));
        }

        var vDuplicate = vIsConnected ? $"{vRepo} is already connected to your account." : null;
        return new RepoValidation(
            true,
            true,
            FrameworkNames.TelemetryPath(vFramework),
            vFramework,
            vBranch,
            vIsConnected,
            vDuplicate);
    }

    /// <summary>
    /// Probes each framework's telemetry directory in turn and returns the first that resolves.
    /// </summary>
    /// <param name="aInfo">GitHub's metadata for the repository.</param>
    /// <param name="aBranch">The branch to probe at.</param>
    /// <param name="aKind">The caller's framework override, or <c>null</c> to auto-detect.</param>
    /// <param name="aCancellationToken">Cancels the probes.</param>
    /// <returns><c>techieflow</c>, <c>playbook</c>, or <c>null</c> when no candidate path exists.</returns>
    /// <remarks>
    /// TechieFlow is probed first, so a repository carrying both directories is classified as the
    /// framework whose telemetry TfLens can report on in full today (ADR-016). An override narrows the
    /// probe to that one framework, so a repository carrying both can still be connected as the other.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aKind"/> names no known framework.</exception>
    private async Task<string?> DetectFrameworkAsync(
        GitHubRepoInfo aInfo,
        string aBranch,
        string? aKind,
        CancellationToken aCancellationToken)
    {
        foreach (var vFramework in Candidates(aKind))
        {
            var vExists = await objFetcher.PathExistsAsync(
                aInfo.Owner,
                aInfo.Name,
                FrameworkNames.TelemetryPath(vFramework),
                aBranch,
                aCancellationToken).ConfigureAwait(false);

            if (vExists)
            {
                return vFramework;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the frameworks to probe for: both, in display order, or only the one the caller forced.
    /// </summary>
    /// <param name="aKind">The caller's framework override, or <c>null</c> to auto-detect.</param>
    /// <returns>The candidate frameworks in probe order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aKind"/> names no known framework.</exception>
    private static IReadOnlyList<string> Candidates(string? aKind)
    {
        if (string.IsNullOrWhiteSpace(aKind))
        {
            return FrameworkNames.All;
        }

        var vKind = aKind.Trim().ToLowerInvariant();
        return FrameworkNames.All.Contains(vKind)
            ? [vKind]
            : throw new ArgumentOutOfRangeException(nameof(aKind), aKind, "Unknown framework.");
    }

    /// <summary>
    /// Builds the refusal shown when no telemetry directory was found.
    /// </summary>
    /// <param name="aBranch">The branch that was probed.</param>
    /// <param name="aKind">The caller's framework override, when they forced one.</param>
    /// <returns>The user-facing reason, naming exactly the paths that were looked for.</returns>
    private static string NoTelemetryMessage(string aBranch, string? aKind) =>
        string.IsNullOrWhiteSpace(aKind)
            ? $"No telemetry found on branch '{aBranch}' — neither docs/metrics (TechieFlow) " +
              "nor verification/telemetry (Playbook) exists in this repository."
            : $"No telemetry found on branch '{aBranch}' — {FrameworkNames.TelemetryPath(aKind.Trim().ToLowerInvariant())} " +
              "does not exist in this repository.";

    /// <summary>
    /// Tests whether one user has already connected a repository (BRD-104).
    /// </summary>
    /// <param name="aUserId">The user to look within — the read cannot see any other user's rows.</param>
    /// <param name="aRepo"><c>owner/name</c> to look for.</param>
    /// <param name="aCancellationToken">Cancels the read.</param>
    /// <returns><c>true</c> when this user already has the repository.</returns>
    private async Task<bool> IsConnectedAsync(int aUserId, string aRepo, CancellationToken aCancellationToken)
    {
        var vRows = await objStore.ReadUserReposAsync(aUserId, aCancellationToken).ConfigureAwait(false);
        return vRows.Any(aRow => string.Equals(aRow.Repo, aRepo, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Starts the repository's first sync without holding up the dialog.
    /// </summary>
    /// <param name="aUserId">The user whose repository to sync.</param>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <remarks>
    /// The runner is resolved here rather than injected, so the registry and the sync services can be
    /// registered in either order. A missing or unresolvable runner is logged and tolerated — the
    /// repository is already stored and the background poller will reach it on its next tick — and a
    /// failing first sync is recorded in <c>"SyncState"</c> by the runner itself, never rethrown into
    /// a request that has already succeeded (BRD-15).
    /// </remarks>
    private void QueueFirstSync(int aUserId, string aRepo)
    {
        IRepoSyncRunner? vRunner;
        try
        {
            vRunner = objSyncRunnerFactory();
        }
        catch (Exception vEx)
        {
            objLogger.LogWarning(vEx, "No sync runner available; {Repo} will sync on the next poll.", aRepo);
            return;
        }

        if (vRunner is null)
        {
            objLogger.LogWarning("No sync runner registered; {Repo} will sync on the next poll.", aRepo);
            return;
        }

        _ = Task.Run(() => RunFirstSyncAsync(vRunner, aUserId, aRepo));
    }

    /// <summary>
    /// Awaits the first sync on a background task and swallows its failure into the log.
    /// </summary>
    /// <param name="aRunner">The resolved sync runner.</param>
    /// <param name="aUserId">The user whose repository is being synced.</param>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <returns>A task that completes when the first sync has finished or failed.</returns>
    private async Task RunFirstSyncAsync(IRepoSyncRunner aRunner, int aUserId, string aRepo)
    {
        try
        {
            var vResult = await aRunner.SyncRepoAsync(aUserId, aRepo, CancellationToken.None).ConfigureAwait(false);
            objLogger.LogInformation(
                "First sync of {Repo} for user {UserId} finished: {Outcome}, {Records} records.",
                aRepo,
                aUserId,
                vResult.Outcome,
                vResult.RecordsWritten);
        }
        catch (Exception vEx)
        {
            objLogger.LogError(vEx, "First sync of {Repo} for user {UserId} failed.", aRepo, aUserId);
        }
    }

    /// <summary>
    /// Deletes one user's raw archive folder for a repository (BRD-101).
    /// </summary>
    /// <param name="aUserId">The user whose archive to delete; the user id is part of the path.</param>
    /// <param name="aRef">The repository whose folder to delete.</param>
    /// <remarks>
    /// The path is built from <see cref="TfLensOptions.RawPath"/>, which is user-scoped, and from an
    /// owner and name that have been through <see cref="RepoInputParser"/> — so no input can walk the
    /// delete out of the calling user's archive.
    /// </remarks>
    private void DeleteRawArchive(int aUserId, RepoRef aRef)
    {
        var vDirectory = Path.Combine(objOptions.RawPath(aUserId), aRef.ArchiveFolder);
        if (!Directory.Exists(vDirectory))
        {
            return;
        }

        Directory.Delete(vDirectory, true);
        objLogger.LogInformation("Deleted raw archive for {Repo}, user {UserId}.", aRef.Repo, aUserId);
    }

    /// <summary>
    /// Builds the row to store from GitHub's canonical owner and name.
    /// </summary>
    /// <param name="aUserId">The connecting user.</param>
    /// <param name="aInfo">GitHub's metadata, which fixes the casing of owner and name.</param>
    /// <param name="aValidation">The completed validation, which fixes the branch and framework.</param>
    /// <returns>The row to write.</returns>
    private static UserRepo ToUserRepo(int aUserId, GitHubRepoInfo aInfo, RepoValidation aValidation) => new()
    {
        UserId = aUserId,
        Repo = $"{aInfo.Owner}/{aInfo.Name}",
        Owner = aInfo.Owner,
        Name = aInfo.Name,
        Branch = aValidation.Branch!,
        Kind = aValidation.Framework!,
        Framework = aValidation.Framework!,
        IsPublic = true,
        ConnectedTs = DateTimeOffset.UtcNow.ToString("O")
    };

    /// <summary>
    /// Picks a repository's sync state out of a user's states.
    /// </summary>
    /// <param name="aStates">The user's sync-state rows.</param>
    /// <param name="aRepo"><c>owner/name</c> to match.</param>
    /// <returns>The matching state, or <c>null</c> when the repository has never synced.</returns>
    private static SyncState? FindState(IReadOnlyList<SyncState> aStates, string aRepo) =>
        aStates.FirstOrDefault(aState => string.Equals(aState.Repo, aRepo, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Turns a failed validation into the message the refusal throws with.
    /// </summary>
    /// <param name="aValidation">The validation that was not connectable.</param>
    /// <returns>The user-facing reason.</returns>
    private static string RefusalOf(RepoValidation aValidation) =>
        aValidation.Message ?? "The repository cannot be connected.";
}
