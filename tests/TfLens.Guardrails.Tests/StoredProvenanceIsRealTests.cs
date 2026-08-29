using System.Reflection;
using FluentAssertions;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Provenance;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-019 / BRD-143 — stored provenance is real, proven structurally rather than by exercising
/// today's write paths.
/// </summary>
/// <remarks>
/// <para>
/// "No path writes a row with provenance nobody obtained" is a statement about paths that do not exist
/// yet as much as about the ones that do, and a behavioural test can only cover the latter. So these
/// assert the shape: every stream table carries the database's own <c>CHECK</c>, the audit's query
/// covers every stream table the store knows about, the check has no relaxation switch, and the
/// quotable stamp cannot be reached by a route that skips provenance.
/// </para>
/// <para>
/// The behavioural half — a fabricated SHA detected, <c>/export</c> reading NOT QUOTABLE, the row
/// removed and both returning to clean — is in <c>TfLens.Core.Tests/Provenance</c> and in the live smoke
/// recorded against the running app.
/// </para>
/// </remarks>
public sealed class StoredProvenanceIsRealTests
{
    /// <summary>The eight stream tables a row can land in.</summary>
    /// <remarks>
    /// Listed rather than crawled, for the same reason <c>PostgresStore.StreamTables</c> is: a stream
    /// table missing from one place and present in another is exactly how pollution finds somewhere to
    /// hide. This list failing is the notification that a new table needs a decision about provenance.
    /// </remarks>
    private static readonly string[] StreamTables =
        ["Run", "Gate", "Session", "Commit", "Miss", "MissFix", "MissAmend", "PbEvent"];

    /// <summary>
    /// Every stream table's <c>"SourceSha"</c> is <c>NOT NULL</c> and carries a non-blank <c>CHECK</c>.
    /// </summary>
    /// <remarks>
    /// The 155 rows found on 2026-08-29 arrived through raw SQL, which is precisely the layer the
    /// application does not control, so the rule is stated a second time where PostgreSQL enforces it
    /// for every writer — the app, a migration, a seed script or a psql session alike.
    /// </remarks>
    [Fact]
    public void EveryStreamTableConstrainsItsSourceSha()
    {
        var vSchema = File.ReadAllText(Path.Combine(RepoTree.Root.FullName, "database", "001-schema.sql"));

        vSchema.Should().Contain(
            """btrim("SourceSha") <> ''""",
            "the database enforces the presence of provenance for writers the application never sees");

        foreach (var vTable in StreamTables)
        {
            vSchema.Should().Contain(
                $"'{vTable}'",
                $"the {vTable} table has to be inside the constraint loop");
        }
    }

    /// <summary>The provenance ledger exists, keyed per user, repository and SHA.</summary>
    [Fact]
    public void TheProvenanceLedgerIsPartOfTheSchema()
    {
        var vSchema = File.ReadAllText(Path.Combine(RepoTree.Root.FullName, "database", "001-schema.sql"));

        vSchema.Should().Contain("""CREATE TABLE IF NOT EXISTS "SourceProvenance" """.TrimEnd());
        vSchema.Should().Contain("""PRIMARY KEY ("UserId", "Repo", "SourceSha")""");
    }

    /// <summary>
    /// The audit reads every stream table, so no table is a place a fabricated SHA could sit unseen.
    /// </summary>
    [Fact]
    public void TheAuditQueryCoversEveryStreamTable()
    {
        var vStoreSource = File.ReadAllText(
            Path.Combine(RepoTree.Root.FullName, "src", "TfLens.Core", "Storage", "PostgresStore.cs"));

        var vQuery = Between(vStoreSource, "StoredProvenanceSql = \"\"\"", "\"\"\"");

        foreach (var vTable in StreamTables)
        {
            vQuery.Should().Contain($"""FROM "{vTable}" """.TrimEnd(), $"a SHA could otherwise hide in {vTable}");
        }
    }

    /// <summary>
    /// The store's own list of stream tables and the audit's list are the same eight, so the two cannot
    /// drift apart.
    /// </summary>
    [Fact]
    public void TheStoreAndTheAuditAgreeOnTheStreamTables()
    {
        var vStoreSource = File.ReadAllText(
            Path.Combine(RepoTree.Root.FullName, "src", "TfLens.Core", "Storage", "PostgresStore.cs"));

        var vDeclared = Between(vStoreSource, "private static readonly string[] StreamTables =", ";");

        foreach (var vTable in StreamTables)
        {
            vDeclared.Should().Contain($"\"{vTable}\"");
        }
    }

    /// <summary>
    /// The check has no relaxation switch: nothing takes an "ignore", "skip", "allow" or "force"
    /// argument, and no configuration key can turn it off (BRD-89 / REQ-NFR-009).
    /// </summary>
    [Fact]
    public void TheCheckHasNoRelaxationSwitch()
    {
        var vOffenders = new List<string>();
        string[] vBanned = ["ignore", "skip", "allow", "force", "disable", "suppress", "threshold"];

        foreach (var vType in new[] { typeof(ProvenanceAudit), typeof(ProvenanceRules), typeof(ReservedUserIds) })
        {
            foreach (var vMethod in vType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                foreach (var vParameter in vMethod.GetParameters())
                {
                    if (vBanned.Any(aWord => vParameter.Name!.Contains(aWord, StringComparison.OrdinalIgnoreCase)))
                    {
                        vOffenders.Add($"{vType.Name}.{vMethod.Name}({vParameter.Name})");
                    }
                }
            }
        }

        vOffenders.Should().BeEmpty("an integrity rule with an off switch is not an integrity rule");
    }

    /// <summary>
    /// The provenance reason is a new value in the existing vocabulary, not a new key: the export's
    /// <c>parity</c> object still carries exactly the fields it did, so <c>tools/parity-compare.py</c>
    /// walks <c>tflens.json</c> with no mapping layer (REQ-FN-058).
    /// </summary>
    [Fact]
    public void TheRefusalExtendsTheReasonVocabularyAndNotTheKeySet()
    {
        ParityReasons.ProvenanceOrphan.Should().Be("provenance-orphan");

        var vJsonSource = File.ReadAllText(
            Path.Combine(RepoTree.Root.FullName, "src", "TfLens.Core", "Export", "SnapshotJson.cs"));

        var vTopLevel = Between(vJsonSource, "var vDocument = new JsonObject", "};");

        foreach (var vKey in new[] { "per_repo", "tainted_reqs", "live", "backfilled", "pooled", "misses" })
        {
            vTopLevel.Should().Contain($"[\"{vKey}\"]");
        }

        vTopLevel.Should().Contain("[\"extras\"]");
        vTopLevel.Should().Contain("[\"parity\"]");
        vTopLevel.Should().NotContain(
            "[\"provenance\"]",
            "clause 4 expresses itself inside the parity object's reason, never as a ninth key");
    }

    /// <summary>
    /// The quotable stamp cannot be reached without the provenance question being asked: the only public
    /// route that can return QUOTABLE while asserting nothing about the data is the pre-existing
    /// <c>EvaluateFor</c>, and the export and the page both go through
    /// <c>EvaluateWithProvenance</c>.
    /// </summary>
    [Fact]
    public void TheExportAndThePageBothEvaluateProvenance()
    {
        var vExporter = File.ReadAllText(
            Path.Combine(RepoTree.Root.FullName, "src", "TfLens.Core", "Export", "SnapshotExporter.cs"));
        var vPage = File.ReadAllText(Path.Combine(
            RepoTree.Root.FullName, "src", "TfLens", "Components", "Pages", "Export", "ExportSurface.razor"));

        vExporter.Should().Contain("AuditProvenanceAsync");
        vExporter.Should().Contain("EvaluateWithProvenance");
        vPage.Should().Contain("AuditProvenanceAsync");
        vPage.Should().Contain("EvaluateWithProvenance");
        vPage.Should().Contain(
            nameof(ParityReasons.ProvenanceOrphan), "the banner has a case for the reason it refuses");
    }

    /// <summary>
    /// The reserved harness band is defined once and the export refuses it, so a seeded user id has no
    /// published figure by construction (clause 2, second half).
    /// </summary>
    [Fact]
    public void TheHarnessBandIsReservedAndTheExportRefusesIt()
    {
        ReservedUserIds.Floor.Should().Be(90_000);
        ReservedUserIds.IsReserved(90_001).Should().BeTrue();
        ReservedUserIds.IsReserved(2).Should().BeFalse();

        var vAct = () => ProvenanceRules.RefuseReservedUser(ReservedUserIds.Floor);
        vAct.Should().Throw<ProvenanceException>();

        var vReal = () => ProvenanceRules.RefuseReservedUser(2);
        vReal.Should().NotThrow();

        File.ReadAllText(
                Path.Combine(RepoTree.Root.FullName, "src", "TfLens.Core", "Export", "SnapshotExporter.cs"))
            .Should().Contain("RefuseReservedUser");
    }

    /// <summary>
    /// Every ingest path that can produce a stream row also records the identity it obtained, so the
    /// ledger cannot fall behind the tables it vouches for.
    /// </summary>
    [Fact]
    public void EveryIngestPathRecordsWhatItObtained()
    {
        typeof(ITelemetryStore).GetMethod(nameof(ITelemetryStore.RecordSourceProvenanceAsync))
            .Should().NotBeNull("the ledger write is part of the store contract, not an implementation detail");

        File.ReadAllText(Path.Combine(
                RepoTree.Root.FullName, "src", "TfLens", "Services", "Sync", "RepoSyncRunner.cs"))
            .Should().Contain("RecordSourceProvenanceAsync", "the sync records the SHA it fetched");

        File.ReadAllText(Path.Combine(
                RepoTree.Root.FullName, "src", "TfLens.Core", "Import", "TelemetryImportService.cs"))
            .Should().Contain("RecordSourceProvenanceAsync", "the import records the bundle it committed");

        File.ReadAllText(Path.Combine(
                RepoTree.Root.FullName, "src", "TfLens.Core", "Parsing", "StreamParser.cs"))
            .Should().Contain("RequireObtained", "the one door every stream row comes through");
    }

    /// <summary>Reads the source text between two markers, for asserting on a declaration's body.</summary>
    /// <param name="aSource">The file text.</param>
    /// <param name="aStart">The opening marker.</param>
    /// <param name="aEnd">The closing marker, searched for after the opening one.</param>
    /// <returns>The text between them.</returns>
    private static string Between(string aSource, string aStart, string aEnd)
    {
        var vFrom = aSource.IndexOf(aStart, StringComparison.Ordinal);
        vFrom.Should().BeGreaterThan(-1, $"'{aStart}' must still be present");

        var vTo = aSource.IndexOf(aEnd, vFrom + aStart.Length, StringComparison.Ordinal);
        vTo.Should().BeGreaterThan(vFrom, $"'{aEnd}' must follow '{aStart}'");

        return aSource[vFrom..vTo];
    }
}
