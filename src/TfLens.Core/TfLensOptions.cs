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

    /// <summary>
    /// The connection string a freshly cloned repository falls back to in Development.
    /// </summary>
    /// <remarks>
    /// It points at the PostgreSQL 16 service in <c>docker-compose.yml</c>, published on 5433 by
    /// <c>docker-compose.override.yml</c>, with the throwaway password from <c>.env.example</c>. It is
    /// seeded as the lowest-priority configuration source in Development only, so user secrets and
    /// environment variables both override it, and no deployment ever sees it. Nothing here is a
    /// secret: it is a local container credential already published in the repository.
    /// </remarks>
    public const string LocalDevelopmentConnection =
        "Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=tflensdev";

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
            throw new InvalidOperationException(MissingDbConnectionMessage());
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

    /// <summary>
    /// Builds the message a developer sees when the connection string is set but nothing answers.
    /// </summary>
    /// <remarks>
    /// Without this the failure surfaces as a raw <c>Npgsql</c> socket stack trace, which names neither
    /// the container to start nor the file that publishes the port. The underlying exception is still
    /// chained as the inner exception and still logged in full — this only puts the actionable part first.
    /// </remarks>
    /// <param name="aCause">The underlying failure, when there was one.</param>
    /// <returns>An actionable message naming the likely causes in order.</returns>
    public static string UnreachableDatabaseMessage(Exception? aCause)
    {
        var vDetail = aCause is null
            ? "The database accepted a connection but did not answer a health check."
            : $"The database did not answer: {aCause.GetType().Name}: {FirstLine(aCause.Message)}";

        return $"""
                TfLens cannot start — the database named by TfLensDbConnection is unreachable.

                {vDetail}

                The connection string IS configured, so this is about the database itself. In order of
                likelihood:

                  1. The database is not running. From the repository root:
                       docker compose up -d postgres
                     Then check it:  docker ps --filter name=tflens-postgres

                  2. There is no .env file, so Compose could not resolve TfLensDbPassword and never
                     started the service. Copy .env.example to .env.

                  3. The port is not published. The production compose file deliberately keeps Postgres
                     off the host network; docker-compose.override.yml publishes 5433:5432 for local
                     development and Compose merges it automatically. Running with
                     `-f docker-compose.yml` alone skips that override.

                  4. Windows with Docker inside WSL: localhost:5433 normally works via WSL2 port
                     forwarding. If it does not, check Docker Desktop's WSL integration.

                  5. Something else already owns the port, or the credentials do not match the ones the
                     container was created with. A container created with a different TfLensDbPassword
                     keeps the OLD password until its pgdata volume is removed.

                Troubleshooting in full: docs/TfLens-DevGuide.md (§Troubleshooting).
                """;
    }

    /// <summary>Keeps only the first line of a message, so a multi-line driver dump stays readable.</summary>
    /// <param name="aMessage">The exception message.</param>
    /// <returns>The first line.</returns>
    private static string FirstLine(string aMessage)
    {
        var vBreak = aMessage.IndexOfAny(['\r', '\n']);
        return vBreak < 0 ? aMessage : aMessage[..vBreak];
    }

    /// <summary>
    /// Builds the message a developer sees when <c>TfLensDbConnection</c> is not set.
    /// </summary>
    /// <remarks>
    /// This is deliberately long. It is the first thing anyone who clones the repository and presses
    /// F5 will see, and a message that only says "supply it as an environment variable" tells them
    /// neither what value to supply, nor where, nor how on their platform — which is exactly how a
    /// working application reads as a broken one.
    /// </remarks>
    /// <returns>An actionable, platform-aware message.</returns>
    private static string MissingDbConnectionMessage()
    {
        const string vExample = "Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=tflensdev";
        var vIsWindows = OperatingSystem.IsWindows();

        var vHowTo = vIsWindows
            ? $"""
               In Visual Studio / Rider, pick the launch profile "TfLens (local compose DB)" — it sets
               this for you. If you are on the "TfLens (own database)" profile, either switch profiles or
               set the value yourself:

                 Debug > TfLens Debug Properties > Environment variables
                   TfLensDbConnection = {vExample}

               Or from PowerShell, for this session:
                 $env:TfLensDbConnection = "{vExample}"
                 dotnet run --project src\TfLens

               Or store it for this machine only (never committed):
                 dotnet user-secrets set TfLens:DbConnection "{vExample}" --project src\TfLens
               """
            : $"""
               From a shell:
                 export TfLensDbConnection="{vExample}"
                 dotnet run --project src/TfLens

               Or store it for this machine only (never committed):
                 dotnet user-secrets set TfLens:DbConnection "{vExample}" --project src/TfLens
               """;

        return $"""
                TfLens cannot start — the database connection string is not configured.

                TfLens needs a PostgreSQL 16 database. Nothing is wrong with your build; the app refuses
                to start without a database rather than failing later at the first user's sign-in (BRD-9).

                1. START THE DATABASE (once). From the repository root:

                     docker compose up -d postgres

                   That runs PostgreSQL 16 as the container `tflens-postgres` and publishes it on
                   localhost:5433 via docker-compose.override.yml. It needs TfLensDbPassword set — copy
                   .env.example to .env first; the committed example already uses `tflensdev`.

                   Already have your own PostgreSQL? Skip this and point the connection string at it.
                   The schema is applied automatically at startup (database/001-schema.sql).

                2. TELL TfLens WHERE IT IS.

                {vHowTo}

                The setting name is PascalCase with no separators — `TfLensDbConnection`, not
                `TFLENS_DB_CONNECTION` and not `TfLens__DbConnection` (see docs/TfLens-Coding-Standards.md).

                Full setup, including the AppManager settings and the Docker path, is in README.md
                (§Configuration) and docs/TfLens-DevGuide.md (§Running TfLens locally).
                """;
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
