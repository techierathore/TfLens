using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Playbook;
using TfLens.Core.Storage;

namespace TfLens.Integration.Tests;

/// <summary>
/// REQ-FN-103 / REQ-FN-104 (BRD-164, BRD-165, ADR-016, ADR-024) — the sibling of
/// <see cref="CrossUserIsolationTests"/>: where that one connects two <b>users</b> and proves neither
/// can see the other's rows, this one connects two <b>frameworks</b> and proves the same.
/// </summary>
/// <remarks>
/// <para>
/// ADR-024 puts the Playbook's misses in the same three tables as TechieFlow's, which makes
/// <c>UserRepo.Framework</c> the only wall between the two editions. The architecture names the residual
/// risk that follows in one sentence: <i>a query that forgets the framework filter</i>. That failure is
/// silent — it returns more rows rather than none, and the extra rows look exactly like the wanted ones —
/// so it can only be caught by seeding both editions with distinguishable values and reading them back
/// through the real SQL. A fake store would prove only that the fake was written correctly.
/// </para>
/// <para>
/// The second half of the class is the partial index Cluster A added. <c>UcMissUserRepoSourceLine</c> is
/// declared <c>WHERE "SourceLineHash" IS NOT NULL</c> precisely so TechieFlow rows, which carry no hash,
/// cannot collide with <b>each other</b> on <c>NULL</c>. That is easy to state, easy to lose to a later
/// "tidy the WHERE clause off", and impossible to notice afterwards — a whole edition's misses would
/// simply stop arriving after the first one.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CrossFrameworkIsolationTests : IAsyncLifetime
{
    /// <summary>One synthetic user, far outside the AppManager range in use.</summary>
    private const int UserId = 992001;

    /// <summary>The user's TechieFlow source.</summary>
    private const string FlowRepo = "framework-probe/techieflow";

    /// <summary>The user's Playbook source.</summary>
    private const string BookRepo = "framework-probe/playbook";

    private readonly PostgresFixture objDb;

    /// <summary>Creates the test class.</summary>
    /// <param name="aDb">The shared live-PostgreSQL fixture.</param>
    public CrossFrameworkIsolationTests(PostgresFixture aDb)
    {
        objDb = aDb;
    }

    /// <summary>Seeds one user with a source on each framework, from a clean slate.</summary>
    /// <returns>A task that completes when the fixture rows exist.</returns>
    public async Task InitializeAsync()
    {
        if (!objDb.IsAvailable)
        {
            return;
        }

        await PurgeAsync();
        await using var vConnection = await objDb.OpenAsync();

        await ConnectAsync(vConnection, FlowRepo, FrameworkNames.TechieFlow);
        await ConnectAsync(vConnection, BookRepo, FrameworkNames.Playbook);

        // Two TechieFlow rows, both with a NULL SourceLineHash. If the partial index ever loses its
        // WHERE clause the second insert is silently swallowed and this count drops to one.
        await InsertMissAsync(vConnection, FlowRepo, "MISS-flow-1", null, null, "build");
        await InsertMissAsync(vConnection, FlowRepo, "MISS-flow-2", null, null, "acceptance");

        // Two Playbook rows, keyed on their own source lines and carrying the Playbook axes instead.
        await InsertMissAsync(vConnection, BookRepo, "PB-1", "ITEM-1", "hash-pb-1", null, "plan-review");
        await InsertMissAsync(vConnection, BookRepo, "PB-2", "ITEM-2", "hash-pb-2", null, "verify");
    }

    /// <summary>Removes the probe rows so a re-run starts clean.</summary>
    /// <returns>A task that completes when the rows are gone.</returns>
    public async Task DisposeAsync()
    {
        if (objDb.IsAvailable)
        {
            await PurgeAsync();
        }
    }

    /// <summary>
    /// A framework-scoped read returns that edition's misses and none of the other's.
    /// </summary>
    /// <remarks>
    /// Both editions belong to the <b>same user</b>, which is the hard case: the <c>UserId</c> predicate
    /// every other test relies on is satisfied by both sets of rows, so only the framework join can tell
    /// them apart.
    /// </remarks>
    [Theory]
    [InlineData(FrameworkNames.TechieFlow, "MISS-flow-1", "MISS-flow-2")]
    [InlineData(FrameworkNames.Playbook, "PB-1", "PB-2")]
    public async Task AFrameworkScopedReadNeverPoolsTheOtherEdition(string aFramework, params string[] aExpected)
    {
        RequireDatabase();

        var vMisses = await Store().ReadMissesAsync(UserId, aFramework);

        vMisses.Select(aMiss => aMiss.MissId).OrderBy(aId => aId, StringComparer.Ordinal)
            .Should().Equal(aExpected);
    }

    /// <summary>
    /// The Playbook read path applies the Playbook's own guards and sees the Playbook's rows only.
    /// </summary>
    [Fact]
    public async Task PlaybookReadSeesOnlyPlaybookRowsAndKeepsTheAxesApart()
    {
        RequireDatabase();

        var vReport = await PlaybookMissNormalizer.ReadAsync(Store(), UserId, FrameworkNames.Playbook);

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vReport.Misses.Select(aMiss => aMiss.MissId).Should().BeEquivalentTo(["PB-1", "PB-2"]);
        vReport.ByItemId.Select(aRow => aRow.Key).Should().Equal(["ITEM-1", "ITEM-2"]);
        vReport.ByFoundPhaseGate.Select(aRow => aRow.Key).Should().Equal(["plan-review", "verify"]);
        vReport.ByFoundPhaseGate.Should().NotContain(
            aRow => aRow.Key == "build" || aRow.Key == "acceptance",
            "a TechieFlow assertion gate never reaches the Playbook process-gate chart (BRD-165)");
    }

    /// <summary>
    /// Two TechieFlow rows with no source-line hash coexist beside two hashed Playbook rows.
    /// </summary>
    /// <remarks>
    /// The whole point of the partial index. Four rows in, four rows out: the TechieFlow pair does not
    /// collide with itself on <c>NULL</c>, and the Playbook pair is still governed by its own key.
    /// </remarks>
    [Fact]
    public async Task NullSourceLineHashRowsDoNotCollideWithEachOther()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        var vNullHashed = await vConnection.ExecuteScalarAsync<long>(
            """SELECT COUNT(*) FROM "Miss" WHERE "UserId" = @UserId AND "SourceLineHash" IS NULL""",
            new { UserId });

        var vHashed = await vConnection.ExecuteScalarAsync<long>(
            """SELECT COUNT(*) FROM "Miss" WHERE "UserId" = @UserId AND "SourceLineHash" IS NOT NULL""",
            new { UserId });

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vNullHashed.Should().Be(2, "TechieFlow rows carry no hash and must not collide on NULL");
        vHashed.Should().Be(2);
    }

    /// <summary>
    /// Re-inserting an already-stored Playbook source line is a no-op rather than a duplicate.
    /// </summary>
    [Fact]
    public async Task ReimportingASourceLineDoesNotDuplicateTheRow()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();
        await InsertMissAsync(vConnection, BookRepo, "PB-1-again", "ITEM-1", "hash-pb-1", null, "plan-review");

        var vRows = await vConnection.ExecuteScalarAsync<long>(
            """SELECT COUNT(*) FROM "Miss" WHERE "UserId" = @UserId AND "SourceLineHash" = 'hash-pb-1'""",
            new { UserId });

        vRows.Should().Be(1, "the source line is the Playbook edition's natural key (BRD-164)");
    }

    /// <summary>
    /// The partial unique index is still declared, and still partial.
    /// </summary>
    /// <remarks>
    /// Asserted against <c>pg_indexes</c> rather than against the DDL file, because what protects the
    /// data is the index the database actually has.
    /// </remarks>
    [Fact]
    public async Task SourceLineIndexIsStillPartial()
    {
        RequireDatabase();

        await using var vConnection = await objDb.OpenAsync();

        var vDefinition = await vConnection.ExecuteScalarAsync<string?>(
            """SELECT indexdef FROM pg_indexes WHERE indexname = 'UcMissUserRepoSourceLine'""");

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vDefinition.Should().NotBeNull("ADR-024's Playbook natural key rests on it");
        vDefinition!.Should().Contain("UNIQUE").And.Contain("IS NOT NULL");
    }

    /// <summary>Fails with an actionable message when the database is not reachable.</summary>
    private void RequireDatabase() =>
        Assert.True(
            objDb.IsAvailable,
            "ADR-016 — PostgreSQL is not reachable, so cross-framework isolation cannot be proven. "
            + $"Set {PostgresFixture.ConnectionVariable} or point user secrets at a server. "
            + $"Reason: {objDb.UnavailableReason}");

    /// <summary>Builds a store against the live database.</summary>
    /// <returns>The store.</returns>
    private PostgresStore Store() =>
        new(Options.Create(new TfLensOptions { DbConnection = objDb.ConnectionString }),
            new StreamParser(),
            NullLogger<PostgresStore>.Instance);

    /// <summary>Removes every row belonging to the synthetic user.</summary>
    /// <returns>A task that completes when the rows are gone.</returns>
    private async Task PurgeAsync()
    {
        await using var vConnection = await objDb.OpenAsync();

        foreach (var vTable in new[] { "Miss", "MissFix", "MissAmend", "UserRepo" })
        {
            await vConnection.ExecuteAsync(
                $"""DELETE FROM "{vTable}" WHERE "UserId" = @UserId""",
                new { UserId });
        }
    }

    /// <summary>Connects one repository on one framework.</summary>
    /// <param name="aConnection">An open connection.</param>
    /// <param name="aRepo">The <c>owner/name</c> to connect.</param>
    /// <param name="aFramework">The provenance axis the repository sits on.</param>
    /// <returns>A task that completes when the row exists.</returns>
    private static Task ConnectAsync(Npgsql.NpgsqlConnection aConnection, string aRepo, string aFramework) =>
        aConnection.ExecuteAsync(
            """
            INSERT INTO "UserRepo" ("UserId","Repo","Owner","Name","Branch","Kind","Framework","IsPublic","ConnectedTs")
            VALUES (@UserId, @Repo, 'framework-probe', @Name, 'main', @Framework, @Framework, true, @Ts)
            """,
            new
            {
                UserId,
                Repo = aRepo,
                Name = aRepo.Split('/')[1],
                Framework = aFramework,
                Ts = DateTimeOffset.UtcNow.ToString("O")
            });

    /// <summary>Inserts one miss row through the same idempotent statement the store uses.</summary>
    /// <param name="aConnection">An open connection.</param>
    /// <param name="aRepo">The repository the row belongs to.</param>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aItemId">The Playbook requirement axis, or <c>null</c> on a TechieFlow row.</param>
    /// <param name="aSourceLineHash">The Playbook natural key, or <c>null</c> on a TechieFlow row.</param>
    /// <param name="aFoundGate">The TechieFlow assertion gate, or <c>null</c>.</param>
    /// <param name="aFoundPhaseGate">The Playbook process gate, or <c>null</c>.</param>
    /// <returns>A task that completes when the row exists or was refused as a duplicate.</returns>
    private static Task InsertMissAsync(
        Npgsql.NpgsqlConnection aConnection,
        string aRepo,
        string aMissId,
        string? aItemId = null,
        string? aSourceLineHash = null,
        string? aFoundGate = null,
        string? aFoundPhaseGate = null) =>
        aConnection.ExecuteAsync(
            """
            INSERT INTO "Miss" ("UserId","Repo","SourceSha","V","Ts","MissId","ItemId","FoundGate",
                                "FoundPhaseGate","SourceLineHash")
            VALUES (@UserId, @Repo, 'probe', 1, @Ts, @MissId, @ItemId, @FoundGate,
                    @FoundPhaseGate, @SourceLineHash)
            ON CONFLICT DO NOTHING
            """,
            new
            {
                UserId,
                Repo = aRepo,
                Ts = "2026-08-30T09:00:00Z",
                MissId = aMissId,
                ItemId = aItemId,
                FoundGate = aFoundGate,
                FoundPhaseGate = aFoundPhaseGate,
                SourceLineHash = aSourceLineHash
            });
}
