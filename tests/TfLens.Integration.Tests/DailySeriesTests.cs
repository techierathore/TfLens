using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Storage;

namespace TfLens.Integration.Tests;

/// <summary>
/// The KPI sparklines plot real, user-scoped, framework-scoped history — or nothing.
/// </summary>
/// <remarks>
/// <para>
/// The mockups put a trend line under every KPI tile. Drawing one is easy; drawing one that is
/// <i>true</i> is the requirement, in a product whose stated failure mode is a plausible wrong number.
/// The rules these tests pin are: a sparkline plots the same quantity its tile states, it never crosses
/// a user or a framework boundary, an empty window draws nothing rather than a flat line, and a quiet
/// day is a zero rather than a gap closed up into a smooth slope.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DailySeriesTests : IAsyncLifetime
{
    private const int UserA = 991001;
    private const int UserB = 991002;
    private const string Repo = "sparkline/probe";

    private readonly PostgresFixture objDb;

    /// <summary>Creates the test class.</summary>
    /// <param name="aDb">The shared live-PostgreSQL fixture.</param>
    public DailySeriesTests(PostgresFixture aDb)
    {
        objDb = aDb;
    }

    /// <summary>Seeds two users' gate rows on known days.</summary>
    /// <returns>A task that completes when the rows exist.</returns>
    public async Task InitializeAsync()
    {
        if (!objDb.IsAvailable)
        {
            return;
        }

        await PurgeAsync();
        await using var vConnection = await objDb.OpenAsync();

        foreach (var vUserId in new[] { UserA, UserB })
        {
            await vConnection.ExecuteAsync(
                """
                INSERT INTO "UserRepo" ("UserId","Repo","Owner","Name","Branch","Kind","Framework","IsPublic","ConnectedTs")
                VALUES (@UserId, @Repo, 'sparkline', 'probe', 'main', 'techieflow', 'techieflow', true, @Ts)
                """,
                new { UserId = vUserId, Repo, Ts = DateTimeOffset.UtcNow.ToString("O") });
        }

        // User A: two rows two days ago, one yesterday — one of them a pass, so a failures-only series
        // must not simply mirror the total.
        var vTwoDaysAgo = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd");
        var vYesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");

        await InsertGateAsync(vConnection, UserA, $"{vTwoDaysAgo}T09:00:00Z", "FAIL");
        await InsertGateAsync(vConnection, UserA, $"{vTwoDaysAgo}T10:00:00Z", "Verified");
        await InsertGateAsync(vConnection, UserA, $"{vYesterday}T09:00:00Z", "FAIL");

        // User B has far more, on a different day — a leak would be obvious in the totals.
        var vThreeDaysAgo = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd");
        for (var vIndex = 0; vIndex < 9; vIndex++)
        {
            await InsertGateAsync(vConnection, UserB, $"{vThreeDaysAgo}T{vIndex + 1:D2}:00:00Z", "FAIL");
        }
    }

    /// <summary>Removes the probe rows.</summary>
    /// <returns>A task that completes when they are gone.</returns>
    public Task DisposeAsync() => objDb.IsAvailable ? PurgeAsync() : Task.CompletedTask;

    /// <summary>
    /// The series counts only the asked-for user's records, and fills quiet days with zero.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task SeriesIsUserScopedAndFillsQuietDaysWithZero()
    {
        RequireDatabase();

        var vSeries = await Store().ReadDailySeriesAsync(UserA, FrameworkNames.TechieFlow, StreamKind.Gates);

        vSeries.Points.Should().HaveCount(14, "the window is generated, so every day appears");
        vSeries.Points.Sum(aP => aP.Count).Should().Be(
            3,
            "user A has three gate rows; user B's nine must not leak in (ADR-013)");

        vSeries.Points.Should().Contain(
            aP => aP.Count == 0,
            "a day with no records is a zero, not a missing point — a gap must read as quiet");

        vSeries.Points.Select(aP => aP.Day).Should().BeInAscendingOrder("the line runs oldest to newest");
        vSeries.IsPlottable.Should().BeTrue();
        vSeries.Label.Should().Be("gate records per day, last 14 days");
    }

    /// <summary>
    /// The failures-only series counts failures, not every verdict.
    /// </summary>
    /// <remarks>
    /// This is the rule that keeps the Failures-scored tile honest: its line must plot the same
    /// quantity the tile states, so a <c>Verified</c> row must not appear in it.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task FailuresOnlySeriesExcludesPassingVerdicts()
    {
        RequireDatabase();

        var vAll = await Store().ReadDailySeriesAsync(UserA, FrameworkNames.TechieFlow, StreamKind.Gates);
        var vFailures = await Store().ReadDailySeriesAsync(
            UserA, FrameworkNames.TechieFlow, StreamKind.Gates, aFailuresOnly: true);

        vAll.Points.Sum(aP => aP.Count).Should().Be(3);
        vFailures.Points.Sum(aP => aP.Count).Should().Be(2, "one of the three rows is Verified");
        vFailures.Label.Should().Be("failure records per day, last 14 days");
    }

    /// <summary>
    /// A framework with no rows yields no series at all, rather than a flat line at zero.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task EmptyWindowYieldsNoSeriesRatherThanAFlatLine()
    {
        RequireDatabase();

        var vSeries = await Store().ReadDailySeriesAsync(UserA, FrameworkNames.Playbook, StreamKind.Gates);

        vSeries.Points.Should().BeEmpty("all-zero history is absence, and absence draws nothing");
        vSeries.IsPlottable.Should().BeFalse();
    }

    /// <summary>
    /// Too few points is not plottable, so a line is never drawn through almost nothing.
    /// </summary>
    [Fact]
    public void TooFewPointsIsNotPlottable()
    {
        var vDay = DateOnly.FromDateTime(DateTime.UtcNow);

        new DailySeries([new DailyCount(vDay, 4)], "x").IsPlottable.Should().BeFalse();
        new DailySeries([new DailyCount(vDay, 4), new DailyCount(vDay.AddDays(1), 5)], "x")
            .IsPlottable.Should().BeFalse("two points draw a straight segment that reads as a trend");
    }

    /// <summary>Fails with a clear reason when PostgreSQL is not reachable.</summary>
    private void RequireDatabase() =>
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

    /// <summary>Builds a store against the live database.</summary>
    /// <returns>The store.</returns>
    private PostgresStore Store() =>
        new(Options.Create(new TfLensOptions { DbConnection = objDb.ConnectionString }),
            new StreamParser(),
            NullLogger<PostgresStore>.Instance);

    /// <summary>Inserts one gate row.</summary>
    /// <param name="aConnection">An open connection.</param>
    /// <param name="aUserId">The owning user.</param>
    /// <param name="aTs">The record's own timestamp.</param>
    /// <param name="aVerdict">The verdict the row carries.</param>
    /// <returns>A task that completes when the row exists.</returns>
    private static Task InsertGateAsync(
        Npgsql.NpgsqlConnection aConnection, int aUserId, string aTs, string aVerdict) =>
        aConnection.ExecuteAsync(
            """
            INSERT INTO "Gate" ("UserId","Repo","SourceSha","V","Ts","App","Verdict","ReqId")
            VALUES (@UserId, @Repo, 'sparklinesha', 1, @Ts, 'Probe', @Verdict, @ReqId)
            """,
            new { UserId = aUserId, Repo, Ts = aTs, Verdict = aVerdict, ReqId = "REQ-" + aTs });

    /// <summary>Removes every probe row.</summary>
    /// <returns>A task that completes when they are gone.</returns>
    private async Task PurgeAsync()
    {
        await using var vConnection = await objDb.OpenAsync();

        foreach (var vTable in new[] { "Gate", "UserRepo" })
        {
            await vConnection.ExecuteAsync(
                $"""DELETE FROM "{vTable}" WHERE "UserId" IN (@A, @B)""",
                new { A = UserA, B = UserB });
        }
    }
}
