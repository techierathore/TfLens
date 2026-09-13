using System.Text.RegularExpressions;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TfLens.Core;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Storage;

namespace TfLens.Core.Tests.Storage;

/// <summary>
/// REQ-FN-103 (BRD-164, ADR-024) — every Playbook miss row is keyed on its own source line, never on
/// <c>miss_id</c>, in all three existing miss tables; TechieFlow rows keep their natural keys.
/// </summary>
/// <remarks>
/// <para>
/// The defect this pins: the TechieFlow keys (<c>UcMissUserRepoMissId</c> and its two siblings) were
/// declared over every row, so a later Playbook export carrying a new line that repeats an existing
/// <c>miss_id</c> with different content collided with the stored line and vanished under
/// <c>ON CONFLICT DO NOTHING</c> — six distinct lines presented, one of the two new ones stored. The
/// Playbook re-emits its whole file on every run, so a corrected line and its predecessor are two facts.
/// </para>
/// <para>
/// Run against the real PostgreSQL because the property is the database's: partial unique indexes and
/// an unqualified conflict clause. Reserved user id <see cref="UserId"/> is far above any AppManager id
/// and every row it writes is purged in <see cref="DisposeAsync"/>.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PlaybookMissSourceLineKeyTests : IAsyncLifetime
{
    /// <summary>A reserved id no other test class uses (the digits name the requirement).</summary>
    private const int UserId = 90103;

    /// <summary>The Playbook source the rows are stored under.</summary>
    private const string BookRepo = "tflenstest/PbMissSourceLine";

    /// <summary>A TechieFlow source, to prove its natural key still collapses a re-parse.</summary>
    private const string FlowRepo = "tflenstest/TfMissNaturalKey";

    /// <summary>The dataset identity the rows pretend to come from.</summary>
    private const string Sha = "b2f4883f04";

    /// <summary>The source-window block every counted Playbook line carries.</summary>
    private const string Window =
        "\"source_window\":{\"complete\":true,\"valid\":true},\"data_quality\":{\"valid\":true,\"cost_status\":\"complete\"}";

    /// <summary>The first export: two misses, one fix, one amendment.</summary>
    private static readonly string[] FirstExport =
    [
        "{\"kind\":\"miss\",\"ts\":\"2026-08-30T09:00:00Z\",\"miss_id\":\"PB-1\",\"why_missed\":\"missing-checklist-item\",\"miss_class\":\"wrong-behaviour\",\"found_phase_gate\":\"verify\",\"item_id\":\"ITEM-1\"," + Window + "}",
        "{\"kind\":\"miss\",\"ts\":\"2026-08-30T09:05:00Z\",\"miss_id\":\"PB-2\",\"miss_class\":\"missing-feature\",\"found_phase_gate\":\"plan-review\",\"item_id\":\"ITEM-2\"," + Window + "}",
        "{\"kind\":\"miss-fix\",\"ts\":\"2026-08-30T10:00:00Z\",\"miss_id\":\"PB-1\",\"fix_run_id\":\"run-1\",\"cost_attribution\":\"sole\",\"tokens_out\":100," + Window + "}",
        "{\"kind\":\"miss-amend\",\"ts\":\"2026-08-30T11:00:00Z\",\"miss_id\":\"PB-2\",\"field\":\"why_missed\",\"value\":\"ambiguous-acceptance\"}"
    ];

    /// <summary>
    /// The later export: the first one verbatim, plus one new line of each kind that repeats an
    /// existing natural key with different content — each a new record on the source-line key.
    /// </summary>
    private static readonly string[] LaterExport =
    [
        .. FirstExport,
        "{\"kind\":\"miss\",\"ts\":\"2026-08-30T12:00:00Z\",\"miss_id\":\"PB-1\",\"why_missed\":\"missing-checklist-item\",\"miss_class\":\"wrong-behaviour\",\"found_phase_gate\":\"gap-report\",\"item_id\":\"ITEM-1\"," + Window + "}",
        "{\"kind\":\"miss-fix\",\"ts\":\"2026-08-30T12:30:00Z\",\"miss_id\":\"PB-1\",\"fix_run_id\":\"run-1\",\"cost_attribution\":\"sole\",\"tokens_out\":140," + Window + "}",
        "{\"kind\":\"miss-amend\",\"ts\":\"2026-08-30T11:00:00Z\",\"miss_id\":\"PB-2\",\"field\":\"why_missed\",\"value\":\"other\"}"
    ];

    private readonly StreamParser objParser = new();
    private PostgresStore objStore = null!;

    /// <summary>Applies the schema and clears both sources before the first test.</summary>
    /// <returns>A task that completes when the store is ready.</returns>
    public async Task InitializeAsync()
    {
        if (TestDatabase.ConnectionStringOrNull() is null)
        {
            return;
        }

        objStore = new PostgresStore(
            Options.Create(new TfLensOptions { DbConnection = TestDatabase.ConnectionStringOrNull()! }),
            objParser,
            NullLogger<PostgresStore>.Instance);
        await objStore.EnsureSchemaAsync();
        await PurgeAsync();
    }

    /// <summary>Leaves the shared database as it was found.</summary>
    /// <returns>A task that completes when the rows are gone.</returns>
    public async Task DisposeAsync()
    {
        if (TestDatabase.ConnectionStringOrNull() is not null)
        {
            await PurgeAsync();
        }
    }

    /// <summary>
    /// A later export adds exactly its new lines — including the three that repeat an existing
    /// <c>miss_id</c> (and fix run, and amended field) — and collapses exactly the unchanged ones.
    /// </summary>
    [Fact]
    public async Task LaterExportAddsEveryNewSourceLineEvenWhenItRepeatsAMissId()
    {
        RequireDatabase();

        var vFirst = await StoreAsync(BookRepo, StreamKind.PlaybookMisses, FirstExport);
        var vAgain = await StoreAsync(BookRepo, StreamKind.PlaybookMisses, FirstExport);
        var vLater = await StoreAsync(BookRepo, StreamKind.PlaybookMisses, LaterExport);

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vFirst.Should().Be(FirstExport.Length, "every line of the first export is a new record");
        vAgain.Should().Be(0, "re-importing the same export duplicates no lifecycle record");
        vLater.Should().Be(
            LaterExport.Length - FirstExport.Length,
            "each new source line is keyed on its own identity, not on the miss_id it repeats");
        (await CountAsync("Miss", BookRepo)).Should().Be(3);
        (await CountAsync("MissFix", BookRepo)).Should().Be(2);
        (await CountAsync("MissAmend", BookRepo)).Should().Be(2);
    }

    /// <summary>
    /// The TechieFlow edition is untouched: a re-parse of the same <c>misses.jsonl</c> still collapses
    /// on <c>miss_id</c>, and a second line carrying an existing <c>miss_id</c> is still the same miss.
    /// </summary>
    [Fact]
    public async Task TechieFlowMissesStillCollapseOnTheirNaturalKey()
    {
        RequireDatabase();

        string[] vStream =
        [
            "{\"v\":1,\"kind\":\"miss\",\"ts\":\"2026-08-30T09:00:00Z\",\"app\":\"TfProbe\",\"miss_id\":\"MISS-TF-1\",\"miss_class\":\"wrong-behaviour\",\"found_by\":\"verifier\"}",
            "{\"v\":1,\"kind\":\"miss\",\"ts\":\"2026-08-30T10:00:00Z\",\"app\":\"TfProbe\",\"miss_id\":\"MISS-TF-1\",\"miss_class\":\"missing-feature\",\"found_by\":\"verifier\"}"
        ];

        await StoreAsync(FlowRepo, StreamKind.Misses, vStream);
        var vAgain = await StoreAsync(FlowRepo, StreamKind.Misses, vStream);

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        (await CountAsync("Miss", FlowRepo)).Should().Be(1, "a TechieFlow miss is opened once (BRD-114)");
        vAgain.Should().Be(0, "a re-parse of the same archived file is a no-op");
    }

    /// <summary>
    /// The database itself holds the two keys per table, each partial and on opposite predicates, so
    /// no row can be governed by both and no Playbook row by <c>miss_id</c>.
    /// </summary>
    [Fact]
    public async Task EveryMissTableCarriesTwoOppositePartialKeys()
    {
        RequireDatabase();

        await using var vConnection = new NpgsqlConnection(TestDatabase.ConnectionStringOrNull());
        await vConnection.OpenAsync();

        var vDefinitions = (await vConnection.QueryAsync<(string Name, string Definition)>(
                """
                SELECT indexname, indexdef FROM pg_indexes
                WHERE indexname IN ('UcMissUserRepoMissId','UcMissFixUserRepoMissIdFixRunId',
                                    'UcMissAmendUserRepoMissIdFieldTs','UcMissUserRepoSourceLine',
                                    'UcMissFixUserRepoSourceLine','UcMissAmendUserRepoSourceLine')
                """))
            .ToDictionary(aRow => aRow.Name, aRow => aRow.Definition, StringComparer.Ordinal);

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vDefinitions.Should().HaveCount(6, "each of the three miss tables carries both editions' keys");
        foreach (var vName in new[] { "UcMissUserRepoMissId", "UcMissFixUserRepoMissIdFixRunId", "UcMissAmendUserRepoMissIdFieldTs" })
        {
            vDefinitions.GetValueOrDefault(vName).Should().MatchRegex(
                "(?i)WHERE \\(?\"?SourceLineHash\"? IS NULL\\)?",
                $"{vName} governs TechieFlow rows only");
        }

        foreach (var vName in new[] { "UcMissUserRepoSourceLine", "UcMissFixUserRepoSourceLine", "UcMissAmendUserRepoSourceLine" })
        {
            vDefinitions.GetValueOrDefault(vName).Should().MatchRegex(
                "(?i)WHERE \\(?\"?SourceLineHash\"? IS NOT NULL\\)?",
                $"{vName} governs Playbook rows only");
        }
    }

    /// <summary>The DDL file declares the same six keys, so a fresh database gets them too.</summary>
    [Fact]
    public void SchemaDeclaresTheTechieFlowMissKeysAsPartial()
    {
        var vSchema = File.ReadAllText(Path.Combine(RepoRoot(), "database", "001-schema.sql"));

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        foreach (var vName in new[] { "UcMissUserRepoMissId", "UcMissFixUserRepoMissIdFixRunId", "UcMissAmendUserRepoMissIdFieldTs" })
        {
            var vMatch = Regex.Match(
                vSchema,
                $"CREATE UNIQUE INDEX IF NOT EXISTS \"{vName}\"(?<body>[^;]*);",
                RegexOptions.Singleline);
            vMatch.Success.Should().BeTrue($"{vName} must still be declared");
            vMatch.Groups["body"].Value.Should().Contain(
                "WHERE \"SourceLineHash\" IS NULL",
                $"{vName} must not govern Playbook rows");
        }

        foreach (var vName in new[] { "UcMissFixUserRepoSourceLine", "UcMissAmendUserRepoSourceLine" })
        {
            vSchema.Should().MatchRegex(
                $"CREATE UNIQUE INDEX IF NOT EXISTS \"{vName}\"[^;]*WHERE \"SourceLineHash\" IS NOT NULL;",
                $"{vName} is the Playbook key of its table");
        }
    }

    /// <summary>Parses one stream through the shared parser and upserts it.</summary>
    /// <param name="aRepo">The source the rows are stored under.</param>
    /// <param name="aKind">The stream kind.</param>
    /// <param name="aLines">The stream's lines.</param>
    /// <returns>How many rows the database wrote.</returns>
    private Task<int> StoreAsync(string aRepo, StreamKind aKind, IEnumerable<string> aLines) =>
        objStore.UpsertAsync(objParser.Parse(UserId, aRepo, Sha, aKind, string.Join('\n', aLines) + "\n"));

    /// <summary>Counts one table's rows for a source, bypassing the framework join the reads use.</summary>
    /// <param name="aTable">The table name, unquoted.</param>
    /// <param name="aRepo">The source.</param>
    /// <returns>The row count.</returns>
    private static async Task<int> CountAsync(string aTable, string aRepo)
    {
        await using var vConnection = new NpgsqlConnection(TestDatabase.ConnectionStringOrNull());
        await vConnection.OpenAsync();

        return await vConnection.ExecuteScalarAsync<int>(
            $"""SELECT COUNT(*) FROM "{aTable}" WHERE "UserId" = @UserId AND "Repo" = @Repo""",
            new { UserId, Repo = aRepo });
    }

    /// <summary>Removes every row the reserved user holds in the miss tables.</summary>
    /// <returns>A task that completes when they are gone.</returns>
    private static async Task PurgeAsync()
    {
        await using var vConnection = new NpgsqlConnection(TestDatabase.ConnectionStringOrNull());
        await vConnection.OpenAsync();

        foreach (var vTable in new[] { "Miss", "MissFix", "MissAmend" })
        {
            await vConnection.ExecuteAsync(
                $"""DELETE FROM "{vTable}" WHERE "UserId" = @UserId""",
                new { UserId });
        }
    }

    /// <summary>Fails with the command to fix it when no database is configured.</summary>
    private static void RequireDatabase() =>
        Assert.True(TestDatabase.ConnectionStringOrNull() is not null, TestDatabase.NotConfiguredReason);

    /// <summary>Walks up from the test binary to the repository root.</summary>
    /// <returns>The directory holding <c>TfLens.slnx</c>.</returns>
    private static string RepoRoot()
    {
        var vDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (vDirectory is not null && !File.Exists(Path.Combine(vDirectory.FullName, "TfLens.slnx")))
        {
            vDirectory = vDirectory.Parent;
        }

        return vDirectory?.FullName ?? throw new InvalidOperationException("TfLens.slnx was not found above the test binary.");
    }
}
