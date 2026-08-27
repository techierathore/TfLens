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
            vShas,
            vParity,
            vStamp.Status,
            vStamp.Reason,
            objOptions.PricesPath,
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }

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
