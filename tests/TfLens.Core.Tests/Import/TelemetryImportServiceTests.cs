using TfLens.Core.Contracts;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TfLens.Core.Import;
using TfLens.Core.Repos;

namespace TfLens.Core.Tests.Import;

/// <summary>
/// REQ-FN-082, REQ-FN-083, REQ-FN-084, REQ-FN-085, REQ-FN-086 — the import service.
/// </summary>
public sealed class TelemetryImportServiceTests
{
    private static readonly RepoRef Source = new("techierathore", "PrivateApp");

    /// <summary>A preview reports records per stream, the date range, invalid lines and the sha.</summary>
    [Fact]
    public async Task PreviewReportsWhatTheBundleHolds()
    {
        var vRoot = ImportTestSupport.TempRoot("preview-reports");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);

        var vZip = ImportTestSupport.Zip(
            ("docs/metrics/gates.jsonl", ImportTestSupport.GateLines),
            ("docs/metrics/runs.jsonl", ImportTestSupport.RunLinesWithOneInvalid));

        var vPreview = await vSubject.PreviewAsync(
            ImportTestSupport.UserId, ImportUpload.FromBytes("metrics.zip", vZip), CancellationToken.None);

        Assert.True(vPreview.IsAccepted);
        Assert.Null(vPreview.Refusal);
        Assert.Equal("techieflow", vPreview.Framework);
        Assert.Equal(2, vPreview.Streams.Count);

        var vRuns = vPreview.Streams.Single(aS => aS.Stream == "runs");
        var vGates = vPreview.Streams.Single(aS => aS.Stream == "gates");

        Assert.Equal(1, vRuns.Records);
        Assert.Equal(1, vRuns.InvalidLines);
        Assert.Equal(2, vGates.Records);
        Assert.Equal(0, vGates.InvalidLines);

        Assert.Equal(3, vPreview.TotalRecords);
        Assert.Equal(1, vPreview.TotalInvalidLines);
        Assert.Equal("2026-08-01T10:00:00Z", vPreview.EarliestTs);
        Assert.Equal("2026-08-03T11:30:00Z", vPreview.LatestTs);

        // ADR-022 — the bundle's own sha256 is its dataset identity.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(vZip)), vPreview.BundleSha);
        Assert.Equal(64, vPreview.BundleSha!.Length);
    }

    /// <summary>
    /// A preview leaves zero rows in every table and zero files under <c>data/raw/</c>.
    /// </summary>
    /// <remarks>REQ-FN-082's acceptance, stated exactly.</remarks>
    [Fact]
    public async Task PreviewWritesNothingAnywhere()
    {
        var vRoot = ImportTestSupport.TempRoot("preview-writes-nothing");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);

        var vZip = ImportTestSupport.Zip(
            ("docs/metrics/gates.jsonl", ImportTestSupport.GateLines),
            ("docs/metrics/runs.jsonl", ImportTestSupport.RunLinesWithOneInvalid));

        await vSubject.PreviewAsync(
            ImportTestSupport.UserId, ImportUpload.FromBytes("metrics.zip", vZip), CancellationToken.None);

        Assert.Empty(vStore.Upserts);
        Assert.Equal(0, ImportTestSupport.FileCount(vRoot));
        Assert.False(Directory.Exists(Path.Combine(vRoot, "raw")));
    }

    /// <summary>An invalid line is counted and reported, never fatal (the REQ-FN-032 contract).</summary>
    [Fact]
    public async Task InvalidLinesAreCountedAndNeverFatal()
    {
        var vRoot = ImportTestSupport.TempRoot("invalid-lines");
        var vSubject = ImportTestSupport.Subject(vRoot, new RecordingImportStore());

        var vPreview = await vSubject.PreviewAsync(
            ImportTestSupport.UserId,
            ImportUpload.FromBytes("runs.jsonl", Encoding.UTF8.GetBytes(ImportTestSupport.RunLinesWithOneInvalid)),
            CancellationToken.None);

        Assert.True(vPreview.IsAccepted);
        Assert.Equal(1, vPreview.TotalInvalidLines);
        Assert.Equal(1, vPreview.TotalRecords);
    }

    /// <summary>An unrecognised bundle says what it found rather than partially ingesting.</summary>
    [Fact]
    public async Task AnUnrecognisedBundleReportsWhatItFound()
    {
        var vRoot = ImportTestSupport.TempRoot("unrecognised");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);

        var vZip = ImportTestSupport.Zip(("src/Program.cs", "class Program { }"), ("README.md", "hello"));

        var vPreview = await vSubject.PreviewAsync(
            ImportTestSupport.UserId, ImportUpload.FromBytes("repo.zip", vZip), CancellationToken.None);

        Assert.False(vPreview.IsAccepted);
        Assert.Equal(ImportRefusalReason.NothingRecognised, vPreview.Refusal!.Reason);
        Assert.Contains("runs.jsonl", vPreview.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("events.ndjson", vPreview.Refusal.Message, StringComparison.Ordinal);
        Assert.Empty(vStore.Upserts);
        Assert.Equal(0, ImportTestSupport.FileCount(vRoot));
    }

    /// <summary>An empty bundle is reported, not partially ingested.</summary>
    [Fact]
    public async Task AnEmptyBundleIsReported()
    {
        var vRoot = ImportTestSupport.TempRoot("empty");
        var vSubject = ImportTestSupport.Subject(vRoot, new RecordingImportStore());

        var vPreview = await vSubject.PreviewAsync(
            ImportTestSupport.UserId, ImportUpload.FromBytes("runs.jsonl", []), CancellationToken.None);

        Assert.False(vPreview.IsAccepted);
        Assert.Equal(ImportRefusalReason.Empty, vPreview.Refusal!.Reason);
    }

    /// <summary>
    /// The size cap is applied before the body is read — proven with a body that cannot be read.
    /// </summary>
    [Fact]
    public async Task TheSizeCapIsAppliedBeforeTheBodyIsRead()
    {
        var vRoot = ImportTestSupport.TempRoot("cap-before-read");
        var vSubject = ImportTestSupport.Subject(vRoot, new RecordingImportStore());
        var vBody = new UnreadableStream();

        var vPreview = await vSubject.PreviewAsync(
            ImportTestSupport.UserId,
            new ImportUpload
            {
                FileName = "metrics.zip",
                DeclaredLength = UploadBounds.MaxUploadBytes + 1,
                Content = vBody
            },
            CancellationToken.None);

        Assert.False(vPreview.IsAccepted);
        Assert.Equal(ImportRefusalReason.TooLarge, vPreview.Refusal!.Reason);
        Assert.False(vBody.WasRead, "REQ-NFR-014 — the cap must be enforced before the body is read.");
    }

    /// <summary>The extension is judged before the body is read too.</summary>
    [Fact]
    public async Task TheExtensionIsJudgedBeforeTheBodyIsRead()
    {
        var vRoot = ImportTestSupport.TempRoot("extension-before-read");
        var vSubject = ImportTestSupport.Subject(vRoot, new RecordingImportStore());
        var vBody = new UnreadableStream();

        var vPreview = await vSubject.PreviewAsync(
            ImportTestSupport.UserId,
            new ImportUpload { FileName = "payload.tar.gz", DeclaredLength = 1024, Content = vBody },
            CancellationToken.None);

        Assert.False(vPreview.IsAccepted);
        Assert.Equal(ImportRefusalReason.UnsupportedExtension, vPreview.Refusal!.Reason);
        Assert.False(vBody.WasRead);
    }

    /// <summary>
    /// All three rollup shapes are refused at preview, before anything is archived (REQ-FN-086).
    /// </summary>
    [Theory]
    [InlineData("tflens.json")]
    [InlineData("snapshot.md")]
    [InlineData("data/reports/2/2026-08-27/techieflow/tflens.json")]
    public async Task ARollupInsideAZipIsRefusedBeforeAnythingIsArchived(string aEntryName)
    {
        var vRoot = ImportTestSupport.TempRoot("rollup-zip");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);

        var vZip = ImportTestSupport.Zip((aEntryName, "{ \"per_repo\": [], \"pooled\": {} }"));

        var vCommit = await vSubject.CommitAsync(
            ImportTestSupport.UserId, Source, ImportUpload.FromBytes("export.zip", vZip), CancellationToken.None);

        Assert.False(vCommit.IsAccepted);
        Assert.Equal(ImportRefusalReason.PrecomputedRollup, vCommit.Refusal!.Reason);
        Assert.Contains("docs/metrics/", vCommit.Refusal.Message, StringComparison.Ordinal);
        Assert.Empty(vStore.Upserts);
        Assert.Equal(0, ImportTestSupport.FileCount(vRoot));
    }

    /// <summary>A rollup renamed to a stream file name is refused by shape.</summary>
    [Fact]
    public async Task ARenamedRollupIsRefusedByShape()
    {
        var vRoot = ImportTestSupport.TempRoot("rollup-renamed");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);

        const string vRollup = """{ "per_repo": [], "pooled": {}, "live": {}, "backfilled": {} }""";

        var vCommit = await vSubject.CommitAsync(
            ImportTestSupport.UserId,
            Source,
            ImportUpload.FromBytes("runs.jsonl", Encoding.UTF8.GetBytes(vRollup)),
            CancellationToken.None);

        Assert.False(vCommit.IsAccepted);
        Assert.Equal(ImportRefusalReason.PrecomputedRollup, vCommit.Refusal!.Reason);
        Assert.Empty(vStore.Upserts);
        Assert.Equal(0, ImportTestSupport.FileCount(vRoot));
    }

    /// <summary>
    /// A commit archives the bytes verbatim under the shared layout, then parses them.
    /// </summary>
    /// <remarks>REQ-FN-083 — the archive is written before the parse and holds the uploaded bytes.</remarks>
    [Fact]
    public async Task CommitArchivesVerbatimThenParses()
    {
        var vRoot = ImportTestSupport.TempRoot("commit-archives");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);

        var vZip = ImportTestSupport.Zip(("docs/metrics/gates.jsonl", ImportTestSupport.GateLines));

        var vCommit = await vSubject.CommitAsync(
            ImportTestSupport.UserId, Source, ImportUpload.FromBytes("metrics.zip", vZip), CancellationToken.None);

        Assert.True(vCommit.IsAccepted);

        var vExpected = Path.Combine(
            vRoot, "raw", ImportTestSupport.UserId.ToString(), "techierathore__PrivateApp",
            $"gates-{vCommit.BundleSha}.jsonl");

        Assert.True(File.Exists(vExpected), $"Expected the archive at {vExpected}.");
        Assert.Equal(ImportTestSupport.GateLines, await File.ReadAllTextAsync(vExpected, CancellationToken.None));

        // The rows carry the bundle sha where a fetched source carries its commit SHA (ADR-022).
        var vParsed = Assert.Single(vStore.Upserts);
        Assert.Equal(vCommit.BundleSha, vParsed.SourceSha);
        Assert.Equal(Source.Repo, vParsed.Repo);
        Assert.Equal(ImportTestSupport.UserId, vParsed.UserId);
        Assert.Equal(2, vCommit.RecordsAdded);
    }

    /// <summary>
    /// Committing a bundle registers the source itself, so an import is a complete way to add one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BRD-131 makes importing the way a <b>private or corporate</b> repository becomes a source at
    /// all. If the commit only stamped an already-connected row, the sole route to such a repo would
    /// run through a <i>Fetch via API</i> validation that can never succeed for it — the feature would
    /// be unreachable for exactly the users it was built for.
    /// </para>
    /// <para>
    /// The row is created with <c>SourceKind = import</c> (the stored value of BRD-132, not the
    /// <i>Imported</i> badge word), carries the bundle sha as its dataset identity (ADR-022), and is
    /// marked not-public because it is not reachable over the API.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CommitRegistersTheSourceItself()
    {
        var vRoot = ImportTestSupport.TempRoot("commit-registers");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);

        var vZip = ImportTestSupport.Zip(("docs/metrics/gates.jsonl", ImportTestSupport.GateLines));

        var vCommit = await vSubject.CommitAsync(
            ImportTestSupport.UserId, Source, ImportUpload.FromBytes("metrics.zip", vZip), CancellationToken.None);

        Assert.True(vCommit.IsAccepted);

        var vRow = Assert.Single(vStore.UserReposWritten);

        Assert.Equal(Source.Repo, vRow.Repo);
        Assert.Equal(ImportTestSupport.UserId, vRow.UserId);
        Assert.Equal(SourceKinds.Import, vRow.SourceKind);
        Assert.Equal("import", vRow.SourceKind);
        Assert.Equal(vCommit.BundleSha, vRow.BundleSha);
        Assert.NotNull(vRow.LastImportTs);
        Assert.False(vRow.IsPublic);
    }

    /// <summary>
    /// The source's counts come from the stored rows, so a re-import never reports zero records.
    /// </summary>
    /// <remarks>
    /// REQ-FN-085 — re-importing an identical bundle legitimately adds <b>zero</b> rows. Deriving the
    /// row's counts from what the bundle presented would therefore blank a healthy source on its
    /// second import; they are read back from the store instead. <c>LastSha</c> is cleared in the same
    /// write because a dataset has exactly one identity (REQ-FN-084) and the poller no longer visits
    /// this source at all.
    /// </remarks>
    [Fact]
    public async Task ImportedCountsComeFromTheStoreNotTheBundle()
    {
        var vRoot = ImportTestSupport.TempRoot("commit-counts");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);

        var vUpload = ImportUpload.FromBytes(
            "metrics.zip", ImportTestSupport.Zip(("docs/metrics/gates.jsonl", ImportTestSupport.GateLines)));

        await vSubject.CommitAsync(ImportTestSupport.UserId, Source, vUpload, CancellationToken.None);

        var vState = Assert.Single(vStore.SyncStatesWritten);

        Assert.Equal(Source.Repo, vState.Repo);
        Assert.Equal(ImportTestSupport.UserId, vState.UserId);
        Assert.Null(vState.LastSha);
        Assert.Null(vState.LastError);
        Assert.NotNull(vState.LastSyncTs);
    }

    /// <summary>
    /// The archive folder and file name are exactly the layout a fetched source uses.
    /// </summary>
    /// <remarks>
    /// REQ-FN-085 — removal purges an imported source's archive identically to a fetched one, with no
    /// import-only cleanup path. That is only true if the archive lands where the existing purge looks,
    /// which is <c>data/raw/&lt;userId&gt;/&lt;owner&gt;__&lt;name&gt;/</c>.
    /// </remarks>
    [Fact]
    public async Task TheArchiveLandsWhereTheExistingPurgeLooks()
    {
        var vRoot = ImportTestSupport.TempRoot("archive-layout");
        var vSubject = ImportTestSupport.Subject(vRoot, new RecordingImportStore());

        await vSubject.CommitAsync(
            ImportTestSupport.UserId,
            Source,
            ImportUpload.FromBytes("gates.jsonl", Encoding.UTF8.GetBytes(ImportTestSupport.GateLines)),
            CancellationToken.None);

        var vFolder = Path.Combine(
            vRoot, "raw", ImportTestSupport.UserId.ToString(), Source.ArchiveFolder);

        Assert.True(Directory.Exists(vFolder));

        // Every archived file matches {stream}-{sha}.jsonl, which is what RebuildAsync replays.
        foreach (var vFile in Directory.GetFiles(vFolder))
        {
            var vName = Path.GetFileNameWithoutExtension(vFile);

            Assert.Equal(".jsonl", Path.GetExtension(vFile));
            Assert.Contains('-', vName);
        }
    }

    /// <summary>
    /// Re-importing the identical bundle overwrites its own archive file rather than accumulating copies.
    /// </summary>
    /// <remarks>REQ-FN-084 and BRD-135 — the sha is the file name, so identical bytes reuse the file.</remarks>
    [Fact]
    public async Task ReimportingTheSameBundleOverwritesItsOwnArchive()
    {
        var vRoot = ImportTestSupport.TempRoot("reimport");
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(vRoot, vStore);
        var vBytes = Encoding.UTF8.GetBytes(ImportTestSupport.GateLines);

        await vSubject.CommitAsync(
            ImportTestSupport.UserId, Source, ImportUpload.FromBytes("gates.jsonl", vBytes), CancellationToken.None);

        // The second import presents the same records; the store reports none of them as new.
        vStore.InsertedOverride = 0;

        var vSecond = await vSubject.CommitAsync(
            ImportTestSupport.UserId, Source, ImportUpload.FromBytes("gates.jsonl", vBytes), CancellationToken.None);

        var vFolder = Path.Combine(vRoot, "raw", ImportTestSupport.UserId.ToString(), Source.ArchiveFolder);

        Assert.Single(Directory.GetFiles(vFolder));
        Assert.Equal(0, vSecond.RecordsAdded);
        Assert.Equal(2, vSecond.DuplicatesCollapsed);
        Assert.Equal(2, vStore.Upserts.Count);
    }

    /// <summary>A changed bundle lands beside the first rather than replacing it.</summary>
    [Fact]
    public async Task AChangedBundleLandsBesideTheFirst()
    {
        var vRoot = ImportTestSupport.TempRoot("superset");
        var vSubject = ImportTestSupport.Subject(vRoot, new RecordingImportStore());

        await vSubject.CommitAsync(
            ImportTestSupport.UserId,
            Source,
            ImportUpload.FromBytes("gates.jsonl", Encoding.UTF8.GetBytes(ImportTestSupport.GateLines)),
            CancellationToken.None);

        var vSuperset = ImportTestSupport.GateLines
            + "\n"
            + """{"v":1,"ts":"2026-08-05T08:00:00Z","kind":"gate","app":"TfLens","run_id":"r3","req_id":"REQ-FN-085","verdict":"pass","gate":"build"}""";

        await vSubject.CommitAsync(
            ImportTestSupport.UserId,
            Source,
            ImportUpload.FromBytes("gates.jsonl", Encoding.UTF8.GetBytes(vSuperset)),
            CancellationToken.None);

        var vFolder = Path.Combine(vRoot, "raw", ImportTestSupport.UserId.ToString(), Source.ArchiveFolder);

        Assert.Equal(2, Directory.GetFiles(vFolder).Length);
    }

    /// <summary>A bundle mixing the two frameworks' streams is refused rather than pooled.</summary>
    [Fact]
    public async Task MixedFrameworkStreamsAreRefused()
    {
        var vRoot = ImportTestSupport.TempRoot("mixed");
        var vSubject = ImportTestSupport.Subject(vRoot, new RecordingImportStore());

        var vZip = ImportTestSupport.Zip(
            ("docs/metrics/gates.jsonl", ImportTestSupport.GateLines),
            ("verification/telemetry/events.ndjson", """{"kind":"turn","ts":"2026-08-01T10:00:00Z"}"""));

        var vPreview = await vSubject.PreviewAsync(
            ImportTestSupport.UserId, ImportUpload.FromBytes("both.zip", vZip), CancellationToken.None);

        Assert.False(vPreview.IsAccepted);
        Assert.Equal(ImportRefusalReason.MixedFrameworks, vPreview.Refusal!.Reason);
    }

    /// <summary>Every write is confined to the signed-in user's own raw root.</summary>
    [Fact]
    public async Task EveryWriteLandsInsideTheCallersOwnRawRoot()
    {
        var vRoot = ImportTestSupport.TempRoot("confined");
        var vSubject = ImportTestSupport.Subject(vRoot, new RecordingImportStore());

        await vSubject.CommitAsync(
            ImportTestSupport.UserId,
            Source,
            ImportUpload.FromBytes("gates.jsonl", Encoding.UTF8.GetBytes(ImportTestSupport.GateLines)),
            CancellationToken.None);

        var vUserRoot = Path.GetFullPath(
            Path.Combine(vRoot, "raw", ImportTestSupport.UserId.ToString()));

        foreach (var vFile in Directory.GetFiles(vRoot, "*", SearchOption.AllDirectories))
        {
            Assert.StartsWith(vUserRoot + Path.DirectorySeparatorChar, Path.GetFullPath(vFile), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The import path owns no parse, dedupe or upsert of its own (REQ-FN-083).
    /// </summary>
    /// <remarks>
    /// The service is constructed with the shared <c>IStreamParser</c> and <c>ITelemetryStore</c> and
    /// with nothing else that could parse or write, so a second ingest path could not be wired without
    /// changing the constructor — which this test would then fail on.
    /// </remarks>
    [Fact]
    public void TheServiceDependsOnlyOnTheSharedParserAndStore()
    {
        var vConstructor = Assert.Single(typeof(TelemetryImportService).GetConstructors());

        var vTypes = vConstructor.GetParameters().Select(aP => aP.ParameterType.Name).ToArray();

        Assert.Contains("IStreamParser", vTypes);
        Assert.Contains("ITelemetryStore", vTypes);
        Assert.DoesNotContain(vTypes, aT => aT.Contains("Dedupe", StringComparison.Ordinal));
    }

    /// <summary>
    /// No type in the import module re-implements the parser, the dedupe rules or an upsert.
    /// </summary>
    /// <remarks>
    /// A parser fix must reach imported and fetched data alike, which it only does if there is exactly
    /// one parser. This walks the module's own types and fails on a method that looks like a second one.
    /// </remarks>
    [Fact]
    public void NoImportOnlyParseDedupeOrUpsertExists()
    {
        var vSuspects = typeof(TelemetryImportService).Assembly
            .GetTypes()
            .Where(aT => aT.Namespace == "TfLens.Core.Import")
            .SelectMany(aT => aT.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            // Property and event accessors are not code paths; a property called IsParseSupported is
            // a fact the preview reports, not a parser.
            .Where(aM => !aM.IsSpecialName)
            .Where(aM => aM.Name.Contains("Dedupe", StringComparison.OrdinalIgnoreCase)
                         || aM.Name.Contains("Upsert", StringComparison.OrdinalIgnoreCase)
                         || (aM.Name.Contains("Parse", StringComparison.OrdinalIgnoreCase)
                             && !aM.Name.Contains("TryParse", StringComparison.OrdinalIgnoreCase)))
            .Select(aM => $"{aM.DeclaringType!.Name}.{aM.Name}")
            .ToArray();

        Assert.True(
            vSuspects.Length == 0,
            "REQ-FN-083 — the import module must own no parse, dedupe or upsert of its own. Found: "
            + string.Join(", ", vSuspects));
    }
}
