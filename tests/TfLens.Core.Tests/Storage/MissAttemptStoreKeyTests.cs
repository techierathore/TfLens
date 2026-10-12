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
/// REQ-FN-144 against the real PostgreSQL — the widened miss-fix and review keys store a second fix
/// attempt and a second review correction, and a re-import of the same file still adds nothing.
/// </summary>
/// <remarks>
/// The property is the database's (unique indexes plus an unqualified <c>ON CONFLICT DO NOTHING</c>), so
/// it is tested there. Reserved user id <see cref="UserId"/> is far above any AppManager id and every row
/// it writes is purged in <see cref="DisposeAsync"/>.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MissAttemptStoreKeyTests : IAsyncLifetime
{
    /// <summary>A reserved id no other test class uses (the digits name the requirement).</summary>
    private const int UserId = 90144;

    private const string Repo = "tflenstest/MissAttemptKey";
    private const string Sha = "fn144fixture";

    /// <summary>One miss, two fix attempts in one fix run, two corrections of one reviewed run.</summary>
    private static readonly string[] Lines =
    [
        """{"v":1,"ts":"2026-10-06T08:00:00Z","kind":"miss","app":"K","miss_id":"MISS-K-1","req_id":"REQ-FN-001"}""",
        """{"v":1,"ts":"2026-10-06T10:00:00Z","kind":"miss-fix","app":"K","miss_id":"MISS-K-1","fix_run_id":"2026-10-06T09:00:00Z","fix_attempt":1,"verdict_after":"FAIL"}""",
        """{"v":1,"ts":"2026-10-06T11:00:00Z","kind":"miss-fix","app":"K","miss_id":"MISS-K-1","fix_run_id":"2026-10-06T09:00:00Z","fix_attempt":2,"verdict_after":"PASS"}""",
        """{"v":1,"ts":"2026-10-01T10:00:00Z","kind":"review","app":"K","phase":"day1-review","reviewed_run_id":"2026-10-01T08:00:00Z","correction_run_id":"2026-10-01T09:00:00Z","corrections":3}""",
        """{"v":1,"ts":"2026-10-01T12:00:00Z","kind":"review","app":"K","phase":"day1-review","reviewed_run_id":"2026-10-01T08:00:00Z","correction_run_id":"2026-10-01T11:00:00Z","corrections":3}"""
    ];

    private readonly StreamParser objParser = new();
    private PostgresStore objStore = null!;

    /// <summary>Applies the schema and clears the reserved user before the test.</summary>
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
    /// Both fix attempts and both review corrections are stored; the identical file again adds zero.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-144 — the store keeps both attempts and both corrections, and a re-import adds nothing")]
    public async Task StoreKeepsBothAndReimportAddsNothing()
    {
        Assert.True(TestDatabase.ConnectionStringOrNull() is not null, TestDatabase.NotConfiguredReason);

        var vFirst = await StoreAsync();
        var vSecond = await StoreAsync();

        using var vScope = new FluentAssertions.Execution.AssertionScope();
        vFirst.Should().Be(5);
        vSecond.Should().Be(0, "re-importing the same file is idempotent");
        (await CountAsync("MissFix")).Should().Be(2);
        (await CountAsync("MissReview")).Should().Be(2);
    }

    /// <summary>Parses the lines through the shared parser and upserts them.</summary>
    /// <returns>How many rows the database wrote.</returns>
    private Task<int> StoreAsync() =>
        objStore.UpsertAsync(objParser.Parse(UserId, Repo, Sha, StreamKind.Misses, string.Join('\n', Lines) + "\n"));

    /// <summary>Counts one table's rows for the reserved user.</summary>
    /// <param name="aTable">The table name, unquoted.</param>
    /// <returns>The row count.</returns>
    private static async Task<int> CountAsync(string aTable)
    {
        await using var vConnection = new NpgsqlConnection(TestDatabase.ConnectionStringOrNull());
        await vConnection.OpenAsync();

        return await vConnection.ExecuteScalarAsync<int>(
            $"""SELECT COUNT(*) FROM "{aTable}" WHERE "UserId" = @UserId""", new { UserId });
    }

    /// <summary>Removes every row the reserved user holds in the miss tables.</summary>
    /// <returns>A task that completes when they are gone.</returns>
    private static async Task PurgeAsync()
    {
        await using var vConnection = new NpgsqlConnection(TestDatabase.ConnectionStringOrNull());
        await vConnection.OpenAsync();

        foreach (var vTable in new[] { "Miss", "MissFix", "MissAmend", "MissReview", "MissVoid" })
        {
            await vConnection.ExecuteAsync($"""DELETE FROM "{vTable}" WHERE "UserId" = @UserId""", new { UserId });
        }
    }
}
