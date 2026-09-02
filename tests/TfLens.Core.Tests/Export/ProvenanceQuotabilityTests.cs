using System.Text.Json;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Provenance;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// REQ-NFR-019 clause 4 / BRD-143 — <c>/export</c> refuses QUOTABLE while the store holds rows whose
/// provenance nobody obtained.
/// </summary>
/// <remarks>
/// <para>
/// The acceptance says "for the same reason it refuses when the reference script has changed", and that
/// is the shape of these tests: a fabricated <c>source_sha</c> is treated exactly as a changed oracle
/// is — the stamp goes NOT QUOTABLE, with a reason a reader can tell apart from the other four, and it
/// comes back on its own once the finding is gone. Nothing upgrades a status by any other means.
/// </para>
/// <para>
/// The written document is checked too, because the banner and the file must not be able to disagree:
/// both read <see cref="ParityRecord.EvaluateWithProvenance"/>, and <c>tflens.json</c> carries the
/// verdict in <c>parity.status_reason</c> — an extended <b>reason</b> vocabulary, not a new key, so
/// <c>extras</c> and <c>parity</c> remain the only two additions to the reference's top-level layout
/// (REQ-FN-058).
/// </para>
/// </remarks>
public sealed class ProvenanceQuotabilityTests : IDisposable
{
    private const string ParserVersionUnderTest = "1.0.0";

    private readonly string objFolder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "tflens-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Removes the throwaway folder holding the test's own reference script and data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objFolder))
        {
            Directory.Delete(objFolder, true);
        }
    }

    /// <summary>
    /// A stamp that is otherwise perfectly current is refused while an unaccounted source SHA stands,
    /// and the reason names provenance rather than blaming the parser or the oracle.
    /// </summary>
    [Fact]
    public void OrphanProvenanceRefusesAnOtherwiseQuotableStamp()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript);

        ParityRecord.EvaluateWithProvenance(vRecord, ParserVersionUnderTest, vScript, ProvenanceAuditReport.Clean)
            .Status.Should().Be(ParityStatuses.Quotable, "the stamp starts out valid");

        var vStamp = ParityRecord.EvaluateWithProvenance(
            vRecord, ParserVersionUnderTest, vScript, Polluted());

        vStamp.Status.Should().Be(ParityStatuses.NotQuotable);
        vStamp.Reason.Should().Be(ParityReasons.ProvenanceOrphan);
        vStamp.Reason.Should().NotBe(ParityReasons.ScriptChanged);
        vStamp.Reason.Should().NotBe(ParityReasons.ParserChanged);
    }

    /// <summary>
    /// Purging the rows restores the stamp with no further action — the refusal is a live reading of the
    /// store, not a latch someone has to clear.
    /// </summary>
    [Fact]
    public void RemovingTheOrphanRestoresTheStamp()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript);

        ParityRecord.EvaluateWithProvenance(vRecord, ParserVersionUnderTest, vScript, Polluted())
            .Status.Should().Be(ParityStatuses.NotQuotable);

        ParityRecord.EvaluateWithProvenance(vRecord, ParserVersionUnderTest, vScript, ProvenanceAuditReport.Clean)
            .Status.Should().Be(ParityStatuses.Quotable);
    }

    /// <summary>
    /// Provenance is decided first: a store carrying invented rows is not quotable whatever else is
    /// stale, and the banner sends the reader to the fix that matters.
    /// </summary>
    [Fact]
    public void ProvenanceOutranksTheOtherInvalidators()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");

        ParityRecord.EvaluateWithProvenance(null, ParserVersionUnderTest, vScript, Polluted())
            .Reason.Should().Be(ParityReasons.ProvenanceOrphan, "even with no parity run at all");

        ParityRecord.EvaluateWithProvenance(PassingRecord(vScript), "9.9.9", vScript, Polluted())
            .Reason.Should().Be(ParityReasons.ProvenanceOrphan, "even with the parser moved on");
    }

    /// <summary>
    /// A store that cannot audit is refused, not waved through: an otherwise perfect stamp does not
    /// reach QUOTABLE while the provenance question is unanswered (REQ-NFR-019 gap b, BRD-89).
    /// </summary>
    /// <remarks>
    /// This is the inversion of the behaviour shipped on 2026-08-29, which returned the plain
    /// <see cref="ParityRecord.EvaluateFor"/> stamp for an unsupported audit and therefore let a store
    /// nobody had asked reach QUOTABLE. An integrity rule that cannot be evaluated has not passed.
    /// </remarks>
    [Fact]
    public void AnUnauditableStoreIsRefused()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript);

        ParityRecord.EvaluateFor(vRecord, ParserVersionUnderTest, vScript)
            .Status.Should().Be(ParityStatuses.Quotable, "the parity evidence itself is impeccable");

        var vStamp = ParityRecord.EvaluateWithProvenance(
            vRecord, ParserVersionUnderTest, vScript, ProvenanceAuditReport.Unsupported);

        vStamp.Status.Should().Be(ParityStatuses.NotQuotable);
        vStamp.Reason.Should().Be(ParityReasons.ProvenanceUnknown);
    }

    /// <summary>
    /// "Could not check" is a different sentence from "checked and clean" and from "checked and
    /// polluted" — the three verdicts never collapse into one another.
    /// </summary>
    /// <remarks>
    /// The distinguishability is the point, not a nicety. A reader who cannot tell an unevaluated rule
    /// from a passing one has been told the store is clean by a system that never looked, which is the
    /// precise failure BRD-143 records for 2026-08-29 — and a reader who cannot tell it from a finding
    /// would go purging rows that are not there.
    /// </remarks>
    [Fact]
    public void TheThreeProvenanceVerdictsStayApart()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript);

        var vClean = ParityRecord.EvaluateWithProvenance(
            vRecord, ParserVersionUnderTest, vScript, ProvenanceAuditReport.Clean);
        var vUnknown = ParityRecord.EvaluateWithProvenance(
            vRecord, ParserVersionUnderTest, vScript, ProvenanceAuditReport.Unsupported);
        var vOrphaned = ParityRecord.EvaluateWithProvenance(
            vRecord, ParserVersionUnderTest, vScript, Polluted());

        new[] { vClean.Reason, vUnknown.Reason, vOrphaned.Reason }.Should().OnlyHaveUniqueItems();

        vClean.Status.Should().Be(ParityStatuses.Quotable);
        vUnknown.Status.Should().Be(ParityStatuses.NotQuotable);
        vOrphaned.Status.Should().Be(ParityStatuses.NotQuotable);

        ProvenanceAuditReport.Unsupported.HasOrphans.Should()
            .BeFalse("an audit that never ran has no findings, which is not the same as no pollution");
        ProvenanceAuditReport.Unsupported.IsSupported.Should()
            .BeFalse("and IsSupported is the field that tells the two apart");
    }

    /// <summary>
    /// There is no value of the audit argument that means "skip the question": a caller with nothing to
    /// hand over is refused exactly as an audit that could not run is (BRD-89 — no relaxation).
    /// </summary>
    [Fact]
    public void NoAuditArgumentSkipsTheQuestion()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript);

        ParityRecord.EvaluateWithProvenance(vRecord, ParserVersionUnderTest, vScript, null!)
            .Should().Be(ParityRecord.EvaluateWithProvenance(
                vRecord, ParserVersionUnderTest, vScript, ProvenanceAuditReport.Unsupported));
    }

    /// <summary>
    /// The store that backs the whole export test suite really audits itself, and reaches QUOTABLE on
    /// its own merits — so the refusal above is a closed door and not a wall.
    /// </summary>
    /// <remarks>
    /// Proving fail-closed with a hand-built report proves only that a constant was read. This asks a
    /// live store, whose rows and whose ledger were both produced by its ingest doors, and shows that an
    /// audited-clean store still publishes.
    /// </remarks>
    [Fact]
    public async Task ARealAuditedStoreIsCleanAndStillReachesQuotable()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vAudit = await ExportFixture.Store().AuditProvenanceAsync(ExportFixture.UserId);

        vAudit.IsSupported.Should().BeTrue("this store holds a ledger its ingest doors wrote");
        vAudit.HasOrphans.Should().BeFalse();
        vAudit.RowsAudited.Should().BeGreaterThan(0, "an audit over nothing proves nothing");

        ParityRecord.EvaluateWithProvenance(PassingRecord(vScript), ParserVersionUnderTest, vScript, vAudit)
            .Should().Be(new ParityStamp(ParityStatuses.Quotable, ParityReasons.Current));
    }

    /// <summary>
    /// A row that reached the store without passing an ingest door is reported by the store's own audit,
    /// so the fixture's clean answer is a comparison and not a rubber stamp.
    /// </summary>
    [Fact]
    public async Task TheFixtureAuditReportsARowNoDoorAccountsFor()
    {
        var vStore = ExportFixture.Store();

        vStore.SmuggleGate(GateFixtures.Gate(aReqId: "REQ-FN-999") with
        {
            SourceSha = "a91f3c2e4b7d9018f5c6a2b3d4e5f60718293a4b"
        });

        var vAudit = await vStore.AuditProvenanceAsync(ExportFixture.UserId);

        vAudit.IsSupported.Should().BeTrue();
        vAudit.HasOrphans.Should().BeTrue("no door ever recorded obtaining that SHA");
        vAudit.Orphans.Should().ContainSingle()
            .Which.SourceSha.Should().Be("a91f3c2e4b7d9018f5c6a2b3d4e5f60718293a4b");

        ParityRecord.EvaluateWithProvenance(
                PassingRecord(WriteScript("#!/usr/bin/env bash\necho rollup\n")),
                ParserVersionUnderTest,
                null,
                vAudit)
            .Reason.Should().Be(ParityReasons.ProvenanceOrphan);
    }

    /// <summary>
    /// A store with no audit implementation at all — the <c>ITelemetryStore</c> default — cannot publish
    /// a figure: the export refuses it end to end, and says why in the written document.
    /// </summary>
    [Fact]
    public async Task TheWrittenSnapshotRefusesAnUnauditableStore()
    {
        var vDataRoot = Path.Combine(objFolder, "unauditable");
        var vStore = ExportFixture.Store();
        vStore.Provenance = ProvenanceAuditReport.Unsupported;

        var vResult = await ExportFixture
            .Exporter(vDataRoot, vStore)
            .ExportAsync(ExportFixture.UserId, ExportFixture.Framework, ExportFixture.Date);

        vResult.ParityStatus.Should().Be(ParityStatuses.NotQuotable);

        using var vDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vResult.JsonPath));
        var vParity = vDocument.RootElement.GetProperty("parity");

        vParity.GetProperty("status").GetString().Should().Be(ParityStatuses.NotQuotable);
        vParity.GetProperty("status_reason").GetString().Should().Be(ParityReasons.ProvenanceUnknown);

        vDocument.RootElement.EnumerateObject().Select(aProperty => aProperty.Name)
            .Should().BeEquivalentTo(
                [
                    "per_repo", "tainted_reqs", "live", "backfilled", "pooled", "misses", "phases",
                    "extras", "parity"
                ],
                "the new refusal is a reason, never a key of its own (REQ-FN-058). `phases` is on this "
                + "list because the ORACLE emits it (BRD-152); extras and parity remain the only two "
                + "keys TfLens adds to the reference's layout");
    }

    /// <summary>
    /// The written snapshot carries the refusal too, in <c>parity.status_reason</c> — and adds no
    /// top-level key, so <c>tools/parity-compare.py</c> still walks the document unchanged
    /// (REQ-FN-058).
    /// </summary>
    [Fact]
    public async Task WrittenSnapshotReportsTheRefusalWithoutAddingAKey()
    {
        var vDataRoot = Path.Combine(objFolder, "data");
        var vStore = ExportFixture.Store();
        vStore.Provenance = Polluted();

        var vResult = await ExportFixture
            .Exporter(vDataRoot, vStore)
            .ExportAsync(ExportFixture.UserId, ExportFixture.Framework, ExportFixture.Date);

        vResult.ParityStatus.Should().Be(ParityStatuses.NotQuotable);

        using var vDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vResult.JsonPath));
        var vRoot = vDocument.RootElement;

        vRoot.GetProperty("parity").GetProperty("status").GetString()
            .Should().Be(ParityStatuses.NotQuotable);
        vRoot.GetProperty("parity").GetProperty("status_reason").GetString()
            .Should().Be(ParityReasons.ProvenanceOrphan);

        vRoot.EnumerateObject().Select(aProperty => aProperty.Name)
            .Should().BeEquivalentTo(
                [
                    "per_repo", "tainted_reqs", "live", "backfilled", "pooled", "misses", "phases",
                    "extras", "parity"
                ],
                "extras and parity stay the only additions to the reference's key layout (REQ-FN-058); "
                + "`phases` is the oracle's own block, which rides inside --rollup --json (BRD-152)");
    }

    /// <summary>
    /// A snapshot is never written for a reserved harness user id, so seeded rows cannot become a
    /// published figure (REQ-NFR-019 clause 2).
    /// </summary>
    [Fact]
    public async Task ExportRefusesAReservedHarnessUserId()
    {
        var vExporter = ExportFixture.Exporter(Path.Combine(objFolder, "reserved"));

        var vAct = async () => await vExporter.ExportAsync(
            ReservedUserIds.Floor + 1, ExportFixture.Framework, ExportFixture.Date);

        await vAct.Should().ThrowAsync<ProvenanceException>().WithMessage("*REQ-NFR-019*");

        ReservedUserIds.IsReserved(ExportFixture.UserId).Should().BeFalse();
        ReservedUserIds.IsReserved(2).Should().BeFalse("the owner's real AppManager id must still export");
    }

    /// <summary>An audit report standing in for the 2026-08-29 pollution.</summary>
    /// <returns>A report carrying one unaccounted source SHA.</returns>
    private static ProvenanceAuditReport Polluted() =>
        new(
            [
                new ProvenanceOrphan(
                    2,
                    "techierathore/TechieFlow",
                    "a91f3c2e4b7d9018f5c6a2b3d4e5f60718293a4b",
                    ["Commit", "Gate", "Run", "Session"],
                    77)
            ],
            77,
            1,
            true);

    /// <summary>Writes the throwaway reference script and returns its path.</summary>
    /// <param name="aBody">The script text.</param>
    /// <returns>The absolute path of the script.</returns>
    private string WriteScript(string aBody)
    {
        var vPath = Path.Combine(objFolder, "tf-metrics.sh");
        File.WriteAllText(vPath, aBody);
        return vPath;
    }

    /// <summary>Builds a record that says a parity run passed against the given script.</summary>
    /// <param name="aScriptPath">Path the record names as the reference.</param>
    /// <returns>The record.</returns>
    private static ParityRecord PassingRecord(string aScriptPath) =>
        new()
        {
            Date = "2026-08-29",
            Passed = true,
            ParserVersion = ParserVersionUnderTest,
            ScriptPath = aScriptPath,
            ScriptHash = ParityRecord.HashScript(aScriptPath),
            CompareCommand = "tools/parity-compare.py reference.json tflens.json",
            CompareOutput = "PASS",
            RecordedTs = "2026-08-29T00:00:00Z"
        };
}
