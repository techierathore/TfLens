using System.Text;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Integration.Tests;

/// <summary>
/// REQ-NFR-010 (BRD-102, BRD-83) — cross-user isolation is proven, not assumed.
/// </summary>
/// <remarks>
/// <para>
/// Two users connect the <b>same public repository</b> — the hardest case, because every row of both
/// users carries an identical <c>Repo</c> value, so any query that forgets <c>UserId</c> returns the
/// other user's data rather than nothing. Each user's rows are seeded with distinguishable values, so
/// a leak shows up as a wrong number and not merely as a wrong count.
/// </para>
/// <para>
/// BRD §16 names the risk this covers: "Cross-user data leak through a missed <c>UserId</c> filter."
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CrossUserIsolationTests : IAsyncLifetime
{
    /// <summary>Two synthetic user ids, far outside the AppManager range in use.</summary>
    private const int UserA = 990001;

    /// <summary>The second synthetic user.</summary>
    private const int UserB = 990002;

    /// <summary>The one public repository both users connect.</summary>
    private const string SharedRepo = "techierathore/TfLens";

    /// <summary>
    /// The two real AppManager accounts the UsageGuide documents, used for the over-HTTP half.
    /// </summary>
    /// <remarks>
    /// The synthetic ids above cannot sign in — the cookie is issued from a genuine AppManager response,
    /// so the HTTP proof needs accounts that actually exist. These are the canonical test users from
    /// <c>docs/TfLens-UsageGuide.md</c>; no account is invented here.
    /// </remarks>
    private const int DemoUserId = 2;

    /// <summary>The second documented account, for the other side of the isolation proof.</summary>
    private const int SecondUserId = 3;

    /// <summary>Email of the demo account.</summary>
    private const string DemoEmail = "tflensdemo@techierathore.com";

    /// <summary>Password of the demo account, as recorded in the UsageGuide.</summary>
    private const string DemoPassword = "TfLensDemo!23";

    /// <summary>Email of the second account.</summary>
    private const string SecondEmail = "tflenstest2@techierathore.com";

    /// <summary>Password of the second account, as recorded in the UsageGuide.</summary>
    private const string SecondPassword = "TfLensTest2!23";

    /// <summary>A repository whose name identifies the demo account as its owner.</summary>
    private const string DemoProbeRepo = "isolation-probe/belongs-to-user-two";

    /// <summary>A repository whose name identifies the second account as its owner.</summary>
    private const string SecondProbeRepo = "isolation-probe/belongs-to-user-three";

    /// <summary>
    /// Unique indexes whose key is globally unique by construction, with the reason each is safe.
    /// </summary>
    /// <remarks>
    /// The rule is that a unique index must carry <c>UserId</c>, because two users holding the same
    /// public repo would otherwise collide. The exception is a key that is already unique across the
    /// whole installation — a random session id cannot collide with another user's, and adding
    /// <c>UserId</c> to it would weaken the constraint rather than strengthen it, by permitting the
    /// same session id under two users.
    /// </remarks>
    private static readonly Dictionary<string, string> GloballyUniqueIndexes = new(StringComparer.Ordinal)
    {
        ["PkAuthSession"] = "SessionId is a random, installation-wide identifier; scoping it by user " +
                            "would allow one session id to exist twice"
    };

    private readonly PostgresFixture objDb;

    /// <summary>Creates the test class.</summary>
    /// <param name="aDb">The shared live-PostgreSQL fixture.</param>
    public CrossUserIsolationTests(PostgresFixture aDb)
    {
        objDb = aDb;
    }

    /// <summary>Seeds both users' data from a clean slate.</summary>
    /// <returns>A task that completes when the fixture rows exist.</returns>
    public async Task InitializeAsync()
    {
        if (!objDb.IsAvailable)
        {
            return;
        }

        await PurgeAsync();
        await SeedAsync(UserA, "aaaaaaa", aRuns: 3, aGates: 5, aSessions: 2, aCommits: 4, aEvents: 1);
        await SeedAsync(UserB, "bbbbbbb", aRuns: 7, aGates: 11, aSessions: 6, aCommits: 9, aEvents: 3);
    }

    /// <summary>Removes both users' rows so a re-run starts clean.</summary>
    /// <returns>A task that completes when the rows are gone.</returns>
    public async Task DisposeAsync()
    {
        if (objDb.IsAvailable)
        {
            await PurgeAsync();
        }
    }

    // ---------------------------------------------------------------- schema-level

    /// <summary>
    /// Every table that holds user data carries a <c>UserId</c> column.
    /// </summary>
    /// <remarks>
    /// The check that survives the schema growing: a new stream table added without <c>UserId</c>
    /// fails here on the day it is added, rather than on the day someone reads it unscoped.
    /// </remarks>
    [Fact]
    public async Task EveryUserDataTableCarriesAUserIdColumn()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        var vTables = (await vConnection.QueryAsync<string>(
                """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
                """))
            .OrderBy(aName => aName, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(vTables);

        var vMissing = new List<string>();

        foreach (var vTable in vTables)
        {
            var vHasUserId = await vConnection.ExecuteScalarAsync<bool>(
                """
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = @Table AND column_name = 'UserId')
                """,
                new { Table = vTable });

            if (!vHasUserId)
            {
                vMissing.Add(vTable);
            }
        }

        Assert.True(
            vMissing.Count == 0,
            $"REQ-NFR-010 — these tables hold data with no UserId column, so no read of them can be " +
            $"user-scoped: {string.Join(", ", vMissing)}");
    }

    /// <summary>
    /// Every dedupe index is scoped by <c>UserId</c>.
    /// </summary>
    /// <remarks>
    /// A unique index that omitted the user would make one user's re-parse collapse onto another
    /// user's row — a leak that looks like a *missing* record rather than an extra one, and would
    /// therefore never be noticed.
    /// </remarks>
    [Fact]
    public async Task EveryUniqueIndexIsScopedByUserId()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        var vDefinitions = await vConnection.QueryAsync<IndexRow>(
            """
            SELECT indexname AS "IndexName", indexdef AS "Definition"
            FROM pg_indexes
            WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE INDEX%'
            """);

        var vUnscoped = vDefinitions
            .Where(aIndex => !aIndex.Definition.Contains("\"UserId\"", StringComparison.Ordinal))
            .Select(aIndex => aIndex.IndexName)
            .OrderBy(aName => aName, StringComparer.Ordinal)
            .ToList();

        var vFindings = vUnscoped
            .Where(aName => !GloballyUniqueIndexes.ContainsKey(aName))
            .ToList();

        Assert.True(
            vFindings.Count == 0,
            $"REQ-NFR-010 — these unique indexes are not scoped by UserId, so two users' records can " +
            $"collide: {string.Join(", ", vFindings)}");

        // And the waiver has not gone stale: an index that no longer exists must leave the list, or
        // the list stops meaning anything.
        var vStale = GloballyUniqueIndexes.Keys.Where(aName => !vUnscoped.Contains(aName)).ToList();

        Assert.True(
            vStale.Count == 0,
            $"These indexes are waived as globally unique but no longer exist unscoped: " +
            $"{string.Join(", ", vStale)}");
    }

    // ---------------------------------------------------------------- data-level

    /// <summary>Two users may connect the same public repo, and each sees only their own row.</summary>
    /// <remarks>Also REQ-FN-019's second clause: the same <c>owner/name</c> across users is legal.</remarks>
    [Fact]
    public async Task TwoUsersConnectTheSameRepoAndSeeOnlyTheirOwnRow()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        var vBothRows = await vConnection.ExecuteScalarAsync<long>(
            """SELECT COUNT(*) FROM "UserRepo" WHERE "Repo" = @Repo AND "UserId" IN (@A, @B)""",
            new { Repo = SharedRepo, A = UserA, B = UserB });

        Assert.Equal(2, vBothRows);

        var vBranchForA = await vConnection.ExecuteScalarAsync<string>(
            """SELECT "Branch" FROM "UserRepo" WHERE "UserId" = @UserId AND "Repo" = @Repo""",
            new { UserId = UserA, Repo = SharedRepo });

        var vBranchForB = await vConnection.ExecuteScalarAsync<string>(
            """SELECT "Branch" FROM "UserRepo" WHERE "UserId" = @UserId AND "Repo" = @Repo""",
            new { UserId = UserB, Repo = SharedRepo });

        // Distinct values, so a query that dropped the UserId predicate would return the wrong one
        // rather than the right one by luck.
        Assert.Equal("branch-aaaaaaa", vBranchForA);
        Assert.Equal("branch-bbbbbbb", vBranchForB);
    }

    /// <summary>A user-scoped read of every stream table returns that user's rows and no others.</summary>
    [Theory]
    [InlineData("Run", 3, 7)]
    [InlineData("Gate", 5, 11)]
    [InlineData("Session", 2, 6)]
    [InlineData("Commit", 4, 9)]
    [InlineData("PbEvent", 1, 3)]
    public async Task ScopedStreamReadsNeverCrossTheUserBoundary(string aTable, int aCountA, int aCountB)
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        var vForA = await vConnection.ExecuteScalarAsync<long>(
            $"""SELECT COUNT(*) FROM "{aTable}" WHERE "UserId" = @UserId AND "Repo" = @Repo""",
            new { UserId = UserA, Repo = SharedRepo });

        var vForB = await vConnection.ExecuteScalarAsync<long>(
            $"""SELECT COUNT(*) FROM "{aTable}" WHERE "UserId" = @UserId AND "Repo" = @Repo""",
            new { UserId = UserB, Repo = SharedRepo });

        Assert.Equal((long)aCountA, vForA);
        Assert.Equal((long)aCountB, vForB);

        // And every row a user's read returns really is theirs.
        var vForeignRows = await vConnection.ExecuteScalarAsync<long>(
            $"""SELECT COUNT(*) FROM "{aTable}" WHERE "UserId" = @UserId AND "SourceSha" <> @Sha""",
            new { UserId = UserA, Sha = "aaaaaaa" });

        Assert.Equal(0, vForeignRows);
    }

    /// <summary>One user's sync state is invisible to the other, error text included.</summary>
    /// <remarks>
    /// <c>LastError</c> is the one free-text field the streams produce, so it is the field a leak
    /// would be most visible in — and the one REQ-FN-023 says must never carry a token or a URL
    /// credential either.
    /// </remarks>
    [Fact]
    public async Task SyncStateIsInvisibleAcrossUsers()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        var vErrorForB = await vConnection.ExecuteScalarAsync<string?>(
            """SELECT "LastError" FROM "SyncState" WHERE "UserId" = @UserId AND "Repo" = @Repo""",
            new { UserId = UserB, Repo = SharedRepo });

        Assert.Equal("403 rate limited (bbbbbbb)", vErrorForB);

        var vShaForA = await vConnection.ExecuteScalarAsync<string?>(
            """SELECT "LastSha" FROM "SyncState" WHERE "UserId" = @UserId AND "Repo" = @Repo""",
            new { UserId = UserA, Repo = SharedRepo });

        Assert.Equal("aaaaaaa", vShaForA);
    }

    /// <summary>One user's server-side session row is never returned by the other user's lookup.</summary>
    /// <remarks>
    /// The AppManager access and refresh tokens live in this table. A cross-user read here would not
    /// merely leak figures — it would hand over another user's identity at AppManager.
    /// </remarks>
    [Fact]
    public async Task AuthSessionRowsAreInvisibleAcrossUsers()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        var vForA = await vConnection.QueryAsync<string>(
            """SELECT "SessionId" FROM "AuthSession" WHERE "UserId" = @UserId""",
            new { UserId = UserA });

        Assert.Equal(new[] { "session-aaaaaaa" }, vForA.OrderBy(aId => aId, StringComparer.Ordinal).ToArray());

        var vForB = await vConnection.QueryAsync<string>(
            """SELECT "SessionId" FROM "AuthSession" WHERE "UserId" = @UserId""",
            new { UserId = UserB });

        Assert.Equal(new[] { "session-bbbbbbb" }, vForB.OrderBy(aId => aId, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Removing a repo from one user leaves the other user's copy of the same repo intact.</summary>
    /// <remarks>REQ-FN-016's second acceptance clause, which is only meaningful across users.</remarks>
    [Fact]
    public async Task RemovingARepoFromOneUserLeavesTheOtherUntouched()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        foreach (var vTable in new[] { "Run", "Gate", "Session", "Commit", "PbEvent", "SyncState", "UserRepo" })
        {
            await vConnection.ExecuteAsync(
                $"""DELETE FROM "{vTable}" WHERE "UserId" = @UserId AND "Repo" = @Repo""",
                new { UserId = UserA, Repo = SharedRepo });
        }

        var vRemainingForA = await vConnection.ExecuteScalarAsync<long>(
            """SELECT COUNT(*) FROM "Run" WHERE "UserId" = @UserId""", new { UserId = UserA });

        var vRemainingForB = await vConnection.ExecuteScalarAsync<long>(
            """SELECT COUNT(*) FROM "Run" WHERE "UserId" = @UserId""", new { UserId = UserB });

        Assert.Equal(0, vRemainingForA);
        Assert.Equal(7, vRemainingForB);
    }

    // ---------------------------------------------------------------- path-level

    /// <summary>The raw archive and the reports folder are separated by path, not by a filter.</summary>
    /// <remarks>
    /// Written into a real temporary directory rather than asserted on strings alone: the property
    /// that matters is that one user's enumeration cannot see the other's file.
    /// </remarks>
    [Fact]
    public async Task RawArchiveAndReportsPathsAreSeparatePerUser()
    {
        var vRoot = Path.Combine(Path.GetTempPath(), "tflens-isolation-" + Guid.NewGuid().ToString("N"));
        var vOptions = new TfLensOptions { DataRoot = vRoot };

        try
        {
            var vRawA = Path.Combine(vOptions.RawPath(UserA), "techierathore__TfLens");
            var vRawB = Path.Combine(vOptions.RawPath(UserB), "techierathore__TfLens");

            Directory.CreateDirectory(vRawA);
            Directory.CreateDirectory(vRawB);
            Directory.CreateDirectory(vOptions.ReportsPath(UserA));
            Directory.CreateDirectory(vOptions.ReportsPath(UserB));

            await File.WriteAllTextAsync(Path.Combine(vRawA, "runs-aaaaaaa.jsonl"), "{\"user\":\"a\"}");
            await File.WriteAllTextAsync(Path.Combine(vRawB, "runs-bbbbbbb.jsonl"), "{\"user\":\"b\"}");

            var vSeenByA = Directory
                .EnumerateFiles(vOptions.RawPath(UserA), "*.jsonl", SearchOption.AllDirectories)
                .Select(aPath => Path.GetFileName(aPath))
                .ToList();

            Assert.Equal(new[] { "runs-aaaaaaa.jsonl" }, vSeenByA);

            Assert.NotEqual(vOptions.RawPath(UserA), vOptions.RawPath(UserB));
            Assert.NotEqual(vOptions.ReportsPath(UserA), vOptions.ReportsPath(UserB));
            Assert.False(vOptions.RawPath(UserA).StartsWith(vOptions.RawPath(UserB), StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(vRoot))
            {
                Directory.Delete(vRoot, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------- store-level

    /// <summary>
    /// The same isolation, proven through the store the application actually resolves.
    /// </summary>
    /// <remarks>
    /// The SQL-level tests above prove the schema supports isolation. This one proves the shipped
    /// implementation uses it. It is tagged <c>Blocked</c> until the store is registered in the
    /// container — until then it fails with the reason, rather than passing on an absence.
    /// </remarks>
    [Fact]
    [Trait("Category", "Blocked")]
    public async Task ScopedReadsThroughTheRegisteredStoreNeverCrossTheUserBoundary()
    {
        RequireDatabase();

        await using var vHost = new TfLensTestHost();
        var vServices = vHost.TryGetServices(out var vWhyNot);

        Assert.True(
            vServices is not null,
            $"REQ-NFR-010 — the application host could not be built, so isolation cannot be proven " +
            $"through the real services yet. Reason: {vWhyNot}");

        await using var vScope = vServices!.CreateAsyncScope();
        var vStore = vScope.ServiceProvider.GetService<ITelemetryStore>();

        Assert.True(
            vStore is not null,
            "REQ-NFR-010 — ITelemetryStore is not registered in the container (AddTfLensStorage is " +
            "still a no-op), so the store-level half of the isolation proof cannot run. Blocked on the " +
            "storage cluster.");

        var vRunsForA = await vStore!.ReadRunsAsync(UserA, FrameworkNames.TechieFlow, SharedRepo);
        var vRunsForB = await vStore.ReadRunsAsync(UserB, FrameworkNames.TechieFlow, SharedRepo);

        Assert.Equal(3, vRunsForA.Count);
        Assert.Equal(7, vRunsForB.Count);

        var vReposForA = await vStore.ReadUserReposAsync(UserA);
        Assert.All(vReposForA, aRepo => Assert.Equal(UserA, aRepo.UserId));

        var vSyncForB = await vStore.ReadSyncStateAsync(UserB);
        Assert.All(vSyncForB, aState => Assert.Equal(UserB, aState.UserId));
    }

    /// <summary>
    /// The same isolation, proven over HTTP with two signed-in users.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The end-to-end shape REQ-NFR-010 describes, and the only form of the proof that covers the whole
    /// stack: cookie, claim, service, SQL predicate and rendered markup. The store-level test above can
    /// only show that a correct call is scoped; this shows that the call the <b>page</b> makes is scoped,
    /// which is where a missed <c>UserId</c> would actually reach a user.
    /// </para>
    /// <para>
    /// It signs in as the two real AppManager accounts the UsageGuide documents, because the cookie is
    /// issued from a genuine AppManager response and a synthetic user could not produce one. Each is
    /// given a repository whose name identifies its owner, so a leak surfaces as the *other* user's
    /// repository name appearing in the markup — a wrong value, not merely a wrong count.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SignedInUsersNeverSeeEachOthersReposOverHttp()
    {
        RequireDatabase();

        await using var vHost = new TfLensTestHost();
        var vServices = vHost.TryGetServices(out var vWhyNot);

        Assert.True(
            vServices is not null,
            $"REQ-NFR-010 — the application host could not be built. Reason: {vWhyNot}");

        using var vAnonymous = vHost.CreateClient();

        var vHealth = await vAnonymous.GetAsync("/healthz");
        Assert.True(vHealth.IsSuccessStatusCode, $"/healthz answered {(int)vHealth.StatusCode}.");

        var vRepos = await vAnonymous.GetAsync("/repos");

        Assert.True(
            vRepos.StatusCode is System.Net.HttpStatusCode.Redirect
                or System.Net.HttpStatusCode.Found
                or System.Net.HttpStatusCode.Unauthorized,
            $"REQ-FN-005 — an unauthenticated GET /repos returned {(int)vRepos.StatusCode}; it must " +
            "redirect to /login.");

        await SeedProbeRepoAsync(DemoUserId, DemoProbeRepo);
        await SeedProbeRepoAsync(SecondUserId, SecondProbeRepo);

        try
        {
            using var vDemoClient = vHost.CreateClient();
            using var vSecondClient = vHost.CreateClient();

            await SignInAsync(vDemoClient, DemoEmail, DemoPassword);
            await SignInAsync(vSecondClient, SecondEmail, SecondPassword);

            var vDemoMarkup = await ReadReposMarkupAsync(vDemoClient, DemoEmail);
            var vSecondMarkup = await ReadReposMarkupAsync(vSecondClient, SecondEmail);

            Assert.Contains(DemoProbeRepo, vDemoMarkup, StringComparison.Ordinal);
            Assert.Contains(SecondProbeRepo, vSecondMarkup, StringComparison.Ordinal);

            // The two assertions that carry the requirement. Each user's page must not name the other's
            // repository anywhere in the rendered markup.
            Assert.DoesNotContain(SecondProbeRepo, vDemoMarkup, StringComparison.Ordinal);
            Assert.DoesNotContain(DemoProbeRepo, vSecondMarkup, StringComparison.Ordinal);
        }
        finally
        {
            await PurgeProbeReposAsync();
        }
    }

    /// <summary>
    /// Signs a client in through the real <c>/login</c> form post, cookie and all.
    /// </summary>
    /// <remarks>
    /// The form carries an antiforgery token, so the token has to be read off the rendered page first —
    /// posting without it is exactly what REQ-NFR-002 makes the endpoint reject. The AppManager call is
    /// real: this is the documented test account, and a stubbed identity would prove nothing about the
    /// claim the cookie ends up carrying.
    /// </remarks>
    /// <param name="aClient">The client to authenticate; it keeps the cookie.</param>
    /// <param name="aEmail">The account's email.</param>
    /// <param name="aPassword">The account's password.</param>
    /// <returns>A task that completes once the client holds an auth cookie.</returns>
    private static async Task SignInAsync(HttpClient aClient, string aEmail, string aPassword)
    {
        var vLoginPage = await aClient.GetStringAsync("/login");
        var vToken = ReadAntiforgeryToken(vLoginPage);

        Assert.False(
            string.IsNullOrEmpty(vToken),
            "REQ-NFR-002 — /login rendered no antiforgery token, so the form post cannot be trusted.");

        var vResponse = await aClient.PostAsync(
            "/auth/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = vToken!,
                ["Email"] = aEmail,
                ["Password"] = aPassword,
                ["ReturnUrl"] = "/repos"
            }));

        var vLocation = vResponse.Headers.Location?.ToString() ?? string.Empty;

        Assert.True(
            vResponse.StatusCode is System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.Found
            && !vLocation.StartsWith("/login", StringComparison.Ordinal),
            $"Sign-in for {aEmail} did not succeed: {(int)vResponse.StatusCode} -> '{vLocation}'. " +
            "The account must exist in AppManager with the password recorded in the UsageGuide.");
    }

    /// <summary>Fetches the repos page as a signed-in client and returns its markup.</summary>
    /// <param name="aClient">An authenticated client.</param>
    /// <param name="aEmail">Whose client it is, for the failure message.</param>
    /// <returns>The rendered markup.</returns>
    private static async Task<string> ReadReposMarkupAsync(HttpClient aClient, string aEmail)
    {
        var vResponse = await aClient.GetAsync("/repos");

        Assert.True(
            vResponse.IsSuccessStatusCode,
            $"/repos answered {(int)vResponse.StatusCode} for the signed-in {aEmail}; it must render.");

        return await vResponse.Content.ReadAsStringAsync();
    }

    /// <summary>Reads the antiforgery token out of a rendered form.</summary>
    /// <param name="aHtml">The page markup.</param>
    /// <returns>The token, or <c>null</c> when the page carries none.</returns>
    private static string? ReadAntiforgeryToken(string aHtml)
    {
        var vMatch = System.Text.RegularExpressions.Regex.Match(
            aHtml,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""");

        return vMatch.Success ? vMatch.Groups[1].Value : null;
    }

    /// <summary>Gives one real account a repository whose name identifies its owner.</summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aRepo">The <c>owner/name</c> to insert.</param>
    /// <returns>A task that completes when the row exists.</returns>
    private async Task SeedProbeRepoAsync(int aUserId, string aRepo)
    {
        await using var vConnection = await objDb.OpenAsync();

        await vConnection.ExecuteAsync(
            """
            INSERT INTO "UserRepo" ("UserId","Repo","Owner","Name","Branch","Kind","Framework","IsPublic","ConnectedTs")
            VALUES (@UserId, @Repo, @Owner, @Name, 'main', 'techieflow', 'techieflow', true, @Ts)
            ON CONFLICT ("UserId","Repo") DO NOTHING
            """,
            new
            {
                UserId = aUserId,
                Repo = aRepo,
                Owner = aRepo.Split('/')[0],
                Name = aRepo.Split('/')[1],
                Ts = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    /// <summary>
    /// Removes only the probe repositories, leaving each account's real workspace untouched.
    /// </summary>
    /// <returns>A task that completes when the probe rows are gone.</returns>
    private async Task PurgeProbeReposAsync()
    {
        await using var vConnection = await objDb.OpenAsync();

        await vConnection.ExecuteAsync(
            """DELETE FROM "UserRepo" WHERE "Repo" IN (@DemoRepo, @SecondRepo)""",
            new { DemoRepo = DemoProbeRepo, SecondRepo = SecondProbeRepo });
    }

    // ---------------------------------------------------------------- fixture plumbing

    /// <summary>Fails with a clear reason when PostgreSQL is not reachable.</summary>
    private void RequireDatabase() =>
        Assert.True(
            objDb.IsAvailable,
            $"REQ-NFR-010 — PostgreSQL is not reachable, so isolation cannot be proven. " +
            $"Set {PostgresFixture.ConnectionVariable} or start `docker compose up -d postgres`. " +
            $"Reason: {objDb.UnavailableReason}");

    /// <summary>Removes every row belonging to the two synthetic users.</summary>
    /// <returns>A task that completes when the rows are gone.</returns>
    private async Task PurgeAsync()
    {
        await using var vConnection = await objDb.OpenAsync();

        foreach (var vTable in new[]
                 { "Run", "Gate", "Session", "Commit", "PbEvent", "SyncState", "UserRepo", "AuthSession" })
        {
            await vConnection.ExecuteAsync(
                $"""DELETE FROM "{vTable}" WHERE "UserId" IN (@A, @B)""",
                new { A = UserA, B = UserB });
        }
    }

    /// <summary>Seeds one user's whole world: repo, sync state, session and every stream.</summary>
    /// <param name="aUserId">The synthetic user.</param>
    /// <param name="aMarker">A per-user marker written into every row, so a leak shows as a wrong value.</param>
    /// <param name="aRuns">How many run rows.</param>
    /// <param name="aGates">How many gate rows.</param>
    /// <param name="aSessions">How many session rows.</param>
    /// <param name="aCommits">How many commit rows.</param>
    /// <param name="aEvents">How many Playbook event rows.</param>
    /// <returns>A task that completes when the rows exist.</returns>
    private async Task SeedAsync(
        int aUserId,
        string aMarker,
        int aRuns,
        int aGates,
        int aSessions,
        int aCommits,
        int aEvents)
    {
        await using var vConnection = await objDb.OpenAsync();

        await vConnection.ExecuteAsync(
            """
            INSERT INTO "UserRepo" ("UserId","Repo","Owner","Name","Branch","Kind","Framework","IsPublic","ConnectedTs")
            VALUES (@UserId, @Repo, 'techierathore', 'TfLens', @Branch, 'techieflow', 'techieflow', true, @Ts)
            """,
            new
            {
                UserId = aUserId,
                Repo = SharedRepo,
                Branch = $"branch-{aMarker}",
                Ts = DateTimeOffset.UtcNow.ToString("O")
            });

        await vConnection.ExecuteAsync(
            """
            INSERT INTO "SyncState" ("UserId","Repo","Kind","Branch","LastSha","LastSyncTs","LastError")
            VALUES (@UserId, @Repo, 'techieflow', @Branch, @Sha, @Ts, @Error)
            """,
            new
            {
                UserId = aUserId,
                Repo = SharedRepo,
                Branch = $"branch-{aMarker}",
                Sha = aMarker,
                Ts = DateTimeOffset.UtcNow.ToString("O"),
                Error = aUserId == UserB ? $"403 rate limited ({aMarker})" : null
            });

        await vConnection.ExecuteAsync(
            """
            INSERT INTO "AuthSession"
              ("SessionId","UserId","Email","DisplayName","AccessToken","RefreshToken","TokenExpiresAt","CreatedTs")
            VALUES (@SessionId, @UserId, @Email, @Name, @Access, @Refresh, @Expires, @Created)
            """,
            new
            {
                SessionId = $"session-{aMarker}",
                UserId = aUserId,
                Email = $"{aMarker}@example.invalid",
                Name = aMarker,
                Access = $"protected-access-{aMarker}",
                Refresh = $"protected-refresh-{aMarker}",
                Expires = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                Created = DateTimeOffset.UtcNow.ToString("O")
            });

        for (var vIndex = 0; vIndex < aRuns; vIndex++)
        {
            await vConnection.ExecuteAsync(
                """
                INSERT INTO "Run" ("UserId","Repo","SourceSha","Ts","App","Cmd")
                VALUES (@UserId, @Repo, @Sha, @Ts, @App, @Cmd)
                """,
                new
                {
                    UserId = aUserId,
                    Repo = SharedRepo,
                    Sha = aMarker,
                    Ts = Stamp(vIndex),
                    App = aMarker,
                    Cmd = $"cmd-{vIndex}"
                });
        }

        for (var vIndex = 0; vIndex < aGates; vIndex++)
        {
            await vConnection.ExecuteAsync(
                """
                INSERT INTO "Gate" ("UserId","Repo","SourceSha","Ts","App","ReqId","RunId","Verdict")
                VALUES (@UserId, @Repo, @Sha, @Ts, @App, @ReqId, @RunId, 'pass')
                """,
                new
                {
                    UserId = aUserId,
                    Repo = SharedRepo,
                    Sha = aMarker,
                    Ts = Stamp(vIndex),
                    App = aMarker,
                    ReqId = $"REQ-{vIndex:D3}",
                    RunId = $"run-{vIndex}"
                });
        }

        for (var vIndex = 0; vIndex < aSessions; vIndex++)
        {
            await vConnection.ExecuteAsync(
                """
                INSERT INTO "Session" ("UserId","Repo","SourceSha","Ts","SessionId","OutputTokens")
                VALUES (@UserId, @Repo, @Sha, @Ts, @SessionId, @Tokens)
                """,
                new
                {
                    UserId = aUserId,
                    Repo = SharedRepo,
                    Sha = aMarker,
                    Ts = Stamp(vIndex),
                    SessionId = $"{aMarker}-{vIndex}",
                    Tokens = 100 + vIndex
                });
        }

        for (var vIndex = 0; vIndex < aCommits; vIndex++)
        {
            await vConnection.ExecuteAsync(
                """
                INSERT INTO "Commit" ("UserId","Repo","SourceSha","Ts","Sha")
                VALUES (@UserId, @Repo, @Source, @Ts, @Sha)
                """,
                new
                {
                    UserId = aUserId,
                    Repo = SharedRepo,
                    Source = aMarker,
                    Ts = Stamp(vIndex),
                    Sha = $"{aMarker}{vIndex:D2}"
                });
        }

        for (var vIndex = 0; vIndex < aEvents; vIndex++)
        {
            await vConnection.ExecuteAsync(
                """
                INSERT INTO "PbEvent" ("UserId","Repo","SourceSha","Ts","Kind","SessionId")
                VALUES (@UserId, @Repo, @Sha, @Ts, 'turn', @SessionId)
                """,
                new
                {
                    UserId = aUserId,
                    Repo = SharedRepo,
                    Sha = aMarker,
                    Ts = Stamp(vIndex),
                    SessionId = $"{aMarker}-{vIndex}"
                });
        }
    }

    /// <summary>A distinct, ordered timestamp per seeded row.</summary>
    /// <param name="aIndex">The row's index.</param>
    /// <returns>An ISO-8601 UTC stamp.</returns>
    private static string Stamp(int aIndex) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(aIndex).ToString("O");

    /// <summary>One row of <c>pg_indexes</c>, for the unique-index audit.</summary>
    private sealed class IndexRow
    {
        /// <summary>The index's name.</summary>
        public string IndexName { get; init; } = string.Empty;

        /// <summary>The <c>CREATE UNIQUE INDEX</c> statement PostgreSQL reports for it.</summary>
        public string Definition { get; init; } = string.Empty;
    }
}
