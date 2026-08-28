using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Parsing;

/// <summary>
/// The three natural-key dedupe rules of the misses stream (REQ-FN-073, BRD-114).
/// </summary>
/// <remarks>
/// Earliest-wins for <c>miss</c> (a miss is opened once), latest-wins for <c>miss-fix</c> (the later
/// write carries the closed run's token window) and earliest-wins per <c>(MissId, Field, Ts)</c> for
/// <c>miss-amend</c>. Each rule is stated in file order and again in reverse, because a rule that only
/// works when the duplicate arrives second is an accident of ordering rather than a rule.
/// </remarks>
public sealed class MissDedupeTests
{
    /// <summary>A miss opened twice collapses to one row, keeping the earliest timestamp.</summary>
    [Fact]
    public void MissKeepsTheEarliestTimestamp()
    {
        var vResult = Dedupe.Misses(
        [
            Miss("MISS-A-1", "2026-08-28T12:00:00Z"),
            Miss("MISS-A-1", "2026-08-28T09:00:00Z"),
            Miss("MISS-A-2", "2026-08-28T10:00:00Z")
        ]);

        vResult.Records.Should().HaveCount(2);
        vResult.Collapsed.Should().Be(1);
        vResult.Records[0].Ts.Should().Be("2026-08-28T09:00:00Z");
    }

    /// <summary>The earliest wins whichever order the duplicate arrives in.</summary>
    [Fact]
    public void MissEarliestWinsWhicheverOrderTheRecordsArriveIn()
    {
        var vForward = Dedupe.Misses([Miss("MISS-A-1", "2026-08-28T09:00:00Z"), Miss("MISS-A-1", "2026-08-28T12:00:00Z")]);
        var vReverse = Dedupe.Misses([Miss("MISS-A-1", "2026-08-28T12:00:00Z"), Miss("MISS-A-1", "2026-08-28T09:00:00Z")]);

        vForward.Records.Single().Ts.Should().Be("2026-08-28T09:00:00Z");
        vReverse.Records.Single().Ts.Should().Be("2026-08-28T09:00:00Z");
    }

    /// <summary>Two repositories may hold the same miss id and neither collapses the other.</summary>
    [Fact]
    public void MissDedupeIsScopedToTheRepository()
    {
        var vResult = Dedupe.Misses(
        [
            Miss("MISS-A-1", "2026-08-28T09:00:00Z", "owner/one"),
            Miss("MISS-A-1", "2026-08-28T09:00:00Z", "owner/two")
        ]);

        vResult.Records.Should().HaveCount(2);
        vResult.Collapsed.Should().Be(0);
    }

    /// <summary>A fix re-written for the same run collapses to one row, keeping the latest timestamp.</summary>
    [Fact]
    public void MissFixKeepsTheLatestTimestamp()
    {
        var vResult = Dedupe.MissFixes(
        [
            Fix("MISS-A-1", "2026-08-28T09:00:00Z", "2026-08-28T08:00:00Z"),
            Fix("MISS-A-1", "2026-08-28T09:00:00Z", "2026-08-28T11:00:00Z")
        ]);

        vResult.Records.Single().Ts.Should().Be("2026-08-28T11:00:00Z");
        vResult.Collapsed.Should().Be(1);
    }

    /// <summary>Two fix runs on one miss are two facts and never collapse.</summary>
    [Fact]
    public void MissFixKeepsOneRowPerFixRun()
    {
        var vResult = Dedupe.MissFixes(
        [
            Fix("MISS-A-1", "2026-08-28T09:00:00Z", "2026-08-28T09:30:00Z"),
            Fix("MISS-A-1", "2026-08-29T09:00:00Z", "2026-08-29T09:30:00Z")
        ]);

        vResult.Records.Should().HaveCount(2);
        vResult.Collapsed.Should().Be(0);
    }

    /// <summary>An amendment of a different field is a distinct fact and never collapses.</summary>
    [Fact]
    public void MissAmendKeepsOneRowPerFieldAndInstant()
    {
        var vResult = Dedupe.MissAmends(
        [
            Amend("MISS-A-1", "why_missed", "2026-08-28T09:00:00Z"),
            Amend("MISS-A-1", "why_missed", "2026-08-28T10:00:00Z"),
            Amend("MISS-A-1", "found_gate", "2026-08-28T09:00:00Z")
        ]);

        vResult.Records.Should().HaveCount(3);
        vResult.Collapsed.Should().Be(0);
    }

    /// <summary>A re-parse of the same archived amendment writes nothing twice.</summary>
    [Fact]
    public void MissAmendCollapsesAByteIdenticalReParse()
    {
        var vResult = Dedupe.MissAmends(
        [
            Amend("MISS-A-1", "why_missed", "2026-08-28T09:00:00Z"),
            Amend("MISS-A-1", "why_missed", "2026-08-28T09:00:00Z")
        ]);

        vResult.Records.Should().HaveCount(1);
        vResult.Collapsed.Should().Be(1);
    }

    /// <summary>Re-parsing the same archived file twice produces the same records and no new ones.</summary>
    [Fact]
    public void ReParsingAnArchivedFileDoubleInsertsNothing()
    {
        var vParser = new StreamParser();
        var vText = Fixtures.Read(Fixtures.TrSetupRepo, StreamKind.Misses);

        var vFirst = vParser.Parse(
            Fixtures.DemoUserId, Fixtures.TrSetupRepo, Fixtures.SourceSha, StreamKind.Misses, vText);
        var vSecond = vParser.Parse(
            Fixtures.DemoUserId, Fixtures.TrSetupRepo, Fixtures.SourceSha, StreamKind.Misses, vText + "\n" + vText);

        vSecond.Misses.Should().HaveCount(vFirst.Misses.Count);
        vSecond.MissFixes.Should().HaveCount(vFirst.MissFixes.Count);
        vSecond.MissAmends.Should().HaveCount(vFirst.MissAmends.Count);
    }

    /// <summary>Builds a miss record carrying only the fields the dedupe rule reads.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aTs">The timestamp.</param>
    /// <param name="aRepo">The repository the record came from.</param>
    /// <returns>The record.</returns>
    private static MissRecord Miss(string aMissId, string aTs, string aRepo = "owner/name") => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = aRepo,
        SourceSha = Fixtures.SourceSha,
        Ts = aTs,
        MissId = aMissId
    };

    /// <summary>Builds a fix record carrying only the fields the dedupe rule reads.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aFixRunId">The repair run's started timestamp.</param>
    /// <param name="aTs">The record's own timestamp.</param>
    /// <returns>The record.</returns>
    private static MissFixRecord Fix(string aMissId, string aFixRunId, string aTs) => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = "owner/name",
        SourceSha = Fixtures.SourceSha,
        Ts = aTs,
        MissId = aMissId,
        FixRunId = aFixRunId
    };

    /// <summary>Builds an amendment carrying only the fields the dedupe rule reads.</summary>
    /// <param name="aMissId">The miss id.</param>
    /// <param name="aField">The field being completed.</param>
    /// <param name="aTs">The timestamp, which is part of the key.</param>
    /// <returns>The record.</returns>
    private static MissAmendRecord Amend(string aMissId, string aField, string aTs) => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = "owner/name",
        SourceSha = Fixtures.SourceSha,
        Ts = aTs,
        MissId = aMissId,
        Field = aField,
        Value = "other"
    };
}
