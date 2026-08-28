namespace TfLens.Core.Contracts;

/// <summary>
/// The framework a connected repository emits telemetry for. A stored, mandatory provenance axis.
/// </summary>
/// <remarks>
/// ADR-016: framework is set at connect time from the telemetry path and every engine read takes it,
/// so a figure cannot pool across frameworks any more than it can across users.
/// </remarks>
public static class FrameworkNames
{
    /// <summary>TechieFlow — telemetry under <c>docs/metrics/</c>.</summary>
    public const string TechieFlow = "techieflow";

    /// <summary>AI-First-Playbook — telemetry under <c>verification/telemetry/</c>.</summary>
    public const string Playbook = "playbook";

    /// <summary>Both frameworks in display order.</summary>
    public static readonly IReadOnlyList<string> All = [TechieFlow, Playbook];

    /// <summary>
    /// Returns the telemetry directory a framework's streams live in.
    /// </summary>
    /// <param name="aFramework">One of <see cref="TechieFlow"/> or <see cref="Playbook"/>.</param>
    /// <returns>The repository-relative telemetry path.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The framework is not recognised.</exception>
    public static string TelemetryPath(string aFramework) => aFramework switch
    {
        TechieFlow => "docs/metrics",
        Playbook => "verification/telemetry",
        _ => throw new ArgumentOutOfRangeException(nameof(aFramework), aFramework, "Unknown framework.")
    };

    /// <summary>
    /// Returns the stream wire names a framework carries.
    /// </summary>
    /// <param name="aFramework">One of <see cref="TechieFlow"/> or <see cref="Playbook"/>.</param>
    /// <returns>The stream names in report order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The framework is not recognised.</exception>
    public static IReadOnlyList<string> Streams(string aFramework) => aFramework switch
    {
        TechieFlow => StreamNames.TechieFlow,
        Playbook => StreamNames.Playbook,
        _ => throw new ArgumentOutOfRangeException(nameof(aFramework), aFramework, "Unknown framework.")
    };
}

/// <summary>
/// How a connected source's telemetry arrives (added 2026-08-28, F-IMPORT).
/// </summary>
/// <remarks>
/// The <b>only</b> structural trace of origin in the whole store: no stream table carries it and no
/// figure segments on it, because a miss is a miss whoever's clone it came from (ADR-021, BRD-136).
/// It is displayed — on <c>/repos</c>, on Coverage and in the export's per-repo block — and pooled
/// nowhere. The behaviour behind an <see cref="Import"/> source belongs to REQ-FN-082..REQ-FN-087.
/// </remarks>
/// <remarks>
/// <b>Stored value versus displayed label.</b> BRD-132 fixes the column's vocabulary as
/// <c>api</c> | <c>import</c> and the badge's wording as <i>Synced</i> | <i>Imported</i>; they are
/// deliberately different words. The stored value names the <i>mode the user chose</i> in the
/// Add-source dialog and is what the export's <c>source_kind</c> key carries, so it must stay stable
/// even if the badge is reworded; the label describes the row's <i>current state</i> to a reader.
/// Collapsing the two — storing the label — would make a copy change a schema change.
/// Use <see cref="DisplayName"/> at every rendering site rather than a literal.
/// </remarks>
public static class SourceKinds
{
    /// <summary>Fetched from GitHub by the poller and the Sync button; carries <c>LastSha</c>.</summary>
    public const string Api = "api";

    /// <summary>Uploaded as a bundle of metric files; carries <c>BundleSha</c> and <c>LastImportTs</c>.</summary>
    public const string Import = "import";

    /// <summary>The value a source row carries when nothing set one — a fetched source.</summary>
    public const string Default = Api;

    /// <summary>Badge wording for <see cref="Api"/> (BRD-132).</summary>
    public const string ApiLabel = "Synced";

    /// <summary>Badge wording for <see cref="Import"/> (BRD-132).</summary>
    public const string ImportLabel = "Imported";

    /// <summary>
    /// Maps a stored source kind to the label shown on <c>/repos</c> and Coverage.
    /// </summary>
    /// <remarks>An unrecognised or absent value reads as <see cref="Api"/>, matching the column default.</remarks>
    /// <param name="aSourceKind">The stored <c>SourceKind</c>.</param>
    /// <returns>The badge wording.</returns>
    public static string DisplayName(string? aSourceKind) =>
        IsImport(aSourceKind) ? ImportLabel : ApiLabel;

    /// <summary>
    /// Tests whether a stored source kind names an imported source.
    /// </summary>
    /// <remarks>An unrecognised or absent value reads as <see cref="Api"/>, matching the column default.</remarks>
    /// <param name="aSourceKind">The stored <c>SourceKind</c>.</param>
    /// <returns><c>true</c> when the source's data was uploaded.</returns>
    public static bool IsImport(string? aSourceKind) =>
        string.Equals(aSourceKind, Import, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A public GitHub repository one user has connected, stored in the <c>"UserRepo"</c> table.
/// </summary>
/// <remarks>
/// The same <c>owner/name</c> may be connected by different users; a duplicate for the same user is
/// rejected at connect time (BRD-104). Removal purges this row, every stream row and the raw archive.
/// </remarks>
public sealed record UserRepo
{
    /// <summary>AppManager user who connected the repository.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> — the key within a user.</summary>
    public required string Repo { get; init; }

    /// <summary>GitHub owner (user or organisation).</summary>
    public required string Owner { get; init; }

    /// <summary>GitHub repository name.</summary>
    public required string Name { get; init; }

    /// <summary>Branch the telemetry is read from; the default branch unless overridden.</summary>
    public required string Branch { get; init; }

    /// <summary>Detected telemetry kind — the same vocabulary as <see cref="Framework"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Provenance axis: <c>techieflow</c> or <c>playbook</c> (ADR-016).</summary>
    public required string Framework { get; init; }

    /// <summary>Always true — private repositories are refused in this release (BRD-100).</summary>
    public bool IsPublic { get; init; } = true;

    /// <summary>ISO-8601 timestamp the repository was connected.</summary>
    public required string ConnectedTs { get; init; }

    /// <summary>
    /// <see cref="SourceKinds.Api"/> (<c>api</c>) or <see cref="SourceKinds.Import"/> (<c>import</c>) — how the data arrives.
    /// </summary>
    /// <remarks>
    /// <b>Schema only at this point.</b> The column exists so the import cluster has somewhere to write;
    /// the behaviour it drives — skipping imported sources in the poller and in Sync, the Re-import
    /// action, the badge — is <b>REQ-FN-085</b> / <b>REQ-FN-087</b> and is not implemented here.
    /// Nothing pools or segments on it (ADR-021).
    /// </remarks>
    public string SourceKind { get; init; } = SourceKinds.Default;

    /// <summary>
    /// sha256 of the uploaded bundle — an imported source's dataset identity; <c>null</c> when fetched.
    /// </summary>
    /// <remarks>
    /// <b>Schema only at this point (REQ-FN-084).</b> The bundle hash stands exactly where a fetched
    /// source's commit SHA stands: raw-archive file name, Coverage row, the export's <c>per_repo</c>
    /// block and the dataset a parity run pins. The invariant the import cluster enforces in code is that
    /// a <see cref="UserRepo"/> carries <see cref="SyncState.LastSha"/> <b>or</b> this, never both.
    /// </remarks>
    public string? BundleSha { get; init; }

    /// <summary>
    /// When the source was last imported; <c>null</c> when fetched.
    /// </summary>
    /// <remarks>
    /// <b>Schema only at this point (REQ-FN-085).</b> A poller tick and a header Sync must leave it
    /// untouched, because neither makes an outbound request for an imported source.
    /// </remarks>
    public DateTimeOffset? LastImportTs { get; init; }
}

/// <summary>
/// A server-side session row holding the AppManager tokens for one TfLens cookie.
/// </summary>
/// <remarks>
/// Tokens never reach the browser: the cookie carries only the session id and display claims.
/// The token columns are encrypted at rest with ASP.NET Data Protection.
/// </remarks>
public sealed record AuthSessionRow
{
    /// <summary>Random session identifier; the only value the cookie carries that maps here.</summary>
    public required string SessionId { get; init; }

    /// <summary>AppManager user id — the only user key TfLens stores.</summary>
    public required int UserId { get; init; }

    /// <summary>Signed-in user's email, for display in the user menu.</summary>
    public required string Email { get; init; }

    /// <summary>Signed-in user's display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>AppManager access token, encrypted at rest.</summary>
    public required string AccessToken { get; init; }

    /// <summary>AppManager refresh token, encrypted at rest; rotated on every refresh.</summary>
    public required string RefreshToken { get; init; }

    /// <summary>ISO-8601 expiry of the access token; refreshed within five minutes of this.</summary>
    public required string TokenExpiresAt { get; init; }

    /// <summary>ISO-8601 timestamp the session was created.</summary>
    public required string CreatedTs { get; init; }

    /// <summary>ISO-8601 timestamp the session was last validated against AppManager (hourly on resume).</summary>
    public string? LastValidatedTs { get; init; }
}

/// <summary>
/// Sync bookkeeping for one user's one repository, stored in the <c>"SyncState"</c> table.
/// </summary>
/// <remarks>
/// <see cref="LastSha"/> is what makes a poll free: when the newest commit touching the telemetry path
/// still equals it, no file is fetched at all. <see cref="LastError"/> holds a redacted message —
/// never a token, never a URL carrying one.
/// </remarks>
public sealed record SyncState
{
    /// <summary>AppManager user the state belongs to.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the repository.</summary>
    public required string Repo { get; init; }

    /// <summary>Detected telemetry kind.</summary>
    public string? Kind { get; init; }

    /// <summary>Branch the telemetry is read from.</summary>
    public string? Branch { get; init; }

    /// <summary>Newest commit SHA touching the telemetry path at the last successful sync.</summary>
    public string? LastSha { get; init; }

    /// <summary>ISO-8601 timestamp of the last sync attempt that completed.</summary>
    public string? LastSyncTs { get; init; }

    /// <summary>Redacted message from the last failed sync; <c>null</c> when the last sync succeeded.</summary>
    public string? LastError { get; init; }

    /// <summary>Rows stored for the <c>runs</c> stream.</summary>
    public int RunsCount { get; init; }

    /// <summary>Rows stored for the <c>gates</c> stream.</summary>
    public int GatesCount { get; init; }

    /// <summary>Rows stored for the <c>sessions</c> stream.</summary>
    public int SessionsCount { get; init; }

    /// <summary>Rows stored for the <c>commits</c> stream.</summary>
    public int CommitsCount { get; init; }

    /// <summary>Rows stored for the Playbook <c>events</c> stream.</summary>
    public int EventsCount { get; init; }

    /// <summary>
    /// Rows stored for the <c>misses</c> stream, summed across its three tables (REQ-FN-071).
    /// </summary>
    /// <remarks>
    /// One count for one stream: <c>misses.jsonl</c> is a single file whose records land in
    /// <c>"Miss"</c>, <c>"MissFix"</c> and <c>"MissAmend"</c>, so Coverage's per-repo stream table goes
    /// from four rows to five rather than to seven. A repository that does not emit the file keeps this
    /// at zero — the same fact a 404 states, and never an error (BRD-112).
    /// </remarks>
    public int MissesCount { get; init; }

    /// <summary>
    /// Session records ingest discarded because another record already carried that <c>session_id</c>.
    /// </summary>
    /// <remarks>
    /// The other five counts answer "how many rows are stored" and can always be recomputed with
    /// <c>COUNT(*)</c>. This one cannot: the collapsed records were never written, so by the time the
    /// metrics engine reads the store they are gone and a read-time count would be zero. It is a
    /// property of ingest and is persisted as one, which is what lets the export report
    /// <c>pooled.session_duplicates_collapsed</c> in agreement with the reference (REQ-FN-063).
    /// A <c>rebuild</c> replays the whole raw archive and is authoritative, so it <b>sets</b> this; an
    /// incremental sync ingests new files on top of what is stored, so it <b>adds</b> to it.
    /// </remarks>
    public int SessionDuplicatesCollapsed { get; init; }
}

/// <summary>The outcome of syncing one repository.</summary>
public enum SyncOutcome
{
    /// <summary>The telemetry SHA was unchanged; no files were fetched.</summary>
    Skipped = 0,

    /// <summary>New content was fetched, archived and parsed.</summary>
    Updated = 1,

    /// <summary>The repository failed to sync; the other repositories were unaffected.</summary>
    Error = 2
}

/// <summary>One repository's line in a <see cref="SyncReport"/>.</summary>
/// <param name="Repo"><c>owner/name</c> of the repository.</param>
/// <param name="Outcome">What happened to it.</param>
/// <param name="Sha">The telemetry SHA at the end of the attempt, when known.</param>
/// <param name="RecordsWritten">Rows written across all streams during this attempt.</param>
/// <param name="Error">Redacted failure message when <paramref name="Outcome"/> is <see cref="SyncOutcome.Error"/>.</param>
public sealed record RepoSyncResult(
    string Repo,
    SyncOutcome Outcome,
    string? Sha,
    int RecordsWritten,
    string? Error);

/// <summary>The result of one sync pass over a set of repositories.</summary>
/// <param name="UserId">The user whose repositories were synced, or <c>null</c> for a poller pass over every user.</param>
/// <param name="Results">One line per repository attempted.</param>
/// <param name="StartedTs">ISO-8601 timestamp the pass started.</param>
/// <param name="EndedTs">ISO-8601 timestamp the pass ended.</param>
public sealed record SyncReport(
    int? UserId,
    IReadOnlyList<RepoSyncResult> Results,
    string StartedTs,
    string EndedTs)
{
    /// <summary>Repositories that fetched new content.</summary>
    public int UpdatedCount => Results.Count(aR => aR.Outcome == SyncOutcome.Updated);

    /// <summary>Repositories whose telemetry SHA was unchanged.</summary>
    public int SkippedCount => Results.Count(aR => aR.Outcome == SyncOutcome.Skipped);

    /// <summary>Repositories that failed; failures are per-repository and never fatal.</summary>
    public int ErrorCount => Results.Count(aR => aR.Outcome == SyncOutcome.Error);
}
