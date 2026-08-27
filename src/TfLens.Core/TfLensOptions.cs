namespace TfLens.Core;

/// <summary>
/// Everything TfLens reads from configuration, bound from the <c>TfLens</c> section.
/// </summary>
/// <remarks>
/// Secrets reach these properties only through the PascalCase environment-variable provider
/// (<c>TfLensAppManagerApiKey</c>, <c>TfLensAppManagerApiSecret</c>, <c>TfLensDbConnection</c>,
/// optionally <c>TfLensGitHubToken</c>) — never from <c>appsettings.json</c>, never from the repository
/// (BRD-8, BRD-10). The app refuses to start when a required secret is missing (BRD-9).
/// </remarks>
public sealed class TfLensOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "TfLens";

    /// <summary>Base URL of the AppManager API.</summary>
    public string AppManagerBaseUrl { get; set; } = "https://appmgrapi.techierathore.com";

    /// <summary>AppManager Application Id; TfLens is application 1.</summary>
    public int AppManagerAppId { get; set; } = 1;

    /// <summary>AppManager API key. Required; env var <c>TfLensAppManagerApiKey</c>.</summary>
    public string? AppManagerApiKey { get; set; }

    /// <summary>AppManager API secret. Required; env var <c>TfLensAppManagerApiSecret</c>.</summary>
    public string? AppManagerApiSecret { get; set; }

    /// <summary>PostgreSQL connection string. Required; env var <c>TfLensDbConnection</c>.</summary>
    public string? DbConnection { get; set; }

    /// <summary>Optional GitHub PAT. Raises the rate limit only — it grants no additional repository access.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>Root of the persistent volume holding <c>raw/</c>, <c>reports/</c> and <c>prices.json</c>.</summary>
    public string DataRoot { get; set; } = "data";

    /// <summary>How often the background poller syncs every user's repositories.</summary>
    public int PollIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// How old a repository's newest <c>sessions</c> or <c>commits</c> record may be before Coverage
    /// calls that clone stale, in days (BRD-41, REQ-UI-015).
    /// </summary>
    /// <remarks>
    /// The threshold is configurable because "stale" is a judgement about a team's cadence, not a fact
    /// about the data; the Coverage warning renders the configured number rather than a hard-coded seven,
    /// so a project that pushes weekly can raise it without the page lying about what it measured.
    /// </remarks>
    public int StalenessDays { get; set; } = 7;

    /// <summary>Repositories seeded onto the demo account at first start (BRD-96).</summary>
    public IList<string> DemoSeedRepos { get; set; } = [];

    /// <summary>Email of the read-only demo account.</summary>
    public string DemoUserEmail { get; set; } = "TfLensDemo";

    /// <summary>
    /// True when both AppManager API-key headers are configured and may be sent.
    /// </summary>
    /// <remarks>
    /// The headers are optional on the AppManager side (guide §2.1) — the application is equally well
    /// resolved from the <c>applicationId</c> the client puts in every request body. What is *not*
    /// tolerated is half a pair: a key without its secret is rejected with <c>INVALID_API_KEY</c> on
    /// every call, so <see cref="Validate"/> refuses that configuration outright.
    /// </remarks>
    public bool HasAppManagerApiCredentials =>
        !string.IsNullOrWhiteSpace(AppManagerApiKey) && !string.IsNullOrWhiteSpace(AppManagerApiSecret);

    /// <summary>
    /// Throws when the configuration cannot produce a working process, so a misconfigured deployment
    /// fails at startup rather than at the first user's sign-in (BRD-9).
    /// </summary>
    /// <remarks>
    /// <see cref="DbConnection"/> is unconditionally required. The AppManager API-key pair must be
    /// supplied whole or not at all: a partial pair authenticates nothing and turns every sign-in into
    /// a 401, which is exactly the silent misconfiguration BRD-9 exists to prevent.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A required setting is missing, or the API-key pair is half-configured.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DbConnection))
        {
            throw new InvalidOperationException(
                "TfLens cannot start — TfLensDbConnection is not set. Supply it as a PascalCase environment variable.");
        }

        var vHasKey = !string.IsNullOrWhiteSpace(AppManagerApiKey);
        var vHasSecret = !string.IsNullOrWhiteSpace(AppManagerApiSecret);

        if (vHasKey != vHasSecret)
        {
            throw new InvalidOperationException(
                "TfLens cannot start — TfLensAppManagerApiKey and TfLensAppManagerApiSecret must be set together " +
                "or both left unset. A half-configured pair is rejected by AppManager on every call.");
        }
    }

    /// <summary>Absolute path of the raw archive for one user.</summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <returns>The user's raw-archive directory; the path itself is user-scoped (ADR-013).</returns>
    public string RawPath(int aUserId) => Path.Combine(DataRoot, "raw", aUserId.ToString());

    /// <summary>Absolute path of the reports folder for one user.</summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <returns>The user's reports directory.</returns>
    public string ReportsPath(int aUserId) => Path.Combine(DataRoot, "reports", aUserId.ToString());

    /// <summary>Path of the editable rate card used for counterfactual repricing.</summary>
    public string PricesPath => Path.Combine(DataRoot, "prices.json");

    /// <summary>Path of the record of the last parity run.</summary>
    public string ParityLastPath => Path.Combine(DataRoot, "parity-last.json");
}

/// <summary>
/// The parser version stamped into the build and into every export.
/// </summary>
/// <remarks>
/// BRD-68 — a figure must be traceable to the code that produced it, and the export banner compares
/// this against the version the last parity run covered. Bump it whenever parsing or the engine changes.
/// </remarks>
public static class ParserVersion
{
    /// <summary>The current parser version.</summary>
    public const string Current = "1.0.0";
}
