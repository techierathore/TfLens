using System.Reflection;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Storage;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// The structural half of the miss-stream contract (REQ-FN-071, REQ-FN-074, BRD-112, BRD-115).
/// </summary>
/// <remarks>
/// The integration tests prove the rows land and the purge empties the tables; these prove an
/// implementer could not have written the code any other way. In particular, the purge and the rebuild
/// walk <b>one</b> table list, so a stream table cannot be added to one and forgotten by the other —
/// which is exactly how a removed repository would keep contributing rows to every figure.
/// </remarks>
public sealed class MissStreamContractTests
{
    /// <summary>Every table the purge and the rebuild walk, read off the store itself.</summary>
    private static readonly string[] PurgedTables =
        (string[])typeof(PostgresStore)
            .GetField("StreamTables", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    /// <summary>The purge covers all four miss tables, so a repo removal leaves none of them behind.</summary>
    /// <remarks>
    /// <c>"MissReview"</c> joined the list on 2026-09-08 (BRD-115). A miss table this list forgets is a
    /// table whose rows survive a removal and go on contributing to every figure for a repository the
    /// owner believes they removed.
    /// </remarks>
    [Fact]
    public void ThePurgeCoversAllFourMissTables()
    {
        Assert.Contains("Miss", PurgedTables);
        Assert.Contains("MissFix", PurgedTables);
        Assert.Contains("MissAmend", PurgedTables);
        Assert.Contains("MissReview", PurgedTables);
    }

    /// <summary>The DDL declares the fourth table, its COALESCE key and the two 2026-09-07 columns.</summary>
    /// <remarks>
    /// The <c>COALESCE("ProducedRunId", '')</c> is asserted as text because it is not cosmetic: a
    /// nullable column never collides with itself in a PostgreSQL unique index, so without it a review
    /// naming no produce run would be re-inserted by every sync.
    /// </remarks>
    [Fact]
    public void TheSchemaDeclaresTheFourthMissTable()
    {
        var vSchema = File.ReadAllText(Path.Combine(RepoTree.Root.FullName, "database", "001-schema.sql"));

        Assert.Contains("""CREATE TABLE IF NOT EXISTS "MissReview" """.TrimEnd(), vSchema, StringComparison.Ordinal);
        Assert.Contains(
            """ON "MissReview" ("UserId", "Repo", "ReviewPhase", COALESCE("ProducedRunId", ''))""",
            vSchema,
            StringComparison.Ordinal);
        Assert.Contains("""ALTER TABLE "Miss" ADD COLUMN IF NOT EXISTS "Sort" text NULL;""", vSchema, StringComparison.Ordinal);
        Assert.Contains("""ALTER TABLE "Miss" ADD COLUMN IF NOT EXISTS "What" text NULL;""", vSchema, StringComparison.Ordinal);
        Assert.Contains("'MissReview'", vSchema, StringComparison.Ordinal);
    }

    /// <summary>The purge still covers every stream table that existed before the miss stream.</summary>
    [Fact]
    public void ThePurgeStillCoversEveryOtherStreamTable()
    {
        foreach (var vTable in new[] { "Run", "Gate", "Session", "Commit", "PbEvent" })
        {
            Assert.Contains(vTable, PurgedTables);
        }
    }

    /// <summary>The store exposes a user-scoped read for each of the four miss record kinds.</summary>
    [Fact]
    public void TheStoreExposesAReadForEachMissKind()
    {
        foreach (var vName in new[]
        {
            "ReadMissesAsync", "ReadMissFixesAsync", "ReadMissAmendsAsync", "ReadMissReviewsAsync"
        })
        {
            var vMethod = typeof(ITelemetryStore).GetMethod(vName);
            Assert.NotNull(vMethod);
            Assert.Equal("aUserId", vMethod!.GetParameters()[0].Name);
            Assert.Equal("aFramework", vMethod.GetParameters()[1].Name);
        }
    }

    /// <summary>The fifth stream is in the TechieFlow list, which is what makes the sync fetch it.</summary>
    [Fact]
    public void MissesIsPartOfTheTechieFlowStreamSet()
    {
        Assert.Contains(StreamNames.Misses, StreamNames.TechieFlow);
        Assert.Equal(StreamKind.Misses, StreamNames.ToKind(StreamNames.Misses));
    }

    /// <summary>
    /// The columns the import cluster owns exist on the model, and a fetched source leaves them alone.
    /// </summary>
    /// <remarks>
    /// Schema-only groundwork for REQ-FN-084 / REQ-FN-085. The invariant those REQs enforce in code is
    /// that a source carries <c>LastSha</c> <b>or</b> <c>BundleSha</c>, never both; the default here is
    /// what keeps it true for everything TfLens writes today.
    /// </remarks>
    [Fact]
    public void UserRepoCarriesTheImportColumnsAndDefaultsToSynced()
    {
        var vRepo = new UserRepo
        {
            UserId = 1,
            Repo = "owner/name",
            Owner = "owner",
            Name = "name",
            Branch = "main",
            Kind = FrameworkNames.TechieFlow,
            Framework = FrameworkNames.TechieFlow,
            ConnectedTs = "2026-08-28T00:00:00Z"
        };

        Assert.Equal(SourceKinds.Api, vRepo.SourceKind);
        Assert.Null(vRepo.BundleSha);
        Assert.Null(vRepo.LastImportTs);
    }
}
