using TfLens.Core.Contracts;

namespace TfLens.Core.Abstractions;

/// <summary>
/// The typed client over the AppManager v1.4 REST surface (Application Id 1).
/// </summary>
/// <remarks>
/// Every call carries <c>X-Api-Key</c> / <c>X-Api-Secret</c> from configuration so the server resolves
/// the application; every password field is RSA-OAEP-256 encrypted with the cached public key before it
/// leaves the process. The client never calls LicenseSvc, FeatureSvc, PaymentSvc or IssueSvc (BRD-95).
/// </remarks>
public interface IAppManagerClient
{
    /// <summary>
    /// Signs a user in.
    /// </summary>
    /// <param name="aEmail">The user's email address.</param>
    /// <param name="aPassword">The plaintext password; encrypted before transport, never logged.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The tokens and profile claims AppManager returned.</returns>
    /// <exception cref="AppManagerException">AppManager rejected the sign-in; the code says why.</exception>
    Task<AuthResponseData> LoginAsync(string aEmail, string aPassword, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Registers a new user, always with <c>applicationRoleCode: "Manager"</c>.
    /// </summary>
    /// <param name="aRequest">The registration details.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The tokens and profile claims for the newly created user.</returns>
    /// <exception cref="AppManagerException">Registration failed — duplicate email, validation, or decryption.</exception>
    Task<AuthResponseData> RegisterAsync(RegisterRequest aRequest, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Starts a password reset. Succeeds identically whether or not the address exists.
    /// </summary>
    /// <param name="aEmail">The address to send the reset link to.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when AppManager has accepted the request.</returns>
    Task ForgotPasswordAsync(string aEmail, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Completes a password reset against a token from the emailed link.
    /// </summary>
    /// <param name="aToken">The reset token read from the query string.</param>
    /// <param name="aNewPassword">The new plaintext password; encrypted before transport.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the password has been changed.</returns>
    /// <exception cref="AppManagerException"><c>INVALID_RESET_TOKEN</c> or <c>APP_ID_MISMATCH</c>.</exception>
    Task ResetPasswordAsync(string aToken, string aNewPassword, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a new access token, rotating the refresh token.
    /// </summary>
    /// <param name="aRefreshToken">The stored refresh token.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The refreshed tokens.</returns>
    /// <exception cref="AppManagerException"><c>EXPIRED_REFRESH_TOKEN</c> when the session can no longer be renewed.</exception>
    Task<AuthResponseData> RefreshAsync(string aRefreshToken, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Confirms an access token is still valid.
    /// </summary>
    /// <param name="aAccessToken">The stored access token.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns><c>true</c> when AppManager still accepts the token.</returns>
    Task<bool> ValidateAsync(string aAccessToken, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Signs the session out at AppManager.
    /// </summary>
    /// <param name="aRefreshToken">The refresh token to invalidate.</param>
    /// <param name="aAccessToken">
    /// The session's access token. <c>POST /AuthSvc/logout</c> is an authenticated endpoint — verified
    /// live on 2026-08-26, a call without the bearer header is answered <c>401</c> and revokes nothing —
    /// so omitting it leaves the refresh token usable.
    /// </param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when AppManager has accepted the logout.</returns>
    /// <exception cref="AppManagerException">AppManager refused the logout; the local session is still cleared (BRD-4).</exception>
    Task LogoutAsync(
        string aRefreshToken,
        string? aAccessToken = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads the signed-in user's AppManager profile.
    /// </summary>
    /// <param name="aAccessToken">The session's access token.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The profile as AppManager holds it; TfLens stores none of it.</returns>
    Task<UserProfile> GetProfileAsync(string aAccessToken, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Changes the signed-in user's password.
    /// </summary>
    /// <param name="aAccessToken">The session's access token.</param>
    /// <param name="aCurrentPassword">The current plaintext password; encrypted before transport.</param>
    /// <param name="aNewPassword">The new plaintext password; encrypted before transport.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the password has been changed.</returns>
    /// <exception cref="AppManagerException"><c>INVALID_CURRENT_PASSWORD</c> or a rule violation.</exception>
    Task ChangePasswordAsync(
        string aAccessToken,
        string aCurrentPassword,
        string aNewPassword,
        CancellationToken aCancellationToken = default);
}

/// <summary>The per-user list of connected public GitHub repositories.</summary>
public interface IRepoRegistry
{
    /// <summary>
    /// Lists the repositories one user has connected.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The user's repositories; never another user's (ADR-013).</returns>
    Task<IReadOnlyList<UserRepo>> ListAsync(int aUserId, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Checks a candidate repository without connecting it.
    /// </summary>
    /// <param name="aUserId">The user connecting it, so a duplicate can be reported.</param>
    /// <param name="aInput">A GitHub URL or <c>owner/name</c>.</param>
    /// <param name="aBranch">Branch to read telemetry from; the default branch when omitted.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>What the checks found — existence, visibility and telemetry path.</returns>
    Task<RepoValidation> ValidateAsync(
        int aUserId,
        string aInput,
        string? aBranch = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Connects a repository to a user after re-running the validation.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aInput">A GitHub URL or <c>owner/name</c>.</param>
    /// <param name="aBranch">Branch to read telemetry from; the default branch when omitted.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The stored row.</returns>
    /// <exception cref="InvalidOperationException">The repository is private, missing, carries no telemetry, or is already connected by this user.</exception>
    Task<UserRepo> ConnectAsync(
        int aUserId,
        string aInput,
        string? aBranch = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Disconnects a repository and purges everything derived from it.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes once the rows, sync state and raw archive are gone.</returns>
    Task RemoveAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default);
}

/// <summary>Read-only access to a repository's telemetry files on GitHub.</summary>
/// <remarks>Structurally GET-only: no code path issues any other verb (BRD-16).</remarks>
public interface IGitHubStreamFetcher
{
    /// <summary>
    /// Finds the newest commit touching a repository's telemetry path.
    /// </summary>
    /// <param name="aOwner">GitHub owner.</param>
    /// <param name="aName">GitHub repository name.</param>
    /// <param name="aBranch">Branch to look at.</param>
    /// <param name="aPath">Repository-relative telemetry path.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The SHA, or <c>null</c> when the path has never been committed to.</returns>
    Task<string?> LatestShaAsync(
        string aOwner,
        string aName,
        string aBranch,
        string aPath,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Fetches one stream file at an exact SHA.
    /// </summary>
    /// <param name="aOwner">GitHub owner.</param>
    /// <param name="aName">GitHub repository name.</param>
    /// <param name="aPath">Repository-relative path of the file.</param>
    /// <param name="aSha">The commit SHA to read the file at.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The file's text, or <c>null</c> when GitHub answered 404 — a legitimate "stream absent".</returns>
    Task<string?> FetchFileAsync(
        string aOwner,
        string aName,
        string aPath,
        string aSha,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads a repository's metadata.
    /// </summary>
    /// <param name="aOwner">GitHub owner.</param>
    /// <param name="aName">GitHub repository name.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The metadata, or <c>null</c> when the repository does not exist or is not visible.</returns>
    Task<GitHubRepoInfo?> GetRepoAsync(string aOwner, string aName, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Tests whether a directory exists in a repository.
    /// </summary>
    /// <param name="aOwner">GitHub owner.</param>
    /// <param name="aName">GitHub repository name.</param>
    /// <param name="aPath">Repository-relative directory path.</param>
    /// <param name="aRef">Branch or SHA to look at.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns><c>true</c> when the path resolves.</returns>
    Task<bool> PathExistsAsync(
        string aOwner,
        string aName,
        string aPath,
        string aRef,
        CancellationToken aCancellationToken = default);
}

/// <summary>Turns raw JSONL text into typed records, preserving everything it does not recognise.</summary>
public interface IStreamParser
{
    /// <summary>
    /// Parses one stream file.
    /// </summary>
    /// <param name="aUserId">The user the records belong to.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aSourceSha">The SHA the file was fetched at.</param>
    /// <param name="aStream">Which stream the text is.</param>
    /// <param name="aText">The raw file text.</param>
    /// <returns>The typed records, plus the counts of what was skipped and what overflowed.</returns>
    ParseResult Parse(int aUserId, string aRepo, string aSourceSha, StreamKind aStream, string aText);
}

/// <summary>The store. Every read and write takes a user id — isolation is a parameter, not a filter (ADR-013).</summary>
public interface ITelemetryStore
{
    /// <summary>
    /// Applies the idempotent schema script.
    /// </summary>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the schema is present.</returns>
    Task EnsureSchemaAsync(CancellationToken aCancellationToken = default);

    /// <summary>
    /// Confirms the database is reachable.
    /// </summary>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns><c>true</c> when a connection opened and answered.</returns>
    Task<bool> PingAsync(CancellationToken aCancellationToken = default);

    /// <summary>
    /// Upserts a batch of parsed records, collapsing duplicates on their natural keys.
    /// </summary>
    /// <param name="aParsed">The parse output to store.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>How many rows were newly written.</returns>
    Task<int> UpsertAsync(ParseResult aParsed, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads every run record for a user, optionally narrowed to one repository.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis to read.</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The matching run records.</returns>
    Task<IReadOnlyList<RunRecord>> ReadRunsAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads every gate record for a user, optionally narrowed to one repository.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis to read.</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The matching gate records.</returns>
    Task<IReadOnlyList<GateRecord>> ReadGatesAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads every session record for a user, optionally narrowed to one repository.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis to read.</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The matching session records.</returns>
    Task<IReadOnlyList<SessionRecord>> ReadSessionsAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads every commit record for a user, optionally narrowed to one repository.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis to read.</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The matching commit records.</returns>
    Task<IReadOnlyList<CommitRecord>> ReadCommitsAsync(
        int aUserId,
        string aFramework,
        string? aRepo = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads every Playbook event record for a user, optionally narrowed to one repository.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo">One repository, or <c>null</c> for all of the user's.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The matching Playbook event records.</returns>
    Task<IReadOnlyList<PbEventRecord>> ReadPbEventsAsync(
        int aUserId,
        string? aRepo = null,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads the sync bookkeeping for a user's repositories.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>One row per connected repository that has been synced at least once.</returns>
    Task<IReadOnlyList<SyncState>> ReadSyncStateAsync(int aUserId, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Writes a repository's sync bookkeeping.
    /// </summary>
    /// <param name="aState">The state to store.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task WriteSyncStateAsync(SyncState aState, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Lists a user's connected repositories.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The user's repository rows.</returns>
    Task<IReadOnlyList<UserRepo>> ReadUserReposAsync(int aUserId, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Lists every connected repository across every user — the poller's work list.
    /// </summary>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>Every repository row.</returns>
    Task<IReadOnlyList<UserRepo>> ReadAllUserReposAsync(CancellationToken aCancellationToken = default);

    /// <summary>
    /// Stores a connected repository.
    /// </summary>
    /// <param name="aRepo">The row to write.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task WriteUserRepoAsync(UserRepo aRepo, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Deletes a repository and every row derived from it, for one user only.
    /// </summary>
    /// <remarks>
    /// <b>This removes all three layers</b>, scoped to <c>(aUserId, aRepo)</c>: every stream table row
    /// (<c>"Run"</c>, <c>"Gate"</c>, <c>"Session"</c>, <c>"Commit"</c>, <c>"PbEvent"</c>), the
    /// <c>"SyncState"</c> row, and the <c>"UserRepo"</c> row itself — so the repository is *disconnected*,
    /// not merely emptied, and the poller stops visiting it (REQ-FN-016). It is the only delete on this
    /// interface, and a caller such as <c>RepoRegistry.RemoveAsync</c> needs nothing else from the store;
    /// it remains responsible for the raw archive under <c>data/raw/</c>, which the store never touches.
    /// Another user's copy of the same public repository is untouched (ADR-013).
    /// </remarks>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the stream rows, the sync state and the repository row are gone.</returns>
    Task DeleteRepoDataAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads the per-stream coverage facts the sync bookkeeping does not hold (REQ-UI-014..REQ-UI-016).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>"SyncState"</c> answers "how many rows", never "how old is the newest one" and never "which
    /// undocumented fields did they carry". Both are properties of the stored rows — the newest
    /// <c>"Ts"</c>, the keys of the <c>"Overflow"</c> jsonb column and the <c>"V"</c> column — so the
    /// Coverage page reads them from the stream tables rather than re-parsing an archive.
    /// </para>
    /// <para>
    /// The implementation must return field <b>names</b> only, already filtered to those SCHEMA.md does
    /// not document: an <c>Overflow</c> payload is never allowed to reach a caller (REQ-UI-016).
    /// </para>
    /// <para>
    /// The default returns <see cref="CoverageFacts.Empty"/> so a store that has nothing to say about
    /// stored rows — a fixture or an in-memory fake — degrades to an honest "nothing observed" rather
    /// than failing to compile.
    /// </para>
    /// </remarks>
    /// <param name="aUserId">The AppManager user id — mandatory (ADR-013).</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The per-stream counts and ages, the undocumented field names, and any <c>v &gt; 1</c> records.</returns>
    Task<CoverageFacts> ReadCoverageFactsAsync(int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult(CoverageFacts.Empty);

    /// <summary>
    /// Drops and recreates every stream table, then replays the raw archive.
    /// </summary>
    /// <param name="aUserId">One user, or <c>null</c> to rebuild every user's data.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>What the replay found — files, records and duplicates collapsed.</returns>
    Task<RebuildReport> RebuildAsync(int? aUserId = null, CancellationToken aCancellationToken = default);
}

/// <summary>Server-side storage of the AppManager tokens behind a TfLens cookie.</summary>
public interface IAuthSessionStore
{
    /// <summary>
    /// Stores a new session.
    /// </summary>
    /// <param name="aSession">The session row; token columns are encrypted by the implementation.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task CreateAsync(AuthSessionRow aSession, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Reads a session by its id.
    /// </summary>
    /// <param name="aSessionId">The session id carried in the cookie.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The session, or <c>null</c> when it has been signed out or expired.</returns>
    Task<AuthSessionRow?> GetAsync(string aSessionId, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Replaces a session's tokens after a refresh.
    /// </summary>
    /// <param name="aSession">The session row with rotated tokens.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the row is updated.</returns>
    Task UpdateAsync(AuthSessionRow aSession, CancellationToken aCancellationToken = default);

    /// <summary>
    /// Deletes a session on sign-out.
    /// </summary>
    /// <param name="aSessionId">The session id.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the row is gone.</returns>
    Task DeleteAsync(string aSessionId, CancellationToken aCancellationToken = default);
}

/// <summary>The port of <c>analyse()</c> from <c>tf-metrics.sh</c> — the parity surface.</summary>
public interface IMetricsEngine
{
    /// <summary>
    /// Computes every segmented figure for one user and framework.
    /// </summary>
    /// <param name="aUserId">The AppManager user id — a required parameter, never a filter (ADR-013).</param>
    /// <param name="aFramework">The provenance axis; figures never pool across frameworks (ADR-016).</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The analysis, whose shape cannot express a merged rate (ADR-007).</returns>
    Task<AnalysisResult> AnalyseAsync(
        int aUserId,
        string aFramework,
        CancellationToken aCancellationToken = default);
}

/// <summary>The metrics the reference does not compute, and which therefore have no parity oracle.</summary>
public interface IExtraMetrics
{
    /// <summary>
    /// Computes the per-harness comparison.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>One column per detected harness, plus the not-detected footnote count.</returns>
    Task<HarnessComparison> CompareHarnessesAsync(
        int aUserId,
        string aFramework,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Computes routing drift, tokens by model and counterfactual repricing.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The routing view; every money figure is labelled an estimate.</returns>
    Task<RoutingAnalysis> AnalyseRoutingAsync(
        int aUserId,
        string aFramework,
        CancellationToken aCancellationToken = default);
}

/// <summary>Writes the weekly snapshot — the diffable numbers plus their parity stamp.</summary>
public interface ISnapshotExporter
{
    /// <summary>
    /// Writes <c>snapshot.md</c> and <c>tflens.json</c> for one user, framework and date.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis; one snapshot per framework.</param>
    /// <param name="aDate">The report date, used as the folder name.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>Where the two files were written and what stamp they carry.</returns>
    Task<SnapshotResult> ExportAsync(
        int aUserId,
        string aFramework,
        DateOnly aDate,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Lists the snapshots already written for a user.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>One entry per snapshot folder, newest first.</returns>
    Task<IReadOnlyList<SnapshotResult>> ListAsync(int aUserId, CancellationToken aCancellationToken = default);
}
