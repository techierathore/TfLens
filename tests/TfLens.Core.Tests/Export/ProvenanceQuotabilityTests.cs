using System.Text.Json;
using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Provenance;

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
    /// A store that cannot audit asserts nothing, so it neither upgrades nor downgrades the stamp — an
    /// absent answer is not a clean bill of health, and it is not an accusation either.
    /// </summary>
    [Fact]
    public void AnUnauditableStoreChangesNothing()
    {
        var vScript = WriteScript("#!/usr/bin/env bash\necho rollup\n");
        var vRecord = PassingRecord(vScript);

        ParityRecord.EvaluateWithProvenance(vRecord, ParserVersionUnderTest, vScript, ProvenanceAuditReport.Unsupported)
            .Should().Be(ParityRecord.EvaluateFor(vRecord, ParserVersionUnderTest, vScript));

        ParityRecord.EvaluateWithProvenance(vRecord, ParserVersionUnderTest, vScript, null)
            .Should().Be(ParityRecord.EvaluateFor(vRecord, ParserVersionUnderTest, vScript));
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
                ["per_repo", "tainted_reqs", "live", "backfilled", "pooled", "misses", "extras", "parity"],
                "extras and parity stay the only additions to the reference's key layout (REQ-FN-058)");
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
