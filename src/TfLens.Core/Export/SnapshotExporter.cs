using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Export;

/// <summary>
/// Writes the weekly snapshot — the diffable numbers plus the parity stamp that says whether they count.
/// </summary>
/// <remarks>
/// <para>
/// REQ-FN-056: one snapshot is <c>snapshot.md</c> (human) and <c>tflens.json</c> (machine) under
/// <c>{DataRoot}/reports/{userId}/{yyyy-MM-dd}/{framework}/</c> — <b>one per user, per date, per
/// framework</b>. The framework segment is why the folder is nested one level deeper than the BRD's
/// shorthand path: ADR-016 makes framework a provenance axis, so a TechieFlow snapshot and a Playbook
/// snapshot taken on the same day are two different documents and cannot share a file name.
/// </para>
/// <para>
/// The exporter <b>composes</b>; it does not compute. Every parity-surface figure comes from
/// <see cref="IMetricsEngine"/> exactly as the pages receive it (ADR-005: the verb and the button share
/// this class, so a parity run exercises the code the pages use), and the no-oracle extras come from
/// <see cref="IExtraMetrics"/>. Nothing here re-derives a number, because a second implementation of a
/// figure is a second thing that can disagree with the reference.
/// </para>
/// <para>
/// Both files are written for the same <see cref="SnapshotInputs"/> and each is written atomically —
/// to a temporary file in the destination folder, then moved over the target — so a reader never sees a
/// half-written snapshot and the pair never describes two different instants.
/// </para>
/// </remarks>
public sealed class SnapshotExporter : ISnapshotExporter
{
    /// <summary>File name of the human-readable half.</summary>
    public const string MarkdownFileName = "snapshot.md";

    /// <summary>File name of the machine-readable half.</summary>
    public const string JsonFileName = "tflens.json";

    /// <summary>The folder-name format; the date is also the folder name (REQ-FN-056).</summary>
    private const string DateFolderFormat = "yyyy-MM-dd";

    /// <summary>
    /// The single bucket the miss records are collapsed into to reproduce the reference's <c>misses</c>
    /// block (REQ-FN-080).
    /// </summary>
    /// <remarks>
    /// It is not a project type and never appears in the export. See
    /// <see cref="MissParityFor(IReadOnlyList{MissRecord}, IReadOnlyList{MissFixRecord}, IReadOnlyList{MissAmendRecord}, IReadOnlyList{RunRecord})"/>
    /// for why the collapse exists at all.
    /// </remarks>
    private const string ParityBucket = "parity";

    private readonly IMetricsEngine objEngine;
    private readonly IExtraMetrics objExtras;
    private readonly IPlaybookReportBuilder objPlaybook;
    private readonly ITelemetryStore objStore;
    private readonly TfLensOptions objOptions;
    private readonly ILogger<SnapshotExporter> objLogger;

    /// <summary>
    /// Creates the exporter.
    /// </summary>
    /// <param name="aEngine">The metrics engine — the parity surface, composed rather than reimplemented.</param>
    /// <param name="aExtras">The metrics with no parity oracle.</param>
    /// <param name="aPlaybook">
    /// The Playbook report builder, read only for a <c>playbook</c> snapshot (REQ-FN-070). It is a
    /// separate interface from <see cref="IMetricsEngine"/> on purpose: the two axes reach this class
    /// through two different doors and are written into two different documents, so a Playbook
    /// <c>phase_gate</c> can never be composed into a TechieFlow snapshot (SCHEMA.md §11).
    /// </param>
    /// <param name="aStore">The store, read for the dataset SHAs of the last sync (REQ-FN-062).</param>
    /// <param name="aOptions">Configuration, for the reports, prices and parity-record paths.</param>
    /// <param name="aLogger">Logger; ids, counts and paths only — never a record body (privacy rule).</param>
    /// <exception cref="ArgumentNullException">A dependency was not supplied.</exception>
    public SnapshotExporter(
        IMetricsEngine aEngine,
        IExtraMetrics aExtras,
        IPlaybookReportBuilder aPlaybook,
        ITelemetryStore aStore,
        IOptions<TfLensOptions> aOptions,
        ILogger<SnapshotExporter> aLogger)
    {
        ArgumentNullException.ThrowIfNull(aEngine);
        ArgumentNullException.ThrowIfNull(aExtras);
        ArgumentNullException.ThrowIfNull(aPlaybook);
        ArgumentNullException.ThrowIfNull(aStore);
        ArgumentNullException.ThrowIfNull(aOptions);
        ArgumentNullException.ThrowIfNull(aLogger);

        objEngine = aEngine;
        objExtras = aExtras;
        objPlaybook = aPlaybook;
        objStore = aStore;
        objOptions = aOptions.Value;
        objLogger = aLogger;
    }

    /// <summary>
    /// Writes <c>snapshot.md</c> and <c>tflens.json</c> for one user, framework and date.
    /// </summary>
    /// <remarks>
    /// The two files are rendered from a single gathered <see cref="SnapshotInputs"/>, so they cannot
    /// disagree, and each is moved into place from a temporary file so neither is ever observed
    /// half-written. Re-exporting the same user, framework and date overwrites in place: a snapshot is
    /// a statement about a date, not an append-only log.
    /// </remarks>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis; one snapshot per framework.</param>
    /// <param name="aDate">The report date, used as the folder name.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>Where the two files were written and what stamp they carry.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="IOException">The reports folder could not be written.</exception>
    public async Task<SnapshotResult> ExportAsync(
        int aUserId,
        string aFramework,
        DateOnly aDate,
        CancellationToken aCancellationToken = default)
    {
        var vInputs = await GatherAsync(aUserId, aFramework, aDate, aCancellationToken).ConfigureAwait(false);
        var vFolder = FolderFor(aUserId, aFramework, aDate);
        Directory.CreateDirectory(vFolder);

        var vMarkdownPath = Path.Combine(vFolder, MarkdownFileName);
        var vJsonPath = Path.Combine(vFolder, JsonFileName);

        var vJson = SnapshotJson.Build(vInputs)
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        await WriteAtomicAsync(vMarkdownPath, SnapshotMarkdown.Build(vInputs), aCancellationToken)
            .ConfigureAwait(false);
        await WriteAtomicAsync(vJsonPath, vJson + Environment.NewLine, aCancellationToken).ConfigureAwait(false);

        objLogger.LogInformation(
            "Snapshot written for user {UserId} framework {Framework} date {Date}: parser {ParserVersion}, "
            + "parity {ParityStatus}, playbook events {PlaybookEvents}",
            aUserId,
            aFramework,
            aDate,
            vInputs.Analysis.ParserVersion,
            vInputs.ParityStatus,
            vInputs.Playbook?.EventsTotal);

        return new SnapshotResult(
            aUserId,
            aFramework,
            aDate,
            Path.GetFullPath(vMarkdownPath),
            Path.GetFullPath(vJsonPath),
            vInputs.Analysis.ParserVersion,
            vInputs.ParityStatus,
            vInputs.DatasetShas);
    }

    /// <summary>
    /// Lists the snapshots already written for a user, newest first.
    /// </summary>
    /// <remarks>
    /// The folder tree is the index — nothing about a snapshot is stored in the database, so a snapshot
    /// copied onto the volume by hand still lists. Each entry's stamp is re-read from its own
    /// <c>tflens.json</c>, so an old snapshot keeps the status it was written with rather than
    /// inheriting today's.
    /// </remarks>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>One entry per snapshot folder, newest date first, then framework in ordinal order.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<IReadOnlyList<SnapshotResult>> ListAsync(
        int aUserId,
        CancellationToken aCancellationToken = default)
    {
        var vRoot = objOptions.ReportsPath(aUserId);
        var vResults = new List<SnapshotResult>();

        if (!Directory.Exists(vRoot))
        {
            return Task.FromResult<IReadOnlyList<SnapshotResult>>(vResults);
        }

        foreach (var vDateFolder in Directory.EnumerateDirectories(vRoot))
        {
            aCancellationToken.ThrowIfCancellationRequested();

            if (!DateOnly.TryParseExact(
                    Path.GetFileName(vDateFolder), DateFolderFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var vDate))
            {
                continue;
            }

            foreach (var vFrameworkFolder in Directory.EnumerateDirectories(vDateFolder))
            {
                var vEntry = ReadEntry(aUserId, vDate, vFrameworkFolder);
                if (vEntry is not null)
                {
                    vResults.Add(vEntry);
                }
            }
        }

        IReadOnlyList<SnapshotResult> vOrdered = vResults
            .OrderByDescending(aR => aR.Date)
            .ThenBy(aR => aR.Framework, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(vOrdered);
    }

    /// <summary>
    /// Gathers everything one snapshot renders from, once.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aFramework">The provenance axis.</param>
    /// <param name="aDate">The report date.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The gathered inputs.</returns>
    private async Task<SnapshotInputs> GatherAsync(
        int aUserId,
        string aFramework,
        DateOnly aDate,
        CancellationToken aCancellationToken)
    {
        var vAnalysis = await objEngine.AnalyseAsync(aUserId, aFramework, aCancellationToken).ConfigureAwait(false);

        // REQ-FN-070: the Playbook-native report set is composed into the playbook snapshot and only
        // into that one. A techieflow snapshot never reads "PbEvent", so the two axes cannot meet in a
        // single document, let alone in a single figure (SCHEMA.md §11, ADR-016).
        var vPlaybook = string.Equals(aFramework, FrameworkNames.Playbook, StringComparison.Ordinal)
            ? await objPlaybook.BuildAsync(aUserId, null, aCancellationToken).ConfigureAwait(false)
            : null;

        var vHarness = await objExtras.CompareHarnessesAsync(aUserId, aFramework, aCancellationToken)
            .ConfigureAwait(false);
        var vRouting = await objExtras.AnalyseRoutingAsync(aUserId, aFramework, aCancellationToken)
            .ConfigureAwait(false);
        var vSyncState = await objStore.ReadSyncStateAsync(aUserId, aCancellationToken).ConfigureAwait(false);

        var vShas = vSyncState
            .Where(aS => !string.IsNullOrWhiteSpace(aS.LastSha))
            .Select(aS => new KeyValuePair<string, string>(aS.Repo, aS.LastSha!))
            .OrderBy(aP => aP.Key, StringComparer.Ordinal)
            .ToList();

        // REQ-FN-080 / REQ-FN-087 — the three per-repository facts the engine's block does not carry, and
        // the miss figures in the shape the reference computes them.
        var vRepos = await objStore.ReadUserReposAsync(aUserId, aCancellationToken).ConfigureAwait(false);
        var vMisses = await objStore.ReadMissesAsync(aUserId, aFramework, null, aCancellationToken)
            .ConfigureAwait(false);
        var vFixes = await objStore.ReadMissFixesAsync(aUserId, aFramework, null, aCancellationToken)
            .ConfigureAwait(false);
        var vAmends = await objStore.ReadMissAmendsAsync(aUserId, aFramework, null, aCancellationToken)
            .ConfigureAwait(false);
        var vGates = await objStore.ReadGatesAsync(aUserId, aFramework, null, aCancellationToken)
            .ConfigureAwait(false);
        var vRuns = await objStore.ReadRunsAsync(aUserId, aFramework, null, aCancellationToken)
            .ConfigureAwait(false);

        var vOrigins = RepoOriginsFor(vAnalysis, vRepos, vMisses, vGates, vRuns);
        var vMissParity = MissParityFor(vMisses, vFixes, vAmends, vRuns);
        var vMeasuredRework = MeasuredReworkFor(vMisses, vFixes, vAmends, vRuns);

        var vParity = ParityRecord.Read(objOptions.ParityLastPath);

        // REQ-FN-063 — the stamp is checked against BOTH invalidators: the parser version and the hash
        // of the reference script the recorded run was compared with.
        var vStamp = ParityRecord.EvaluateFor(
            vParity, vAnalysis.ParserVersion, objOptions.ResolveReferenceScriptPath());

        return new SnapshotInputs(
            aUserId,
            aFramework,
            aDate,
            vAnalysis,
            vPlaybook,
            vHarness,
            vRouting,
            vOrigins,
            vMissParity,
            vMeasuredRework,
            vShas,
            vParity,
            vStamp.Status,
            vStamp.Reason,
            objOptions.PricesPath,
            await RateCard.LoadAsync(objOptions.PricesPath, aCancellationToken).ConfigureAwait(false),
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The per-repository facts the export adds to the engine's own <see cref="PerRepoFacts"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two of the three exist because the reference emits them and an absent key the reference emits is a
    /// parity <c>MISSING</c> finding, which is always closed by implementing it (BRD §13): the repository's
    /// <c>miss</c> record count, and the project types its older records still declare.
    /// </para>
    /// <para>
    /// The third — the source kind — exists because BRD-136 requires origin to be visible in the export.
    /// It is read straight off <c>"UserRepo"</c> and attached <b>here</b>, outside the engine, so that
    /// nothing on the figure path can see it: no engine method takes it, and no figure is keyed by it
    /// (ADR-021, REQ-FN-087).
    /// </para>
    /// </remarks>
    /// <param name="aAnalysis">The engine's output, which fixes the repository order.</param>
    /// <param name="aRepos">The user's connected repositories, carrying the stored source kind.</param>
    /// <param name="aMisses">Every stored miss record for the framework, live and backfilled.</param>
    /// <param name="aGates">Every stored gate record for the framework.</param>
    /// <param name="aRuns">Every stored run record for the framework.</param>
    /// <returns>One entry per repository the analysis reports, in the analysis's own order.</returns>
    private static IReadOnlyList<SnapshotRepoOrigin> RepoOriginsFor(
        AnalysisResult aAnalysis,
        IReadOnlyList<UserRepo> aRepos,
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<GateRecord> aGates,
        IReadOnlyList<RunRecord> aRuns)
    {
        // BRD-132 fixes the wire vocabulary at `api` | `import`. The stored value is canonicalised on the
        // way out rather than echoed, using the same rule SourceKinds.DisplayName degrades by — anything
        // that is not `import` is a fetched source. That keeps a third spelling out of the export even
        // when a row carries one: this deployment's "UserRepo"."SourceKind" column was created with an
        // older DEFAULT of 'Synced' (the badge wording) before database/001-schema.sql was corrected to
        // 'api', so every row predating the column still reads 'Synced' in the database.
        var vKinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var vRepo in aRepos)
        {
            vKinds[vRepo.Repo] = SourceKinds.IsImport(vRepo.SourceKind)
                ? SourceKinds.Import
                : SourceKinds.Api;
        }

        var vOrigins = new List<SnapshotRepoOrigin>();
        foreach (var vFacts in aAnalysis.PerRepo)
        {
            // Records keep the project type they were written with; a reclassified repository therefore
            // occupies two segments that SCHEMA.md §6 forbids pooling. The list says so rather than
            // letting one project quietly be half of two answers.
            var vStale = aGates
                .Where(aGate => string.Equals(aGate.Repo, vFacts.Repo, StringComparison.Ordinal))
                .Select(aGate => aGate.ProjectType)
                .Concat(aRuns
                    .Where(aRun => string.Equals(aRun.Repo, vFacts.Repo, StringComparison.Ordinal))
                    .Select(aRun => aRun.ProjectType))
                .Where(aType => !string.IsNullOrEmpty(aType)
                    && !string.Equals(aType, vFacts.ProjectType, StringComparison.Ordinal))
                .Select(aType => aType!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(aType => aType, StringComparer.Ordinal)
                .ToList();

            vOrigins.Add(new SnapshotRepoOrigin(
                vFacts.Repo,
                vKinds.GetValueOrDefault(vFacts.Repo, SourceKinds.Default),
                aMisses.Count(aMiss => string.Equals(aMiss.Repo, vFacts.Repo, StringComparison.Ordinal)),
                vStale));
        }

        return vOrigins;
    }

    /// <summary>
    /// The miss figures in the shape the reference computes them — one bucket, not one per project type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> <c>tf-metrics.sh</c>'s <c>analyse_misses()</c> deliberately does <i>not</i>
    /// segment the miss stream: its own comment states that "raw counts and the miss-class distribution
    /// ARE poolable: a miss counts as a miss whoever missed it; only its attribution is
    /// confidence-bounded". TfLens's product surface is stricter — REQ-FN-077 segments the block per
    /// <c>project_type</c> and there is no "all types" tab — but BRD-129 requires the export's
    /// <c>misses</c> block to diff against the reference's key for key, and a segmented block cannot.
    /// </para>
    /// <para>
    /// So the parity block is produced by running <b>the engine's own</b>
    /// <see cref="MissFigures.Compute"/> a second time over the same records with the segment key
    /// collapsed, rather than by aggregating segment results here. That distinction matters: aggregating
    /// would be a second implementation of every figure — and a mean cannot be pooled from rounded
    /// per-segment means anyway, because a segment below <see cref="MetricsConstants.MinN"/> carries no
    /// value to pool. Nothing is loosened: <see cref="Segment"/> still has no "all types" bucket, the
    /// engine still returns one block per project type, and this collapsed block is written into exactly
    /// one place — the <c>misses</c> key the compare walks.
    /// </para>
    /// </remarks>
    /// <param name="aMisses">Every stored miss record for the framework.</param>
    /// <param name="aFixes">Every stored fix record for the framework.</param>
    /// <param name="aAmends">Every stored amendment record for the framework.</param>
    /// <param name="aRuns">Every run record for the framework.</param>
    /// <returns>The single collapsed block, or the empty block when the stream holds no live miss.</returns>
    private static MissSegmentFigures MissParityFor(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes,
        IReadOnlyList<MissAmendRecord> aAmends,
        IReadOnlyList<RunRecord> aRuns)
    {
        var vCollapsed = MissFigures.Compute(
            aMisses.Select(aMiss => aMiss with { ProjectType = ParityBucket, ProjectTypeInferred = false }).ToList(),
            aFixes.Select(aFix => aFix with { ProjectType = ParityBucket, ProjectTypeInferred = false }).ToList(),
            aAmends,
            aRuns.Select(aRun => aRun with { ProjectType = ParityBucket, ProjectTypeInferred = false }).ToList());

        return vCollapsed.Live.TryGetValue(ParityBucket, out var vFigures) ? vFigures : EmptyMissFigures();
    }

    /// <summary>
    /// The measuring harness's money row over <c>sole</c> fix records only (REQ-FN-080).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference computes its two measured-dollar keys over <c>[f for f in sole if f.cost_usd is not
    /// None]</c> — the cost attribution bounds them, exactly as it bounds the token columns beside them,
    /// because a dollar figure per miss fixed means nothing if the run it came from repaired three.
    /// <see cref="MissHarnessCost"/> is a per-<i>harness</i> row and carries no such bound, so reading
    /// <c>cost_usd_records</c> straight off it would count an apportioned record as a measured one.
    /// </para>
    /// <para>
    /// The bound is applied the same way the segment collapse is: by feeding
    /// <see cref="MissFigures.Compute"/> the record set the reference feeds it, and reading its answer.
    /// Nothing is recomputed here — the filter is the engine's own
    /// <see cref="MissFigures.SoleAttribution"/> constant, and the mean is the engine's own.
    /// </para>
    /// </remarks>
    /// <param name="aMisses">Every stored miss record for the framework.</param>
    /// <param name="aFixes">Every stored fix record for the framework.</param>
    /// <param name="aAmends">Every stored amendment record for the framework.</param>
    /// <param name="aRuns">Every run record for the framework.</param>
    /// <returns>The measuring harness's row, or <c>null</c> when no <c>sole</c> fix exists.</returns>
    private static MissHarnessCost? MeasuredReworkFor(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes,
        IReadOnlyList<MissAmendRecord> aAmends,
        IReadOnlyList<RunRecord> aRuns)
    {
        var vSole = aFixes
            .Where(aFix => string.Equals(aFix.CostAttribution, MissFigures.SoleAttribution, StringComparison.Ordinal))
            .ToList();

        return MissParityFor(aMisses, vSole, aAmends, aRuns).Cost.ByHarness
            .FirstOrDefault(aRow =>
                string.Equals(aRow.Harness, MissFigures.OpenCodeHarness, StringComparison.Ordinal));
    }

    /// <summary>
    /// The miss block a framework with no live miss reports.
    /// </summary>
    /// <remarks>
    /// The engine returns <i>no segment at all</i> in that case — correctly, because there is no project
    /// type to name — so there is nothing to render and this block is built here. Both shares read
    /// <c>insufficient data (n=0)</c> rather than <c>—</c>, which is the reference's own wording for the
    /// same state and the more informative of the two: nothing was measured because there was nothing to
    /// measure, not because the metric does not apply.
    /// </remarks>
    /// <returns>The zero block.</returns>
    private static MissSegmentFigures EmptyMissFigures() => new()
    {
        Misses = 0,
        MissFixes = 0,
        OrphanFixes = 0,
        OpenMisses = 0,
        WontFix = 0,
        ResolvedMisses = 0,
        ClassDistribution = [],
        ClassDistributionN = 0,
        ClassDistributionNote = Figure.InsufficientData(0).Display(),
        ClassNotRecorded = 0,
        FailedPracticeDistribution = [],
        WhyMissedN = 0,
        WhyMissedEligibility = new FieldEligibility(
            MissAmendFolder.WhyMissedField,
            MetricsConstants.FieldSince.GetValueOrDefault(MissAmendFolder.WhyMissedField),
            0,
            0,
            0),
        FailedPracticeNote = Figure.InsufficientData(0).Display(),
        FoundBy = [],
        FoundByNotRecorded = 0,
        DesignMissShare = Figure.InsufficientData(0),
        EscapeShare = Figure.InsufficientData(0),
        MedianTimeToCloseHours = Figure.InsufficientData(0),
        Attribution = MissAttributionFigures.Empty,
        Cost = MissMoney.Empty
    };

    /// <summary>
    /// Reads one already-written snapshot's stamp back off disk.
    /// </summary>
    /// <param name="aUserId">The AppManager user id.</param>
    /// <param name="aDate">The date the folder names.</param>
    /// <param name="aFolder">The framework folder holding the pair.</param>
    /// <returns>The entry, or <c>null</c> when the folder holds no <c>tflens.json</c>.</returns>
    private static SnapshotResult? ReadEntry(int aUserId, DateOnly aDate, string aFolder)
    {
        var vJsonPath = Path.Combine(aFolder, JsonFileName);
        if (!File.Exists(vJsonPath))
        {
            return null;
        }

        var vFramework = Path.GetFileName(aFolder);
        var vParserVersion = ParserVersion.Current;
        var vStatus = ParityStatuses.NeverRun;
        var vShas = new List<KeyValuePair<string, string>>();

        try
        {
            using var vDocument = JsonDocument.Parse(File.ReadAllText(vJsonPath));
            if (vDocument.RootElement.TryGetProperty("parity", out var vParity))
            {
                vParserVersion = ReadText(vParity, "parser_version") ?? vParserVersion;
                vStatus = ReadText(vParity, "status") ?? vStatus;
                vShas = ReadShas(vParity);
            }
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        return new SnapshotResult(
            aUserId,
            vFramework,
            aDate,
            Path.GetFullPath(Path.Combine(aFolder, MarkdownFileName)),
            Path.GetFullPath(vJsonPath),
            vParserVersion,
            vStatus,
            vShas);
    }

    /// <summary>Reads an optional string from a JSON object.</summary>
    /// <param name="aElement">The object.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static string? ReadText(JsonElement aElement, string aName) =>
        aElement.TryGetProperty(aName, out var vValue) && vValue.ValueKind == JsonValueKind.String
            ? vValue.GetString()
            : null;

    /// <summary>Reads the dataset SHA map out of a snapshot's parity block.</summary>
    /// <param name="aParity">The <c>parity</c> object.</param>
    /// <returns>Repository to SHA pairs.</returns>
    private static List<KeyValuePair<string, string>> ReadShas(JsonElement aParity)
    {
        var vShas = new List<KeyValuePair<string, string>>();

        if (!aParity.TryGetProperty("dataset_shas", out var vNode) || vNode.ValueKind != JsonValueKind.Object)
        {
            return vShas;
        }

        foreach (var vProperty in vNode.EnumerateObject())
        {
            if (vProperty.Value.ValueKind == JsonValueKind.String)
            {
                vShas.Add(new KeyValuePair<string, string>(vProperty.Name, vProperty.Value.GetString()!));
            }
        }

        return vShas;
    }

    /// <summary>
    /// The folder one snapshot lives in.
    /// </summary>
    /// <param name="aUserId">The AppManager user id — the path itself is user-scoped (ADR-013).</param>
    /// <param name="aFramework">The provenance axis.</param>
    /// <param name="aDate">The report date.</param>
    /// <returns>The folder path.</returns>
    private string FolderFor(int aUserId, string aFramework, DateOnly aDate) =>
        Path.Combine(
            objOptions.ReportsPath(aUserId),
            aDate.ToString(DateFolderFormat, CultureInfo.InvariantCulture),
            aFramework);

    /// <summary>
    /// Writes one file so that no reader ever sees it half-written.
    /// </summary>
    /// <remarks>
    /// The temporary file is created in the destination folder so the move is a rename within one
    /// volume, which is atomic; a temp file elsewhere would degrade to a copy and reintroduce the
    /// torn-read the pattern exists to prevent.
    /// </remarks>
    /// <param name="aPath">The destination path.</param>
    /// <param name="aContent">The file's text.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    private static async Task WriteAtomicAsync(string aPath, string aContent, CancellationToken aCancellationToken)
    {
        var vTemporary = aPath + ".tmp";

        await File.WriteAllTextAsync(vTemporary, aContent, aCancellationToken).ConfigureAwait(false);
        File.Move(vTemporary, aPath, true);
    }
}
