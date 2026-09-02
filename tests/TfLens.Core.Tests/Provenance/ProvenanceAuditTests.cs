using FluentAssertions;
using TfLens.Core.Provenance;

namespace TfLens.Core.Tests.Provenance;

/// <summary>
/// REQ-NFR-019 clause 3 / BRD-143 — the check that reports provenance nobody obtained.
/// </summary>
/// <remarks>
/// <para>
/// The dataset here is the real one. On 2026-08-29 the BRD §13 parity re-run found 155 rows across
/// <c>Gate</c>/<c>Run</c>/<c>Session</c>/<c>Commit</c> under user 2 carrying two <c>SourceSha</c> values
/// that do not exist in their repositories — <c>a91f3c2e…</c> on <c>techierathore/TechieFlow</c> and
/// <c>e3b9d40a…</c> on <c>techierathore/TrBlazeUI</c>, both well-formed hex, both HTTP 422 from the
/// GitHub commits API. They were caught only because their counts disagreed with upstream. These tests
/// pin the property that makes the size of the forgery irrelevant: a SHA no ingest path recorded
/// obtaining is a finding whether it carries 155 rows or one.
/// </para>
/// <para>
/// Every case runs against <see cref="ProvenanceAudit.Compare"/> directly, with no database and no
/// network — which is the acceptance's own wording, and the reason the check is usable in a deployment
/// that has neither.
/// </para>
/// </remarks>
public sealed class ProvenanceAuditTests
{
    private const int UserId = 2;
    private const string TechieFlow = "techierathore/TechieFlow";
    private const string TrBlazeUi = "techierathore/TrBlazeUI";

    /// <summary>The SHA the 2026-08-29 re-run found on 77 TechieFlow rows; not a commit in that repo.</summary>
    private const string FabricatedFlowSha = "a91f3c2e4b7d9018f5c6a2b3d4e5f60718293a4b";

    /// <summary>The SHA the same re-run found on 78 TrBlazeUI rows; likewise not a commit.</summary>
    private const string FabricatedBlazeSha = "e3b9d40a1c2b3d4e5f60718293a4b5c6d7e8f901";

    /// <summary>A SHA a sync genuinely fetched at — the newest one <c>"SyncState"</c> holds.</summary>
    private const string SyncedSha = "696b5eb2e5df9ee11ac02dbead27f72b8fd33e3c";

    /// <summary>A SHA an <i>earlier</i> sync fetched at; the archive is the only oracle that still holds it.</summary>
    private const string EarlierSha = "4f6f5bbafa01f0362fdf95f3ad3837a6f3aa2556";

    /// <summary>The sha256 of an uploaded bundle — an imported source's dataset identity (BRD-134).</summary>
    private const string BundleSha =
        "1b4f0e9851971998e732078544c96b36c3d01cedf7caa332359d6f1d83567014";

    /// <summary>A store whose every SHA some ingest path recorded obtaining reports nothing.</summary>
    [Fact]
    public void CleanStoreReportsNoOrphans()
    {
        var vReport = ProvenanceAudit.Compare(
            [
                new StoredProvenance(UserId, TechieFlow, SyncedSha, "Gate", 40),
                new StoredProvenance(UserId, TechieFlow, SyncedSha, "Run", 7)
            ],
            [Api(TechieFlow, SyncedSha)]);

        vReport.IsSupported.Should().BeTrue();
        vReport.HasOrphans.Should().BeFalse();
        vReport.Orphans.Should().BeEmpty();
        vReport.RowsAudited.Should().Be(47);
        vReport.SourcesAudited.Should().Be(1);
    }

    /// <summary>
    /// The exact 2026-08-29 pollution: two invented SHAs across four tables, reported with the tables
    /// and the row count an operator needs before purging anything.
    /// </summary>
    [Fact]
    public void FabricatedShasAreReportedWithTheirTablesAndRowCounts()
    {
        var vReport = ProvenanceAudit.Compare(
            [
                new StoredProvenance(UserId, TechieFlow, SyncedSha, "Run", 13),
                new StoredProvenance(UserId, TechieFlow, FabricatedFlowSha, "Gate", 34),
                new StoredProvenance(UserId, TechieFlow, FabricatedFlowSha, "Run", 20),
                new StoredProvenance(UserId, TechieFlow, FabricatedFlowSha, "Session", 12),
                new StoredProvenance(UserId, TechieFlow, FabricatedFlowSha, "Commit", 11),
                new StoredProvenance(UserId, TrBlazeUi, FabricatedBlazeSha, "Gate", 78)
            ],
            [Api(TechieFlow, SyncedSha)]);

        vReport.HasOrphans.Should().BeTrue();
        vReport.Orphans.Should().HaveCount(2);
        vReport.OrphanRows.Should().Be(155, "that is exactly what the parity re-run had to purge");

        var vFlow = vReport.Orphans.Single(aOrphan => aOrphan.SourceSha == FabricatedFlowSha);
        vFlow.Repo.Should().Be(TechieFlow);
        vFlow.Rows.Should().Be(77);
        vFlow.Tables.Should().BeEquivalentTo(["Commit", "Gate", "Run", "Session"]);

        vReport.Orphans.Single(aOrphan => aOrphan.SourceSha == FabricatedBlazeSha).Rows.Should().Be(78);
    }

    /// <summary>
    /// One fabricated row is the whole finding: the check does not need the counts to disagree with
    /// upstream, which is the failure BRD-143 was written to close.
    /// </summary>
    [Fact]
    public void ASingleFabricatedRowIsReported()
    {
        var vReport = ProvenanceAudit.Compare(
            [
                new StoredProvenance(UserId, TechieFlow, SyncedSha, "Gate", 400),
                new StoredProvenance(UserId, TechieFlow, FabricatedFlowSha, "Gate", 1)
            ],
            [Api(TechieFlow, SyncedSha)]);

        vReport.Orphans.Should().ContainSingle().Which.Rows.Should().Be(1);
    }

    /// <summary>
    /// An imported source is accounted for by its bundle sha256 and is never flagged merely for having
    /// no commit SHA.
    /// </summary>
    [Fact]
    public void ImportedBundleIdentityAccountsForItsRows()
    {
        var vReport = ProvenanceAudit.Compare(
            [new StoredProvenance(UserId, TrBlazeUi, BundleSha, "Miss", 9)],
            [new SourceProvenanceRecord(UserId, TrBlazeUi, BundleSha, ProvenanceKinds.Import, "2026-08-28T00:00:00Z")]);

        vReport.HasOrphans.Should().BeFalse();
    }

    /// <summary>
    /// A SHA an earlier sync obtained is accounted for by the raw archive, even though
    /// <c>"SyncState"</c> has since moved on to a newer one.
    /// </summary>
    /// <remarks>
    /// User 2's four repositories legitimately hold rows on eight different SHAs while
    /// <c>"SyncState"</c> holds four. A check that only read <c>"SyncState"</c> would report six real
    /// datasets as fabricated, and a check that cries wolf is a check nobody runs (REQ-NFR-018).
    /// </remarks>
    [Fact]
    public void EarlierSyncShaIsAccountedForByTheRawArchive()
    {
        var vReport = ProvenanceAudit.Compare(
            [new StoredProvenance(UserId, TechieFlow, EarlierSha, "Gate", 43)],
            [
                Api(TechieFlow, SyncedSha),
                new SourceProvenanceRecord(UserId, TechieFlow, EarlierSha, ProvenanceKinds.Archive, string.Empty)
            ]);

        vReport.HasOrphans.Should().BeFalse();
    }

    /// <summary>
    /// Provenance is per repository: the same SHA obtained for one repository does not account for rows
    /// attributed to another.
    /// </summary>
    [Fact]
    public void ProvenanceDoesNotCrossRepositories()
    {
        var vReport = ProvenanceAudit.Compare(
            [new StoredProvenance(UserId, TrBlazeUi, SyncedSha, "Gate", 3)],
            [Api(TechieFlow, SyncedSha)]);

        vReport.Orphans.Should().ContainSingle().Which.Repo.Should().Be(TrBlazeUi);
    }

    /// <summary>
    /// Provenance is per user too — one user's sync never vouches for another user's rows (ADR-013).
    /// </summary>
    [Fact]
    public void ProvenanceDoesNotCrossUsers()
    {
        var vReport = ProvenanceAudit.Compare(
            [new StoredProvenance(3, TechieFlow, SyncedSha, "Gate", 5)],
            [Api(TechieFlow, SyncedSha)]);

        vReport.Orphans.Should().ContainSingle().Which.UserId.Should().Be(3);
    }

    /// <summary>
    /// Case is not a difference: git writes a SHA lower-case and a hand-typed record often does not, and
    /// reporting that as a forgery would be a false alarm.
    /// </summary>
    [Fact]
    public void ShaComparisonIgnoresCaseAndSurroundingSpace()
    {
        var vReport = ProvenanceAudit.Compare(
            [new StoredProvenance(UserId, TechieFlow, SyncedSha.ToUpperInvariant(), "Gate", 2)],
            [Api(TechieFlow, "  " + SyncedSha + "  ")]);

        vReport.HasOrphans.Should().BeFalse();
    }

    /// <summary>A ledger entry with no SHA vouches for nothing, so it cannot launder a blank row.</summary>
    [Fact]
    public void BlankLedgerEntryAccountsForNothing()
    {
        var vReport = ProvenanceAudit.Compare(
            [new StoredProvenance(UserId, TechieFlow, FabricatedFlowSha, "Gate", 1)],
            [Api(TechieFlow, "   ")]);

        vReport.HasOrphans.Should().BeTrue();
    }

    /// <summary>The printed report names the SHA, the repository, the tables and the row count.</summary>
    [Fact]
    public void DescribeNamesTheShaTheRepositoryAndTheRows()
    {
        var vReport = ProvenanceAudit.Compare(
            [new StoredProvenance(UserId, TechieFlow, FabricatedFlowSha, "Gate", 34)],
            []);

        var vLines = string.Join('\n', ProvenanceAudit.Describe(vReport));

        vLines.Should().Contain(FabricatedFlowSha);
        vLines.Should().Contain(TechieFlow);
        vLines.Should().Contain("Gate");
        vLines.Should().Contain("34 row(s)");
    }

    /// <summary>
    /// A store that cannot audit says so, and is never reported as clean — an absent answer and a clean
    /// answer are different facts.
    /// </summary>
    [Fact]
    public void UnsupportedIsNotTheSameAsClean()
    {
        ProvenanceAuditReport.Unsupported.IsSupported.Should().BeFalse();
        ProvenanceAuditReport.Unsupported.HasOrphans.Should().BeFalse();
        ProvenanceAuditReport.Clean.IsSupported.Should().BeTrue();

        var vDescription = string.Join('\n', ProvenanceAudit.Describe(ProvenanceAuditReport.Unsupported));

        vDescription.Should().Contain("cannot be audited");
        vDescription.Should().Contain(
            "may be quoted",
            "the description has to state the consequence, not just the absence: since 2026-08-30 an "
            + "unauditable store is a refusal rather than a shrug (REQ-NFR-019 gap b, BRD-89)");
    }

    /// <summary>Builds a ledger entry a sync would have written.</summary>
    /// <param name="aRepo">The repository the sync fetched.</param>
    /// <param name="aSha">The SHA it fetched at.</param>
    /// <returns>The entry.</returns>
    private static SourceProvenanceRecord Api(string aRepo, string aSha) =>
        new(UserId, aRepo, aSha, ProvenanceKinds.Api, "2026-08-29T00:00:00Z");
}
