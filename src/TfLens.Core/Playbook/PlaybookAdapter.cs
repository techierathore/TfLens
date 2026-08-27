using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// Fetches, archives, probes, parses and stores an AI-First-Playbook repository's
/// <c>verification/telemetry</c> stream (REQ-FN-065, BRD-73).
/// </summary>
/// <remarks>
/// <para>
/// The order of operations is the point. The bytes are archived <b>before</b> the probe or the parser
/// sees them (REQ-FN-027), so a parser exception leaves a replayable archive behind; the schema probe
/// then runs <b>before</b> the parser, because ADR-010 makes recording the observed field names the
/// adapter's first task and no <c>"PbEvent"</c> column may be fixed ahead of it.
/// </para>
/// <para>
/// Rows land in the <c>"PbEvent"</c> table, which is physically separate from the four TechieFlow stream
/// tables and shares no gate column with them (SCHEMA.md §11, REQ-FN-066). Anything the provisional
/// column set does not cover is preserved in <c>PbEventRecord.Overflow</c> by the parser, so a rebuild
/// after the columns are corrected loses nothing.
/// </para>
/// </remarks>
public sealed class PlaybookAdapter : IPlaybookAdapter
{
    private readonly IGitHubStreamFetcher objFetcher;
    private readonly IStreamParser objParser;
    private readonly ITelemetryStore objStore;
    private readonly TfLensOptions objOptions;
    private readonly ILogger<PlaybookAdapter> objLogger;

    /// <summary>
    /// Creates the adapter.
    /// </summary>
    /// <param name="aFetcher">The read-only GitHub client.</param>
    /// <param name="aParser">The shared stream parser; the adapter adds no parsing of its own.</param>
    /// <param name="aStore">The telemetry store.</param>
    /// <param name="aOptions">Application options, for the raw-archive root.</param>
    /// <param name="aLogger">Logger; IDs, counts and SHAs only, never a record body (Coding Standards).</param>
    public PlaybookAdapter(
        IGitHubStreamFetcher aFetcher,
        IStreamParser aParser,
        ITelemetryStore aStore,
        IOptions<TfLensOptions> aOptions,
        ILogger<PlaybookAdapter> aLogger)
    {
        objFetcher = aFetcher;
        objParser = aParser;
        objStore = aStore;
        objOptions = aOptions.Value;
        objLogger = aLogger;
    }

    /// <inheritdoc />
    public async Task<PlaybookIngestResult> IngestAsync(
        UserRepo aRepo,
        string aSha,
        CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aRepo);
        ArgumentException.ThrowIfNullOrWhiteSpace(aSha);

        if (!PlaybookRouting.UsesAdapter(aRepo))
        {
            throw new ArgumentException(
                $"Repository {aRepo.Repo} routes to {PlaybookRouting.RouteFor(aRepo)} and must not reach the Playbook adapter.",
                nameof(aRepo));
        }

        var vArchivePaths = new List<string>();
        var vFetched = 0;
        var vAbsent = 0;
        var vWritten = 0;
        PlaybookSchemaObservation? vObservation = null;

        foreach (var vFile in PlaybookStreamFiles.Files)
        {
            var vText = await objFetcher
                .FetchFileAsync(aRepo.Owner, aRepo.Name, PlaybookStreamFiles.PathOf(vFile), aSha, aCancellationToken)
                .ConfigureAwait(false);

            if (vText is null)
            {
                vAbsent++;
                objLogger.LogInformation(
                    "Playbook stream {File} absent for {Repo} at {Sha}", vFile, aRepo.Repo, aSha);
                continue;
            }

            vFetched++;
            vArchivePaths.Add(await ArchiveAsync(aRepo, aSha, vFile, vText, aCancellationToken).ConfigureAwait(false));
            vObservation = await ProbeAsync(aRepo, aSha, vFile, vText, aCancellationToken).ConfigureAwait(false);
            vWritten += await StoreAsync(aRepo, aSha, vText, aCancellationToken).ConfigureAwait(false);
        }

        objLogger.LogInformation(
            "Playbook ingest for {Repo} at {Sha}: {Fetched} files, {Absent} absent, {Written} rows",
            aRepo.Repo,
            aSha,
            vFetched,
            vAbsent,
            vWritten);

        return new PlaybookIngestResult(aRepo.Repo, aSha, vFetched, vAbsent, vArchivePaths, vWritten, vObservation);
    }

    /// <summary>
    /// Writes the fetched text verbatim to the raw archive, before anything reads it (REQ-FN-027).
    /// </summary>
    /// <param name="aRepo">The repository the file came from.</param>
    /// <param name="aSha">The SHA the file was fetched at.</param>
    /// <param name="aFile">The stream file name.</param>
    /// <param name="aText">The response body, unmodified.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The path the bytes were written to.</returns>
    private async Task<string> ArchiveAsync(
        UserRepo aRepo,
        string aSha,
        string aFile,
        string aText,
        CancellationToken aCancellationToken)
    {
        var vDirectory = Path.Combine(
            objOptions.RawPath(aRepo.UserId),
            aRepo.Owner + "__" + aRepo.Name);

        Directory.CreateDirectory(vDirectory);

        var vPath = Path.Combine(vDirectory, $"{Path.GetFileNameWithoutExtension(aFile)}-{aSha}.ndjson");
        await File.WriteAllTextAsync(vPath, aText, new UTF8Encoding(false), aCancellationToken).ConfigureAwait(false);
        return vPath;
    }

    /// <summary>
    /// Runs the schema probe and writes its field table beside the archive (REQ-FN-068).
    /// </summary>
    /// <param name="aRepo">The repository the file came from.</param>
    /// <param name="aSha">The SHA the file was fetched at.</param>
    /// <param name="aFile">The stream file name.</param>
    /// <param name="aText">The archived text.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>What the probe observed.</returns>
    private async Task<PlaybookSchemaObservation> ProbeAsync(
        UserRepo aRepo,
        string aSha,
        string aFile,
        string aText,
        CancellationToken aCancellationToken)
    {
        var vObservation = PlaybookSchemaProbe.Observe(aText);

        var vDirectory = Path.Combine(
            objOptions.RawPath(aRepo.UserId),
            aRepo.Owner + "__" + aRepo.Name);

        var vPath = Path.Combine(vDirectory, $"{Path.GetFileNameWithoutExtension(aFile)}-{aSha}.fields.md");
        var vMarkdown = PlaybookSchemaProbe.ToDecisionsMarkdown(vObservation, $"{aRepo.Repo}@{aSha}/{aFile}");
        await File.WriteAllTextAsync(vPath, vMarkdown, new UTF8Encoding(false), aCancellationToken).ConfigureAwait(false);

        objLogger.LogWarning(
            "Playbook schema discovery for {Repo} at {Sha}: observed fields [{Fields}] over {Records} records. "
            + "Record these in DECISIONS.md §Playbook before fixing any PbEvent column or chart (ADR-010).",
            aRepo.Repo,
            aSha,
            string.Join(", ", vObservation.FieldNames),
            vObservation.Records);

        return vObservation;
    }

    /// <summary>
    /// Parses the archived text through the shared parser and upserts the rows.
    /// </summary>
    /// <param name="aRepo">The repository the file came from.</param>
    /// <param name="aSha">The SHA the file was fetched at.</param>
    /// <param name="aText">The archived text.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>Rows newly written.</returns>
    private async Task<int> StoreAsync(
        UserRepo aRepo,
        string aSha,
        string aText,
        CancellationToken aCancellationToken)
    {
        var vParsed = objParser.Parse(aRepo.UserId, aRepo.Repo, aSha, StreamKind.Events, aText);
        return await objStore.UpsertAsync(vParsed, aCancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The files the Playbook adapter reads out of <c>verification/telemetry</c>.
/// </summary>
/// <remarks>
/// Only <see cref="Events"/> is listed. BRD-73 also asks for "the joiner output if committed", but no
/// sample repository has ever been available to TfLens, so the joiner file's name is unknown and
/// guessing one would be exactly the invented-schema failure ADR-010 exists to prevent. Add it here once
/// a real repository shows what it is called; the fetcher treats a 404 as a legitimate absent stream, so
/// adding a name costs nothing when the file is not committed.
/// </remarks>
public static class PlaybookStreamFiles
{
    /// <summary>The Playbook event stream file name.</summary>
    public const string Events = "events.ndjson";

    /// <summary>Every Playbook stream file the adapter fetches, in report order.</summary>
    public static readonly IReadOnlyList<string> Files = [Events];

    /// <summary>
    /// The repository-relative path of one Playbook stream file.
    /// </summary>
    /// <param name="aFile">The file name, e.g. <see cref="Events"/>.</param>
    /// <returns>The path under the Playbook telemetry directory.</returns>
    public static string PathOf(string aFile) =>
        FrameworkNames.TelemetryPath(FrameworkNames.Playbook) + "/" + aFile;
}
