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

    // REMOVED 2026-08-29 (owner report, MISS-TfLens-20260829-23): `LocalDevelopmentConnection`, a
    // `const string` holding a full localhost connection string with its password inline.
    // It was a credential in committed source, and it silently pinned every unconfigured developer to
    // one specific database — see the note in Program.cs. `DbConnection` below now has no default in
    // any environment: user secrets in development, `TfLensDbConnection` in deployment, and a missing
    // value is a startup failure, not a guess.


    /// <summary>Base URL of the AppManager API.</summary>
    public string AppManagerBaseUrl { get; set; } = "https://appmgrapi.techierathore.com";

    /// <summary>
    /// AppManager Application Id; TfLens is application 1. Supplied by user secrets
    /// (<c>TfLens:AppManagerAppId</c>) in development and <c>TfLensAppManagerAppId</c> in deployment.
    /// </summary>
    /// <remarks>
    /// 2026-08-29, owner report (MISS-TfLens-20260829-23): the value now lives in the user-secrets
    /// store rather than only in source, and `docker-compose.yml` sets it explicitly. The `= 1` default
    /// STAYS. It was briefly made required in the same change as the database connection string, and
    /// that was wrong to lump together: an application id is a public identifier, not a credential, and
    /// unlike the connection string it pins nothing about the developer's machine. Requiring it broke
    /// 48 tests that legitimately rely on the documented default and bought no safety.
    /// The thing that actually mattered — a database password in committed source that silently pinned
    /// every unconfigured developer to one specific server — is gone and stays gone.
    /// </remarks>
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

    /// <summary>
    /// Path of the reference implementation <c>tf-metrics.sh</c>, whose hash the parity stamp is
    /// checked against (REQ-FN-063, BRD-71).
    /// </summary>
    /// <remarks>
    /// <c>data/parity-last.json</c> records the SHA-256 of the script the passing run was compared
    /// against, and a figure is only quotable while that hash still describes the script on disk — a
    /// reference change invalidates the stamp exactly as a parser change does. The path is configurable
    /// because the oracle lives outside <see cref="DataRoot"/> and many deployments will not ship it at
    /// all; when the file is absent or unreadable the stamp degrades to
    /// <see cref="Contracts.ParityStatuses.NotQuotable"/> with reason
    /// <see cref="Contracts.ParityReasons.ScriptUnavailable"/>, never to quotable, because an
    /// unverifiable claim is not a verified one. It is resolved relative to the process working
    /// directory when it is not rooted.
    /// </remarks>
    public string ReferenceScriptPath { get; set; } = Path.Combine(".tfcore", "telemetry", "tf-metrics.sh");

    /// <summary>
    /// <see cref="ReferenceScriptPath"/> resolved to a path that actually exists, or the plain
    /// working-directory reading when it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rooted setting is returned untouched — an operator who names an absolute path means it.
    /// A <b>relative</b> default, though, cannot simply be resolved against the working directory:
    /// <see cref="DataRoot"/> is also relative and is only correct when the process runs from
    /// <c>src/TfLens</c>, whereas <c>.tfcore/</c> lives at the repository root. Both cannot be right
    /// against one working directory, so a bare <c>dotnet run</c> found no oracle and <c>/export</c>
    /// reported <see cref="Contracts.ParityReasons.ScriptUnavailable"/> — "the reference script cannot
    /// be hashed" — even with a valid, passing parity record on disk. The banner was wrong about its
    /// own evidence, which is the one thing it exists to be right about.
    /// </para>
    /// <para>
    /// So a relative setting is probed against the working directory and then against each ancestor,
    /// which finds a repository-root <c>.tfcore/</c> from any subdirectory a head might run in. This
    /// deliberately does <b>not</b> fall back to the <c>script_path</c> recorded inside
    /// <c>parity-last.json</c>: letting the stamp nominate the file that proves the stamp would make
    /// the record self-attesting. When nothing is found the original relative reading is returned, so
    /// a genuinely missing oracle still reads not-quotable exactly as before.
    /// </para>
    /// </remarks>
    /// <returns>The first existing candidate, else the working-directory reading.</returns>
    public string ResolveReferenceScriptPath()
    {
        if (string.IsNullOrWhiteSpace(ReferenceScriptPath) || Path.IsPathRooted(ReferenceScriptPath))
        {
            return ReferenceScriptPath;
        }

        var vFromWorkingDirectory = Path.GetFullPath(ReferenceScriptPath);
        if (File.Exists(vFromWorkingDirectory))
        {
            return vFromWorkingDirectory;
        }

        for (var vDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
             vDirectory is not null;
             vDirectory = vDirectory.Parent)
        {
            var vCandidate = Path.Combine(vDirectory.FullName, ReferenceScriptPath);
            if (File.Exists(vCandidate))
            {
                return vCandidate;
            }
        }

        return vFromWorkingDirectory;
    }

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

                The connection string IS configured, so this is about the server it names. In order of
                likelihood:

                  1. Your PostgreSQL server is not running. Start the one you already use — TfLens does
                     not start a database for you and does not assume one exists.

                  2. The host or port does not match that server. Check what it actually publishes.

                  3. The database named in the connection string does not exist on it. Create it empty;
                     the schema is applied at startup.

                  4. The credentials are wrong, or that user cannot create tables in that database.

                  5. Windows with Docker inside WSL: localhost normally works via WSL2 port forwarding.
                     If it does not, try 127.0.0.1 explicitly.

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
        // A NEUTRAL example, deliberately. It used to be the compose container's own connection string
        // (port 5433, password tflensdev), which read as "this is the database to use" and is part of
        // how a second PostgreSQL server ended up beside the machine's real one
        // (MISS-TfLens-20260829-23). The message now shows the SHAPE and lets the developer name their
        // own server.
        const string vExample = "Host=localhost;Port=<port>;Database=tflens;Username=<user>;Password=<password>";
        var vIsWindows = OperatingSystem.IsWindows();

        var vHowTo = vIsWindows
            ? $"""
               Point this at YOUR PostgreSQL server — TfLens ships no default and starts no database
               for you. Store it in user secrets, which is per-machine and never committed:

                 dotnet user-secrets set TfLens:DbConnection "{vExample}" --project src\TfLens

               Or, for one PowerShell session only:
                 $env:TfLensDbConnection = "{vExample}"
                 dotnet run --project src\TfLens
               """
            : $"""
               Point this at YOUR PostgreSQL server — TfLens ships no default and starts no database
               for you. Store it in user secrets, which is per-machine and never committed:

                 dotnet user-secrets set TfLens:DbConnection "{vExample}" --project src/TfLens

               Or, for one shell session only:
                 export TfLensDbConnection="{vExample}"
                 dotnet run --project src/TfLens
               """;

        return $"""
                TfLens cannot start — the database connection string is not configured.

                This is expected on a fresh clone, and it is deliberate: TfLens ships NO default
                connection string in any environment. It refuses to start rather than guess a server,
                because a database nobody chose is worse than an error somebody reads (BRD-9).

                1. USE A POSTGRESQL SERVER YOU ALREADY RUN. Do not stand up a new one for this project.
                   Create an empty database named `tflens` on it — you do not need to create the schema,
                   database/001-schema.sql is applied at every startup.

                   (`docker compose up` runs TfLens with its own PostgreSQL, together, as a deployment
                   would. That is a different thing from local development and is not this step.)

                2. TELL TfLens WHERE IT IS.

                {vHowTo}

                The setting name is PascalCase with no separators — `TfLensDbConnection`, not
                `TFLENS_DB_CONNECTION` and not `TfLens__DbConnection` (see docs/TfLens-Coding-Standards.md).

                Full setup is in docs/TfLens-DevGuide.md (§Running TfLens locally).
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
    /// <remarks>
    /// <para>
    /// <b>1.2.0 (2026-08-28)</b> — minor bump: the export gained the whole <c>misses</c> block plus
    /// <c>per_repo[].misses</c> and <c>per_repo[].stale_types</c>. The reference gained them when
    /// <c>tf-metrics.sh</c> learned to read the <c>misses</c> stream (<c>analyse_misses()</c>), and
    /// TfLens must emit every key the reference emits or the parity compare fails on a MISSING key.
    /// Metrics were added and nothing stored changed meaning, so this is minor, not major (D-005) —
    /// see <c>DECISIONS.md</c> §6 P-003.
    /// </para>
    /// <para>
    /// <b>1.1.0 (2026-08-27)</b> — minor bump: the export gained a metric,
    /// <c>pooled.session_duplicates_collapsed</c>. Per <c>DECISIONS.md</c> D-005 a metric being added is
    /// a minor bump — old exports stay comparable for the metrics they do carry, and nothing stored
    /// changed meaning. The reference gained the same figure when <c>tf-metrics.sh</c> learned to
    /// de-duplicate the sessions stream, and TfLens must emit every key the reference does or the
    /// parity compare fails on a MISSING key.
    /// </para>
    /// <para>
    /// Bumping this deliberately <b>un-quotes</b> every export until a fresh parity run is recorded
    /// (D-005), which is the whole point of stamping it: a figure is quotable only when the run on
    /// record postdates the parser that produced it.
    /// </para>
    /// <para><b>1.0.0 (2026-08-26)</b> — the first shipping parser.</para>
    /// </remarks>
    public const string Current = "1.2.0";
}
