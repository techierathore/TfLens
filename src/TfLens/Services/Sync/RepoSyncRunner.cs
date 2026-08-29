using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Import;

namespace TfLens.Services.Sync;

/// <summary>
/// The one code path a sync ever takes — poller, <c>Sync now</c> button and <c>sync</c> verb alike.
/// </summary>
/// <remarks>
/// <para>
/// Per repository: ask GitHub for the newest commit touching the telemetry path, and when that SHA
/// still equals <c>SyncState.LastSha</c> skip the repository entirely — no file traffic at all, one API
/// call (BRD-13). Otherwise fetch each stream file at that exact SHA, write the bytes to the raw
/// archive <b>before</b> parsing (BRD-19 — the archive is what <c>rebuild</c> replays, so it is written
/// first, always), then parse and upsert.
/// </para>
/// <para>
/// Failures are contained per repository (BRD-15): a 401, 403, 404 or network failure is redacted into
/// that repository's <c>SyncState.LastError</c> and the remaining repositories still sync. A pass is
/// never fatal. <c>SyncAsync(null)</c> is the poller's pass over every user; <c>SyncAsync(userId)</c>
/// is what <c>Sync now</c> calls, and it touches only that user's repositories (BRD-103).
/// </para>
/// <para>
/// The runner is a <b>singleton</b>: the background poller is one, and the repo registry queues a
/// first sync through a factory resolved from the root provider, which only works for a singleton.
/// The scoped services a pass needs — the fetcher, the store and the parser — are therefore resolved
/// from a scope opened per pass rather than injected, which is what keeps a captive scoped dependency
/// out of the graph.
/// </para>
/// </remarks>
public sealed class RepoSyncRunner : IRepoSyncRunner
{
    private static readonly UTF8Encoding RawEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IServiceScopeFactory objScopeFactory;
    private readonly AnalysisCacheInvalidator objCacheInvalidator;
    private readonly TfLensOptions objOptions;
    private readonly ILogger<RepoSyncRunner> objLogger;

    /// <summary>
    /// Creates the runner.
    /// </summary>
    /// <param name="aScopeFactory">Opens the scope each pass resolves its fetcher, store and parser from.</param>
    /// <param name="aCacheInvalidator">Drops the memoised analysis of every user a pass changed.</param>
    /// <param name="aOptions">TfLens configuration, read for the data root.</param>
    /// <param name="aLogger">Logger; it records user ids, repositories, SHAs, counts and status codes only.</param>
    public RepoSyncRunner(
        IServiceScopeFactory aScopeFactory,
        AnalysisCacheInvalidator aCacheInvalidator,
        IOptions<TfLensOptions> aOptions,
        ILogger<RepoSyncRunner> aLogger)
    {
        objScopeFactory = aScopeFactory;
        objCacheInvalidator = aCacheInvalidator;
        objOptions = aOptions.Value;
        objLogger = aLogger;
    }

    /// <inheritdoc />
    public async Task<SyncReport> SyncAsync(int? aUserId = null, CancellationToken aCancellationToken = default)
    {
        var vStartedTs = Timestamp();

        await using var vScope = objScopeFactory.CreateAsyncScope();
        var vWork = Resolve(vScope.ServiceProvider);

        var vRepos = aUserId is null
            ? await vWork.Store.ReadAllUserReposAsync(aCancellationToken).ConfigureAwait(false)
            : await vWork.Store.ReadUserReposAsync(aUserId.Value, aCancellationToken).ConfigureAwait(false);

        objLogger.LogInformation(
            "Sync pass starting for {RepoCount} repositories ({Scope})",
            vRepos.Count,
            aUserId is null ? "all users" : $"user {aUserId.Value}");

        var vResults = new List<RepoSyncResult>(vRepos.Count);
        var vChangedUsers = new HashSet<int>();

        foreach (var vRepo in vRepos)
        {
            aCancellationToken.ThrowIfCancellationRequested();

            var vResult = await SyncOneAsync(vWork, vRepo, aCancellationToken).ConfigureAwait(false);
            vResults.Add(vResult);

            if (vResult.Outcome == SyncOutcome.Updated)
            {
                vChangedUsers.Add(vRepo.UserId);
            }
        }

        // BRD-18: a report page opened after this pass must recompute, not serve the pre-sync figures.
        foreach (var vChangedUser in vChangedUsers)
        {
            objCacheInvalidator.Invalidate(vChangedUser);
        }

        var vReport = new SyncReport(aUserId, vResults, vStartedTs, Timestamp());

        objLogger.LogInformation(
            "Sync pass finished: {Updated} updated, {Skipped} skipped, {Errors} errors",
            vReport.UpdatedCount,
            vReport.SkippedCount,
            vReport.ErrorCount);

        return vReport;
    }

    /// <inheritdoc />
    public async Task<RepoSyncResult> SyncRepoAsync(
        int aUserId,
        string aRepo,
        CancellationToken aCancellationToken = default)
    {
        await using var vScope = objScopeFactory.CreateAsyncScope();
        var vWork = Resolve(vScope.ServiceProvider);

        var vRepos = await vWork.Store.ReadUserReposAsync(aUserId, aCancellationToken).ConfigureAwait(false);

        var vRepo = vRepos.FirstOrDefault(aR => string.Equals(aR.Repo, aRepo, StringComparison.OrdinalIgnoreCase));

        if (vRepo is null)
        {
            return new RepoSyncResult(aRepo, SyncOutcome.Error, null, 0, "The repository is not connected by this user.");
        }

        var vResult = await SyncOneAsync(vWork, vRepo, aCancellationToken).ConfigureAwait(false);

        if (vResult.Outcome == SyncOutcome.Updated)
        {
            objCacheInvalidator.Invalidate(aUserId);
        }

        return vResult;
    }

    /// <summary>
    /// Syncs one repository, absorbing every failure into that repository's own state.
    /// </summary>
    /// <remarks>BRD-15 — this method never throws for anything but cancellation, so a pass never aborts.</remarks>
    /// <param name="aWork">The pass's scoped services.</param>
    /// <param name="aRepo">The connected repository.</param>
    /// <param name="aCancellationToken">Cancels the sync.</param>
    /// <returns>What happened to that repository.</returns>
    private async Task<RepoSyncResult> SyncOneAsync(SyncWork aWork, UserRepo aRepo, CancellationToken aCancellationToken)
    {
        // REQ-FN-085 — an imported source has no repository TfLens can reach. Returning before the
        // state read means a poller tick makes NO outbound request for it and leaves its counts,
        // its LastImportTs and its error state exactly as it found them; a Sync that "failed" would
        // otherwise write an error onto a perfectly healthy row. Its action is Re-import.
        if (!ImportedSourceRules.CanSync(aRepo.SourceKind))
        {
            return new RepoSyncResult(aRepo.Repo, SyncOutcome.Skipped, null, 0, null);
        }

        var vPrevious = await ReadStateAsync(aWork, aRepo, aCancellationToken).ConfigureAwait(false);

        try
        {
            return await SyncChangedAsync(aWork, aRepo, vPrevious, aCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (aCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception vEx)
        {
            var vError = SyncErrorRedactor.Redact(vEx);

            objLogger.LogWarning(
                "Sync failed for user {UserId} repository {Repo}: {Reason}", aRepo.UserId, aRepo.Repo, vError);

            await WriteStateAsync(
                    aWork,
                    aRepo,
                    vPrevious with { LastSyncTs = Timestamp(), LastError = vError },
                    aCancellationToken)
                .ConfigureAwait(false);

            return new RepoSyncResult(aRepo.Repo, SyncOutcome.Error, vPrevious.LastSha, 0, vError);
        }
    }

    /// <summary>
    /// Reads the telemetry SHA and, when it moved, archives, parses and stores every stream.
    /// </summary>
    /// <param name="aWork">The pass's scoped services.</param>
    /// <param name="aRepo">The connected repository.</param>
    /// <param name="aPrevious">The repository's state before this attempt.</param>
    /// <param name="aCancellationToken">Cancels the sync.</param>
    /// <returns>What happened to that repository.</returns>
    private async Task<RepoSyncResult> SyncChangedAsync(
        SyncWork aWork,
        UserRepo aRepo,
        SyncState aPrevious,
        CancellationToken aCancellationToken)
    {
        var vFramework = ResolveFramework(aRepo);
        var vTelemetryPath = FrameworkNames.TelemetryPath(vFramework);

        var vSha = await aWork.Fetcher
            .LatestShaAsync(aRepo.Owner, aRepo.Name, aRepo.Branch, vTelemetryPath, aCancellationToken)
            .ConfigureAwait(false);

        // BRD-13: an unchanged repository costs exactly one API call and fetches no file bytes.
        if (vSha is null || string.Equals(vSha, aPrevious.LastSha, StringComparison.Ordinal))
        {
            await WriteStateAsync(
                    aWork,
                    aRepo,
                    aPrevious with { LastSyncTs = Timestamp() },
                    aCancellationToken)
                .ConfigureAwait(false);

            objLogger.LogInformation(
                "Skipped user {UserId} repository {Repo}: telemetry SHA unchanged at {Sha}",
                aRepo.UserId,
                aRepo.Repo,
                vSha ?? "(none)");

            return new RepoSyncResult(aRepo.Repo, SyncOutcome.Skipped, vSha ?? aPrevious.LastSha, 0, null);
        }

        var vCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var vWritten = 0;

        // REQ-FN-063: this pass' share of the session collapses. A sync sees new files on top of what is
        // already stored, so it ADDS to the stored figure — only a rebuild, which replays everything, is
        // authoritative enough to set it. Measured as presented-minus-inserted rather than from the
        // parser's own count, because a session id repeated across two syncs is collapsed by
        // UcSessionUserRepoId and no single parse can see it.
        var vSessionsCollapsed = 0;

        foreach (var vStream in FrameworkNames.Streams(vFramework))
        {
            var vText = await aWork.Fetcher
                .FetchFileAsync(
                    aRepo.Owner,
                    aRepo.Name,
                    $"{vTelemetryPath}/{StreamFileName(vStream)}",
                    vSha,
                    aCancellationToken)
                .ConfigureAwait(false);

            // BRD-14: a repository missing one stream syncs successfully with that stream at zero.
            if (vText is null)
            {
                vCounts[vStream] = 0;
                continue;
            }

            // BRD-19: the archive is the rebuild source of truth, so the bytes land before the parse.
            await ArchiveAsync(aRepo, vStream, vSha, vText, aCancellationToken).ConfigureAwait(false);

            var vParsed = aWork.Parser.Parse(aRepo.UserId, aRepo.Repo, vSha, StreamNames.ToKind(vStream), vText);
            vCounts[vStream] = vParsed.RecordCount;

            var vInserted = await aWork.Store.UpsertAsync(vParsed, aCancellationToken).ConfigureAwait(false);
            vWritten += vInserted;

            if (vStream == StreamNames.Sessions)
            {
                // The dataset's own duplicate count, from THIS snapshot — not how many rows the
                // store rejected as already present.
                //
                // It used to be `SessionsPresented - vInserted`, accumulated onto the previous
                // total. Both halves were wrong for a figure that gets quoted. Re-syncing a repo
                // whose sessions.jsonl had not changed presented every record again and inserted
                // none, so the count grew by a whole file each time: TechieFlow reached 25 against
                // the 3 duplicates its file actually contains, and BRD §13 caught it on 2026-08-29.
                //
                // A quotable figure is a property of the data, not of how many times we read it.
                // Two TfLens instances pointed at the same repositories have to report the same
                // number, and the reference (`dedupe_sessions`, per repo) counts duplicates WITHIN
                // the snapshot it is reading. `vParsed.SessionDuplicatesCollapsed` is exactly that
                // count, so it is assigned — the newest snapshot replaces the older answer rather
                // than being added to it.
                //
                // What this deliberately gives up: a session id repeated across two archived
                // snapshots, which no single parse can see. That is real, but it is a fact about
                // TfLens's archive rather than about the user's telemetry, and pricing it into a
                // published figure is what made the number indefensible.
                vSessionsCollapsed = vParsed.SessionDuplicatesCollapsed;
            }
        }

        // BRD-17: the SHA, the timestamp and the per-stream counts, with LastError cleared on success.
        await WriteStateAsync(
                aWork,
                aRepo,
                BuildState(aRepo, vSha, vCounts, vSessionsCollapsed),
                aCancellationToken)
            .ConfigureAwait(false);

        objLogger.LogInformation(
            "Updated user {UserId} repository {Repo} to {Sha}: {Written} rows written",
            aRepo.UserId,
            aRepo.Repo,
            vSha,
            vWritten);

        return new RepoSyncResult(aRepo.Repo, SyncOutcome.Updated, vSha, vWritten, null);
    }

    /// <summary>
    /// Writes one stream file's bytes to the raw archive, verbatim.
    /// </summary>
    /// <remarks>
    /// REQ-FN-027 — <c>data/raw/&lt;userId&gt;/&lt;owner&gt;__&lt;name&gt;/&lt;stream&gt;-&lt;sha&gt;.jsonl</c>.
    /// A parser exception after this point leaves the archive intact, so <c>rebuild</c> can replay it.
    /// </remarks>
    /// <param name="aRepo">The connected repository.</param>
    /// <param name="aStream">The stream's wire name.</param>
    /// <param name="aSha">The SHA the file was fetched at.</param>
    /// <param name="aText">The file's text exactly as GitHub answered it.</param>
    /// <param name="aCancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the bytes are on disk.</returns>
    private async Task ArchiveAsync(
        UserRepo aRepo,
        string aStream,
        string aSha,
        string aText,
        CancellationToken aCancellationToken)
    {
        var vDirectory = ArchiveDirectory(objOptions, aRepo);
        Directory.CreateDirectory(vDirectory);

        var vPath = Path.Combine(vDirectory, $"{aStream}-{aSha}.jsonl");
        await File.WriteAllBytesAsync(vPath, RawEncoding.GetBytes(aText), aCancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Names the raw-archive directory for one user's one repository.
    /// </summary>
    /// <param name="aOptions">TfLens configuration, read for the data root.</param>
    /// <param name="aRepo">The connected repository.</param>
    /// <returns>The directory the stream files are archived in.</returns>
    public static string ArchiveDirectory(TfLensOptions aOptions, UserRepo aRepo) =>
        Path.Combine(aOptions.RawPath(aRepo.UserId), $"{aRepo.Owner}__{aRepo.Name}");

    /// <summary>
    /// Names the file one stream lives in under a repository's telemetry path.
    /// </summary>
    /// <param name="aStream">The stream's wire name.</param>
    /// <returns>The file name, including its extension.</returns>
    public static string StreamFileName(string aStream) =>
        aStream == StreamNames.Events ? $"{StreamNames.Events}.ndjson" : $"{aStream}.jsonl";

    /// <summary>
    /// Resolves the provenance axis of a connected repository.
    /// </summary>
    /// <remarks>ADR-016 — <c>Framework</c> is the stored axis; <c>Kind</c> is the same vocabulary and is the fallback.</remarks>
    /// <param name="aRepo">The connected repository.</param>
    /// <returns>The framework name.</returns>
    private static string ResolveFramework(UserRepo aRepo) =>
        string.IsNullOrWhiteSpace(aRepo.Framework) ? aRepo.Kind : aRepo.Framework;

    /// <summary>
    /// Builds the state row a successful sync writes.
    /// </summary>
    /// <param name="aRepo">The connected repository.</param>
    /// <param name="aSha">The SHA the streams were read at.</param>
    /// <param name="aCounts">Records the parser reported, by stream wire name.</param>
    /// <param name="aSessionDuplicatesCollapsed">
    /// The repository's running session-collapse total — what was stored before this pass plus what this
    /// pass collapsed. The caller does the addition, because only it knows the previous row (REQ-FN-063).
    /// </param>
    /// <returns>The row to store.</returns>
    private static SyncState BuildState(
        UserRepo aRepo,
        string aSha,
        IReadOnlyDictionary<string, int> aCounts,
        int aSessionDuplicatesCollapsed) =>
        new()
        {
            UserId = aRepo.UserId,
            Repo = aRepo.Repo,
            Kind = aRepo.Kind,
            Branch = aRepo.Branch,
            LastSha = aSha,
            LastSyncTs = Timestamp(),
            LastError = null,
            RunsCount = Count(aCounts, StreamNames.Runs),
            GatesCount = Count(aCounts, StreamNames.Gates),
            SessionsCount = Count(aCounts, StreamNames.Sessions),
            CommitsCount = Count(aCounts, StreamNames.Commits),
            EventsCount = Count(aCounts, StreamNames.Events),
            // REQ-FN-071: one count for the whole misses stream — the parser has already split its
            // three record kinds, but the file, and therefore the Coverage row, is one.
            MissesCount = Count(aCounts, StreamNames.Misses),
            SessionDuplicatesCollapsed = aSessionDuplicatesCollapsed
        };

    /// <summary>Reads one stream's count out of the pass's tally.</summary>
    /// <param name="aCounts">The tally.</param>
    /// <param name="aStream">The stream's wire name.</param>
    /// <returns>The count, or zero when the stream was absent.</returns>
    private static int Count(IReadOnlyDictionary<string, int> aCounts, string aStream) =>
        aCounts.TryGetValue(aStream, out var vCount) ? vCount : 0;

    /// <summary>
    /// Reads a repository's stored state, or an empty one on its first sync.
    /// </summary>
    /// <param name="aWork">The pass's scoped services.</param>
    /// <param name="aRepo">The connected repository.</param>
    /// <param name="aCancellationToken">Cancels the read.</param>
    /// <returns>The stored state, or a fresh row keyed by user and repository.</returns>
    private async Task<SyncState> ReadStateAsync(SyncWork aWork, UserRepo aRepo, CancellationToken aCancellationToken)
    {
        var vFresh = new SyncState
        {
            UserId = aRepo.UserId,
            Repo = aRepo.Repo,
            Kind = aRepo.Kind,
            Branch = aRepo.Branch
        };

        try
        {
            var vStates = await aWork.Store.ReadSyncStateAsync(aRepo.UserId, aCancellationToken).ConfigureAwait(false);

            return vStates.FirstOrDefault(aS => string.Equals(aS.Repo, aRepo.Repo, StringComparison.Ordinal)) ?? vFresh;
        }
        catch (Exception vEx) when (!aCancellationToken.IsCancellationRequested)
        {
            objLogger.LogWarning(
                vEx, "Could not read sync state for user {UserId} repository {Repo}", aRepo.UserId, aRepo.Repo);
            return vFresh;
        }
    }

    /// <summary>Writes a repository's state, absorbing a store failure so it cannot abort the pass.</summary>
    /// <param name="aWork">The pass's scoped services.</param>
    /// <param name="aRepo">The connected repository, for the log line.</param>
    /// <param name="aState">The row to write.</param>
    /// <param name="aCancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the write has been attempted.</returns>
    private async Task WriteStateAsync(
        SyncWork aWork,
        UserRepo aRepo,
        SyncState aState,
        CancellationToken aCancellationToken)
    {
        try
        {
            await aWork.Store.WriteSyncStateAsync(aState, aCancellationToken).ConfigureAwait(false);
        }
        catch (Exception vEx) when (!aCancellationToken.IsCancellationRequested)
        {
            objLogger.LogWarning(
                vEx, "Could not write sync state for user {UserId} repository {Repo}", aRepo.UserId, aRepo.Repo);
        }
    }

    /// <summary>
    /// Resolves the scoped services one pass works through.
    /// </summary>
    /// <param name="aServices">The pass's scope.</param>
    /// <returns>The fetcher, store and parser for this pass.</returns>
    private static SyncWork Resolve(IServiceProvider aServices) => new(
        aServices.GetRequiredService<IGitHubStreamFetcher>(),
        aServices.GetRequiredService<ITelemetryStore>(),
        aServices.GetRequiredService<IStreamParser>());

    /// <summary>The scoped services one sync pass works through.</summary>
    /// <param name="Fetcher">The read-only GitHub client.</param>
    /// <param name="Store">The telemetry store.</param>
    /// <param name="Parser">The JSONL parser.</param>
    private sealed record SyncWork(IGitHubStreamFetcher Fetcher, ITelemetryStore Store, IStreamParser Parser);

    /// <summary>The ISO-8601 UTC timestamp the report and the state rows carry.</summary>
    /// <returns>The current instant, formatted for storage.</returns>
    private static string Timestamp() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
