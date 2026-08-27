namespace TfLens.Core.Contracts;

/// <summary>What AppManager returns on a successful sign-in, registration or refresh.</summary>
/// <param name="UserId">The AppManager user id — the only user key TfLens stores.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="FirstName">The user's given name.</param>
/// <param name="LastName">The user's family name.</param>
/// <param name="ApplicationRole">The role within Application Id 1; always <c>Manager</c> for TfLens.</param>
/// <param name="AccessToken">Bearer token for AppManager calls; kept server-side only.</param>
/// <param name="RefreshToken">Token used to renew the access token; rotated on every refresh.</param>
/// <param name="TokenExpiresAt">ISO-8601 expiry of <paramref name="AccessToken"/>.</param>
public sealed record AuthResponseData(
    int UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string ApplicationRole,
    string AccessToken,
    string RefreshToken,
    string TokenExpiresAt)
{
    /// <summary>The name shown in the header user menu, falling back to the email's local part.</summary>
    public string DisplayName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(aN => !string.IsNullOrWhiteSpace(aN))) is { Length: > 0 } vName
            ? vName
            : Email.Split('@')[0];
}

/// <summary>The fields a registration submits.</summary>
/// <param name="Email">The new user's email address.</param>
/// <param name="Password">The plaintext password; encrypted before transport, never stored or logged.</param>
/// <param name="FirstName">The user's given name.</param>
/// <param name="LastName">The user's family name.</param>
public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName);

/// <summary>The AppManager profile shown, read-only, on the Profile page.</summary>
/// <param name="UserId">The AppManager user id.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="FirstName">The user's given name.</param>
/// <param name="LastName">The user's family name.</param>
/// <param name="ApplicationRole">The role within the application; always <c>Manager</c>.</param>
/// <param name="MemberSince">ISO-8601 date the account was created.</param>
/// <param name="IdentityProvider">Where the identity lives; always <c>AppManager</c> in this release.</param>
public sealed record UserProfile(
    int UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string ApplicationRole,
    string? MemberSince,
    string IdentityProvider);

/// <summary>
/// An error AppManager reported, carrying its documented code.
/// </summary>
/// <remarks>
/// The UI renders a generic message for every code except <c>ACCOUNT_LOCKED</c>; the code itself is
/// logged, never displayed, so a failed sign-in cannot be used to enumerate accounts (BRD-90).
/// </remarks>
public sealed class AppManagerException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="aCode">The documented AppManager error code, e.g. <c>INVALID_CREDENTIALS</c>.</param>
    /// <param name="aMessage">A message for the log; never rendered to the user verbatim.</param>
    /// <param name="aStatusCode">The HTTP status AppManager answered with.</param>
    /// <param name="aInner">The underlying exception, when there was one.</param>
    public AppManagerException(string aCode, string aMessage, int aStatusCode = 0, Exception? aInner = null)
        : base(aMessage, aInner)
    {
        Code = aCode;
        StatusCode = aStatusCode;
    }

    /// <summary>The documented AppManager error code.</summary>
    public string Code { get; }

    /// <summary>The HTTP status AppManager answered with, or zero when the call never completed.</summary>
    public int StatusCode { get; }

    /// <summary>Codes the UI is allowed to surface a specific message for.</summary>
    public static class Codes
    {
        /// <summary>Email or password wrong — rendered generically.</summary>
        public const string InvalidCredentials = "INVALID_CREDENTIALS";

        /// <summary>Too many failed attempts; the one code with its own user-facing message.</summary>
        public const string AccountLocked = "ACCOUNT_LOCKED";

        /// <summary>The account exists but is disabled — rendered generically.</summary>
        public const string AccountDisabled = "ACCOUNT_DISABLED";

        /// <summary>The refresh token can no longer renew the session; the user must sign in again.</summary>
        public const string ExpiredRefreshToken = "EXPIRED_REFRESH_TOKEN";

        /// <summary>AppManager could not decrypt the password field — rendered generically.</summary>
        public const string DecryptionFailed = "DECRYPTION_FAILED";

        /// <summary>The submitted fields failed AppManager's rules — rendered generically.</summary>
        public const string ValidationError = "VALIDATION_ERROR";

        /// <summary>The user has no access to Application Id 1.</summary>
        public const string NoAppAccess = "NO_APP_ACCESS";

        /// <summary>The email is already registered — rendered as a field error.</summary>
        public const string DuplicateEmail = "DUPLICATE_EMAIL";

        /// <summary>The reset token is unknown or expired.</summary>
        public const string InvalidResetToken = "INVALID_RESET_TOKEN";

        /// <summary>The reset token belongs to a different application.</summary>
        public const string AppIdMismatch = "APP_ID_MISMATCH";

        /// <summary>The supplied current password was wrong — rendered as a field error.</summary>
        public const string InvalidCurrentPassword = "INVALID_CURRENT_PASSWORD";
    }
}

/// <summary>What GitHub says about a repository at connect time.</summary>
/// <param name="Owner">GitHub owner.</param>
/// <param name="Name">GitHub repository name.</param>
/// <param name="IsPrivate">True when the repository is private; such repositories are refused (BRD-100).</param>
/// <param name="DefaultBranch">The branch used when the user names none.</param>
public sealed record GitHubRepoInfo(string Owner, string Name, bool IsPrivate, string DefaultBranch);

/// <summary>The outcome of checking a candidate repository, one line per check the dialog shows.</summary>
/// <param name="Exists">The repository resolved on GitHub.</param>
/// <param name="IsPublic">The repository is public.</param>
/// <param name="TelemetryPath">The telemetry directory found, or <c>null</c> when neither framework's path exists.</param>
/// <param name="Framework">The framework detected from the telemetry path, or <c>null</c>.</param>
/// <param name="Branch">The branch that will be read.</param>
/// <param name="AlreadyConnected">This user has already connected this repository (BRD-104).</param>
/// <param name="Message">A user-facing explanation when the candidate is refused.</param>
public sealed record RepoValidation(
    bool Exists,
    bool IsPublic,
    string? TelemetryPath,
    string? Framework,
    string? Branch,
    bool AlreadyConnected,
    string? Message)
{
    /// <summary>True only when every check passed and the repository may be connected.</summary>
    public bool IsConnectable => Exists && IsPublic && TelemetryPath is not null && !AlreadyConnected;
}

/// <summary>
/// What one parsed stream file yielded.
/// </summary>
/// <remarks>
/// A malformed line is counted in <see cref="InvalidLines"/> and skipped, mirroring <c>read_stream</c>
/// in the reference. Fields SCHEMA.md does not document are preserved per record in its
/// <c>Overflow</c> column and named once in <see cref="UnknownFields"/> for the Coverage report.
/// </remarks>
public sealed record ParseResult
{
    /// <summary>The user the records belong to.</summary>
    public required int UserId { get; init; }

    /// <summary><c>owner/name</c> of the source repository.</summary>
    public required string Repo { get; init; }

    /// <summary>The SHA the file was fetched at.</summary>
    public required string SourceSha { get; init; }

    /// <summary>Which stream was parsed.</summary>
    public required StreamKind Stream { get; init; }

    /// <summary>Run records, when <see cref="Stream"/> is <see cref="StreamKind.Runs"/>.</summary>
    public IReadOnlyList<RunRecord> Runs { get; init; } = [];

    /// <summary>Gate records, when <see cref="Stream"/> is <see cref="StreamKind.Gates"/>.</summary>
    public IReadOnlyList<GateRecord> Gates { get; init; } = [];

    /// <summary>Session records, after the keep-highest-output-tokens dedupe.</summary>
    public IReadOnlyList<SessionRecord> Sessions { get; init; } = [];

    /// <summary>Commit records, after the dedupe on <c>sha</c>.</summary>
    public IReadOnlyList<CommitRecord> Commits { get; init; } = [];

    /// <summary>Playbook event records, when <see cref="Stream"/> is <see cref="StreamKind.Events"/>.</summary>
    public IReadOnlyList<PbEventRecord> PbEvents { get; init; } = [];

    /// <summary>Lines that were not valid JSON; counted and skipped, never fatal.</summary>
    public int InvalidLines { get; init; }

    /// <summary>Records collapsed by the stream's natural-key dedupe.</summary>
    public int DuplicatesCollapsed { get; init; }

    /// <summary>Field names seen that SCHEMA.md does not document, for the Coverage report.</summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = [];

    /// <summary>Records whose schema version was greater than 1 — a hard warning on Coverage.</summary>
    public int RecordsAboveSchemaV1 { get; init; }

    /// <summary>Total records stored from this file, across every record type.</summary>
    public int RecordCount => Runs.Count + Gates.Count + Sessions.Count + Commits.Count + PbEvents.Count;
}

/// <summary>What a rebuild-from-raw replayed.</summary>
/// <param name="FilesReplayed">Raw archive files re-parsed.</param>
/// <param name="RecordsWritten">Rows written across every stream.</param>
/// <param name="DuplicatesCollapsed">Records collapsed by the natural-key dedupes.</param>
/// <param name="InvalidLines">Malformed lines skipped.</param>
/// <param name="StartedTs">ISO-8601 timestamp the rebuild started.</param>
/// <param name="EndedTs">ISO-8601 timestamp the rebuild ended.</param>
public sealed record RebuildReport(
    int FilesReplayed,
    int RecordsWritten,
    int DuplicatesCollapsed,
    int InvalidLines,
    string StartedTs,
    string EndedTs);

/// <summary>The per-harness comparison shown on the Harness page.</summary>
/// <param name="Columns">One column per detected harness, in the fixed order claude-code, opencode, codex.</param>
/// <param name="NotDetectedRecords">Records whose <c>harness</c> is null — a footnote, never a column (ADR-017).</param>
/// <param name="OpenCodeCostUsd">The only measured dollars in the system; null when there are no OpenCode records.</param>
public sealed record HarnessComparison(
    IReadOnlyList<HarnessColumn> Columns,
    int NotDetectedRecords,
    decimal? OpenCodeCostUsd);

/// <summary>One harness's column of the comparison.</summary>
/// <param name="Harness">The harness name — <c>claude-code</c>, <c>opencode</c> or <c>codex</c>.</param>
/// <param name="Runs">Run records attributed to it.</param>
/// <param name="RunsByCmd">Its top commands by run count.</param>
/// <param name="GateRecords">Gate records attributed to it.</param>
/// <param name="VerdictMix">Verdict counts, highest first.</param>
/// <param name="Sessions">Session records attributed to it.</param>
/// <param name="TokensIn">Input tokens.</param>
/// <param name="TokensOut">Output tokens.</param>
/// <param name="TokensCacheRead">Cache-read tokens.</param>
/// <param name="TokensCacheWrite">Cache-write tokens.</param>
/// <param name="TokensPerVerifiedReq">Tokens per Verified verdict for this harness.</param>
public sealed record HarnessColumn(
    string Harness,
    int Runs,
    IReadOnlyList<KeyValuePair<string, int>> RunsByCmd,
    int GateRecords,
    IReadOnlyList<KeyValuePair<string, int>> VerdictMix,
    int Sessions,
    long TokensIn,
    long TokensOut,
    long TokensCacheRead,
    long TokensCacheWrite,
    Figure TokensPerVerifiedReq);

/// <summary>The routing and economics view. Every money figure here is an estimate, and says so.</summary>
/// <param name="RunsWithRoutingFields">Runs that carry routing fields at all.</param>
/// <param name="UnroutedRuns">Runs whose <c>routed</c> is false — the drift signal.</param>
/// <param name="DistinctModels">Distinct observed model names.</param>
/// <param name="Drift">One row per run with routing fields, unrouted ones first.</param>
/// <param name="TokensByModel">Token totals per observed model.</param>
/// <param name="ActualMixUsd">Estimated cost of the observed model mix.</param>
/// <param name="AllAtMaxUsd">Estimated cost had every run used the most expensive model.</param>
/// <param name="MostExpensiveModel">The model <paramref name="AllAtMaxUsd"/> reprices to.</param>
/// <param name="RunsExcludedNoTokenScope">Runs excluded from repricing because <c>tokens_scope</c> is none.</param>
/// <param name="MissingPriceModels">Observed models with no entry in <c>prices.json</c>.</param>
public sealed record RoutingAnalysis(
    int RunsWithRoutingFields,
    int UnroutedRuns,
    int DistinctModels,
    IReadOnlyList<DriftRow> Drift,
    IReadOnlyList<ModelTokens> TokensByModel,
    decimal? ActualMixUsd,
    decimal? AllAtMaxUsd,
    string? MostExpensiveModel,
    int RunsExcludedNoTokenScope,
    IReadOnlyList<string> MissingPriceModels)
{
    /// <summary>The counterfactual delta — what routing saved, as an estimate.</summary>
    public decimal? DeltaUsd => AllAtMaxUsd is { } vMax && ActualMixUsd is { } vActual ? vMax - vActual : null;
}

/// <summary>One run's routing facts.</summary>
/// <param name="Ts">ISO-8601 timestamp of the run.</param>
/// <param name="Cmd">The phase command that ran.</param>
/// <param name="Tier">The routing tier requested.</param>
/// <param name="TierModel">The model the tier was expected to resolve to.</param>
/// <param name="Model">The model actually observed.</param>
/// <param name="Models">Every model observed in the run.</param>
/// <param name="Routed">False when the request bypassed the tier.</param>
public sealed record DriftRow(
    string Ts,
    string? Cmd,
    string? Tier,
    string? TierModel,
    string? Model,
    string? Models,
    bool? Routed);

/// <summary>Token totals for one observed model.</summary>
/// <param name="Model">The model name.</param>
/// <param name="TokensIn">Input tokens.</param>
/// <param name="TokensOut">Output tokens.</param>
/// <param name="TokensCacheRead">Cache-read tokens.</param>
/// <param name="TokensCacheWrite">Cache-write tokens.</param>
public sealed record ModelTokens(
    string Model,
    long TokensIn,
    long TokensOut,
    long TokensCacheRead,
    long TokensCacheWrite)
{
    /// <summary>All four counts added together.</summary>
    public long Total => TokensIn + TokensOut + TokensCacheRead + TokensCacheWrite;
}

/// <summary>One written snapshot and the stamp that says whether its numbers are quotable.</summary>
/// <param name="UserId">The user the snapshot belongs to.</param>
/// <param name="Framework">The provenance axis; one snapshot per framework.</param>
/// <param name="Date">The report date, which is also the folder name.</param>
/// <param name="MarkdownPath">Absolute path of <c>snapshot.md</c>.</param>
/// <param name="JsonPath">Absolute path of <c>tflens.json</c>.</param>
/// <param name="ParserVersion">The parser version that produced it.</param>
/// <param name="ParityStatus">Whether the last parity run covers this parser version.</param>
/// <param name="DatasetShas">The repository SHAs the figures were computed from.</param>
public sealed record SnapshotResult(
    int UserId,
    string Framework,
    DateOnly Date,
    string MarkdownPath,
    string JsonPath,
    string ParserVersion,
    string ParityStatus,
    IReadOnlyList<KeyValuePair<string, string>> DatasetShas);

/// <summary>The parity statuses the export banner distinguishes.</summary>
public static class ParityStatuses
{
    /// <summary>A parity run passed against this parser version — the figures may be quoted.</summary>
    public const string Quotable = "QUOTABLE";

    /// <summary>The parser changed after the last parity run — re-run the procedure before quoting.</summary>
    public const string NotQuotable = "NOT QUOTABLE";

    /// <summary>No parity run has ever been recorded.</summary>
    public const string NeverRun = "NEVER RUN";
}
