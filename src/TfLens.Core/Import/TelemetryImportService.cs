using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Provenance;
using TfLens.Core.Repos;

namespace TfLens.Core.Import;

/// <summary>
/// Previews and commits an uploaded telemetry bundle (REQ-FN-082, REQ-FN-083, REQ-FN-084, REQ-FN-086).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where this joins the existing pipeline.</b> At the archive, exactly where the GitHub fetcher
/// hands off. Everything downstream — <see cref="IStreamParser"/>, the natural-key dedupes it applies,
/// <see cref="ITelemetryStore.UpsertAsync"/>, the engine, the cache, the export — is the same code an
/// API-fetched source runs through. There is no import-only parse, dedupe or upsert anywhere in this
/// file, which is what makes a parser fix reach imported and fetched data alike (BRD-132, REQ-FN-083).
/// </para>
/// <para>
/// <b>Where it deliberately does not join.</b> A preview extracts <i>into memory</i> and touches the
/// filesystem at no point, so the promise that a preview leaves zero rows and zero files is a property
/// of the code rather than of a cleanup path that might not run (REQ-FN-082).
/// </para>
/// <para>
/// <b>Identity.</b> An uploaded bundle has no commit to name, so its sha256 stands where a fetched
/// source's commit SHA stands — in the archive file name, on the source row, on Coverage and in the
/// dataset a parity run pins (ADR-022). Re-importing identical bytes therefore overwrites its own
/// archive file rather than accumulating copies; a changed bundle lands beside it and replays in order.
/// </para>
/// </remarks>
public sealed class TelemetryImportService : ITelemetryImportService
{
    /// <summary>
    /// The repository name a dry-run parse is attributed to.
    /// </summary>
    /// <remarks>
    /// A preview happens before a source exists, and the parser needs a repository for the dedupe keys.
    /// The value never reaches the store — a preview writes nothing — and is deliberately not a legal
    /// <c>owner/name</c>, so a row carrying it could only have come from a bug.
    /// </remarks>
    public const string PreviewRepo = "(preview)";

    /// <summary>Writes the archive exactly as the bytes arrived, with no byte-order mark of our own.</summary>
    private static readonly UTF8Encoding RawEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The record-list properties of <see cref="ParseResult"/>, cached once.
    /// </summary>
    /// <remarks>
    /// The date range is read over whichever record lists a parse result carries rather than over a
    /// hard-coded five, so a stream added to <see cref="ParseResult"/> tomorrow is covered by the
    /// preview the same day rather than the day somebody remembers to extend a switch here. Nothing is
    /// parsed twice to obtain it: these are the records the shared parser already produced.
    /// </remarks>
    private static readonly PropertyInfo[] RecordListProperties = typeof(ParseResult)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(aP => aP.PropertyType.IsGenericType
                     && typeof(IEnumerable).IsAssignableFrom(aP.PropertyType)
                     && aP.PropertyType != typeof(string))
        .ToArray();

    private readonly IStreamParser objParser;
    private readonly ITelemetryStore objStore;
    private readonly TfLensOptions objOptions;
    private readonly ILogger<TelemetryImportService> objLogger;

    /// <summary>
    /// Creates the import service.
    /// </summary>
    /// <param name="aParser">The one stream parser — the same instance a sync pass uses.</param>
    /// <param name="aStore">The telemetry store, for the one upsert every ingest path shares.</param>
    /// <param name="aOptions">TfLens configuration, read for the data root the archive lives under.</param>
    /// <param name="aLogger">Logger; it records user ids, stream names, counts and hashes only.</param>
    public TelemetryImportService(
        IStreamParser aParser,
        ITelemetryStore aStore,
        IOptions<TfLensOptions> aOptions,
        ILogger<TelemetryImportService> aLogger)
    {
        ArgumentNullException.ThrowIfNull(aOptions);

        objParser = aParser;
        objStore = aStore;
        objOptions = aOptions.Value;
        objLogger = aLogger;
    }

    /// <inheritdoc />
    public async Task<ImportPreview> PreviewAsync(
        int aUserId,
        ImportUpload aUpload,
        CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aUpload);

        var vResolved = await ResolveAsync(aUpload, aCancellationToken).ConfigureAwait(false);

        if (vResolved.Refusal is not null)
        {
            objLogger.LogInformation(
                "Refused an import preview for user {UserId}: {Reason}", aUserId, vResolved.Refusal.Reason);

            return ImportPreview.Refused(vResolved.Refusal);
        }

        var vBundle = vResolved.Bundle!;
        var vStreams = new List<ImportStreamPreview>();
        var vUnknownFields = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var vEntry in Ordered(vBundle.Entries))
        {
            if (!ImportStreamCatalog.TryResolveKind(vEntry.Stream, out var vKind))
            {
                vStreams.Add(new ImportStreamPreview
                {
                    Stream = vEntry.Stream!,
                    EntryName = vEntry.EntryName,
                    Bytes = vEntry.Content.LongLength,
                    IsParseSupported = false
                });

                continue;
            }

            // The shared parser, on text that is never stored. Nothing below this line touches disk.
            var vParsed = objParser.Parse(aUserId, PreviewRepo, vBundle.BundleSha, vKind, Decode(vEntry.Content));
            var vRange = RangeOf(vParsed);

            foreach (var vField in vParsed.UnknownFields)
            {
                vUnknownFields.Add(vField);
            }

            vStreams.Add(new ImportStreamPreview
            {
                Stream = vEntry.Stream!,
                EntryName = vEntry.EntryName,
                Bytes = vEntry.Content.LongLength,
                Records = vParsed.RecordCount,
                DuplicatesCollapsed = vParsed.DuplicatesCollapsed,
                InvalidLines = vParsed.InvalidLines,
                RecordsAboveSchemaV1 = vParsed.RecordsAboveSchemaV1,
                EarliestTs = vRange.Earliest,
                LatestTs = vRange.Latest,
                UnknownFields = vParsed.UnknownFields
            });
        }

        objLogger.LogInformation(
            "Previewed an import for user {UserId}: {Streams} streams, {Records} records, bundle {BundleSha}",
            aUserId,
            vStreams.Count,
            vStreams.Sum(aS => aS.Records),
            vBundle.BundleSha);

        return new ImportPreview
        {
            IsAccepted = true,
            BundleSha = vBundle.BundleSha,
            Framework = vBundle.Framework,
            Streams = vStreams,
            UnrecognisedEntries = vBundle.UnrecognisedEntries,
            EarliestTs = vStreams.Select(aS => aS.EarliestTs).Where(aT => aT is not null).Min(),
            LatestTs = vStreams.Select(aS => aS.LatestTs).Where(aT => aT is not null).Max(),
            UnknownFields = [.. vUnknownFields]
        };
    }

    /// <inheritdoc />
    public async Task<ImportCommitResult> CommitAsync(
        int aUserId,
        RepoRef aSource,
        ImportUpload aUpload,
        CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aSource);
        ArgumentNullException.ThrowIfNull(aUpload);

        var vResolved = await ResolveAsync(aUpload, aCancellationToken).ConfigureAwait(false);

        if (vResolved.Refusal is not null)
        {
            objLogger.LogInformation(
                "Refused an import for user {UserId} source {Source}: {Reason}",
                aUserId,
                aSource.Repo,
                vResolved.Refusal.Reason);

            return ImportCommitResult.Refused(vResolved.Refusal);
        }

        var vBundle = vResolved.Bundle!;
        var vRoot = Path.GetFullPath(objOptions.RawPath(aUserId));
        var vStreams = new List<ImportStreamCommit>();

        // REQ-NFR-019 clause 1 / BRD-143 — an imported source's dataset identity is the bundle's sha256
        // (BRD-134), and it is recorded as obtained BEFORE any row carrying it is written. This is what
        // stops an imported source from being reported as unaccounted merely for having no commit SHA:
        // the audit compares against obtained identities, not against a shape, so the two kinds need no
        // branch and `SourceKind` stays a thing that is displayed and never divided on (ADR-021).
        await objStore
            .RecordSourceProvenanceAsync(
                new SourceProvenanceRecord(
                    aUserId,
                    aSource.Repo,
                    vBundle.BundleSha,
                    ProvenanceKinds.Import,
                    DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                aCancellationToken)
            .ConfigureAwait(false);

        foreach (var vEntry in Ordered(vBundle.Entries))
        {
            if (!ImportStreamCatalog.TryResolveKind(vEntry.Stream, out var vKind))
            {
                objLogger.LogInformation(
                    "Skipped the {Stream} stream of an import for user {UserId}: this build has no parser for it.",
                    vEntry.Stream,
                    aUserId);

                continue;
            }

            var vRelative = Path.Combine(aSource.ArchiveFolder, $"{vEntry.Stream}-{vBundle.BundleSha}.jsonl");

            // REQ-NFR-014 — every written path is proven to resolve inside data/raw/<userId>/.
            if (!UploadBounds.TryConfine(vRoot, vRelative, out var vArchivePath))
            {
                objLogger.LogWarning(
                    "Refused an import for user {UserId}: an archive path resolved outside their raw root.",
                    aUserId);

                return ImportCommitResult.Refused(new ImportRefusal(
                    ImportRefusalReason.UnsafeArchive,
                    SafeZipReader.UnsafeMessage("its contents would be written outside your own archive")));
            }

            // REQ-FN-027 / REQ-FN-083 — the bytes land verbatim BEFORE anything parses them, so a
            // parser exception after this point still leaves an archive `rebuild` can replay.
            Directory.CreateDirectory(Path.GetDirectoryName(vArchivePath)!);
            await File.WriteAllBytesAsync(vArchivePath, vEntry.Content, aCancellationToken).ConfigureAwait(false);

            var vParsed = objParser.Parse(
                aUserId, aSource.Repo, vBundle.BundleSha, vKind, Decode(vEntry.Content));

            var vAdded = await objStore.UpsertAsync(vParsed, aCancellationToken).ConfigureAwait(false);

            vStreams.Add(new ImportStreamCommit(
                vEntry.Stream!,
                vArchivePath,
                vParsed.RecordCount + vParsed.DuplicatesCollapsed,
                vAdded,
                vParsed.DuplicatesCollapsed + Math.Max(0, vParsed.RecordCount - vAdded),
                vParsed.InvalidLines));
        }

        var vImportedTs = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await StampSourceRowAsync(aUserId, aSource, vBundle.BundleSha, vBundle.Framework, vImportedTs, aCancellationToken)
            .ConfigureAwait(false);

        objLogger.LogInformation(
            "Imported bundle {BundleSha} for user {UserId} source {Source}: {Added} rows written across {Streams} streams",
            vBundle.BundleSha,
            aUserId,
            aSource.Repo,
            vStreams.Sum(aS => aS.Added),
            vStreams.Count);

        return new ImportCommitResult
        {
            IsAccepted = true,
            BundleSha = vBundle.BundleSha,
            Framework = vBundle.Framework,
            Streams = vStreams,
            ImportedTs = vImportedTs
        };
    }

    /// <summary>
    /// Records the bundle as the source's dataset identity on its <c>"UserRepo"</c> row (REQ-FN-084,
    /// REQ-FN-085, ADR-022).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs <b>after</b> the archive and the upsert, so a row is never stamped with an identity for
    /// bytes that failed to land. <see cref="UserRepo.LastSha"/> is cleared in the same write because a
    /// dataset has exactly one identity — a row carrying both would give two answers to "which bytes
    /// produced these figures", which is the ambiguity a parity run exists to remove.
    /// </para>
    /// <para>
    /// Re-importing simply overwrites the stamp, which is what makes re-import idempotent at the row
    /// level as well as in the streams (REQ-FN-085). A source that is <b>not yet connected is created
    /// here</b>: an import is how a private or corporate repository becomes a source at all (BRD-131),
    /// so requiring it to be connected first would leave the only path to it running through a
    /// <i>Fetch via API</i> validation that could never succeed.
    /// </para>
    /// <para>
    /// <b>The counts are written here too.</b> They are read back from the stored rows rather than
    /// added up from what this bundle presented, so a re-import of an identical bundle — which
    /// legitimately adds zero records — still leaves the row showing its true totals instead of zero.
    /// </para>
    /// </remarks>
    /// <param name="aUserId">Owner of the source.</param>
    /// <param name="aSource">The source the bundle was imported into.</param>
    /// <param name="aBundleSha">sha256 of the bundle.</param>
    /// <param name="aFramework">Framework the bundle's streams belong to, when it could be told.</param>
    /// <param name="aImportedTs">When the import completed.</param>
    /// <param name="aCancellationToken">Cancels the write.</param>
    private async Task StampSourceRowAsync(
        int aUserId,
        RepoRef aSource,
        string aBundleSha,
        string? aFramework,
        string aImportedTs,
        CancellationToken aCancellationToken)
    {
        var vRepos = await objStore.ReadUserReposAsync(aUserId, aCancellationToken).ConfigureAwait(false);

        var vRow = vRepos.FirstOrDefault(
            aR => string.Equals(aR.Repo, aSource.Repo, StringComparison.OrdinalIgnoreCase));

        var vImportedAt = DateTimeOffset.Parse(aImportedTs, CultureInfo.InvariantCulture);
        var vFramework = string.IsNullOrWhiteSpace(aFramework)
            ? vRow?.Framework ?? FrameworkNames.TechieFlow
            : aFramework;

        var vStamped = vRow is null
            ? new UserRepo
            {
                UserId = aUserId,
                Repo = aSource.Repo,
                Owner = aSource.Owner,
                Name = aSource.Name,
                Branch = string.Empty,
                Kind = vFramework,
                Framework = vFramework,
                // An imported source is not reachable over the API — that is the whole point of the
                // mode. It is never public in the sense BRD-100 means, and nothing polls it.
                IsPublic = false,
                ConnectedTs = aImportedTs,
                SourceKind = SourceKinds.Import,
                BundleSha = aBundleSha,
                LastImportTs = vImportedAt
            }
            : vRow with
            {
                SourceKind = SourceKinds.Import,
                BundleSha = aBundleSha,
                LastImportTs = vImportedAt,
                Framework = vFramework,
                Kind = vFramework
            };

        await objStore.WriteUserRepoAsync(vStamped, aCancellationToken).ConfigureAwait(false);

        // The XOR of REQ-FN-084 spans two tables: BundleSha lives on "UserRepo", LastSha on
        // "SyncState". A repository connected by API and later imported would otherwise keep a commit
        // SHA that no longer describes the bytes on disk — two answers to "which dataset produced
        // these figures". Clearing it is what makes AssertSingleDatasetIdentity true of the pair,
        // and it is safe: the poller no longer visits this source at all (REQ-FN-085), so nothing
        // reads LastSha to decide whether a fetch can be skipped.
        var vStates = await objStore.ReadSyncStateAsync(aUserId, aCancellationToken).ConfigureAwait(false);

        var vState = vStates.FirstOrDefault(
                aS => string.Equals(aS.Repo, aSource.Repo, StringComparison.OrdinalIgnoreCase))
            ?? new SyncState { UserId = aUserId, Repo = aSource.Repo };

        var vFacts = await objStore.ReadCoverageFactsAsync(aUserId, aCancellationToken).ConfigureAwait(false);

        var vMine = vFacts.Streams
            .Where(aF => string.Equals(aF.Repo, aSource.Repo, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await objStore.WriteSyncStateAsync(
                vState with
                {
                    Kind = vFramework,
                    LastSha = null,
                    LastSyncTs = aImportedTs,
                    LastError = null,
                    RunsCount = StoredCount(vMine, StreamNames.Runs),
                    GatesCount = StoredCount(vMine, StreamNames.Gates),
                    SessionsCount = StoredCount(vMine, StreamNames.Sessions),
                    CommitsCount = StoredCount(vMine, StreamNames.Commits),
                    MissesCount = StoredCount(vMine, StreamNames.Misses),
                    EventsCount = StoredCount(vMine, StreamNames.Events)
                },
                aCancellationToken)
            .ConfigureAwait(false);

        ImportedSourceRules.AssertSingleDatasetIdentity(null, vStamped.BundleSha);
    }

    /// <summary>
    /// Reads one stream's stored row count out of a user's coverage facts.
    /// </summary>
    /// <param name="aFacts">The facts for one source.</param>
    /// <param name="aStream">The stream's wire name.</param>
    /// <returns>The stored count, or zero when that stream holds nothing for the source.</returns>
    private static int StoredCount(IReadOnlyList<StreamCoverage> aFacts, string aStream) =>
        aFacts.FirstOrDefault(aF => string.Equals(aF.Stream, aStream, StringComparison.Ordinal))?.Records ?? 0;

    /// <summary>
    /// Computes the sha256 of an uploaded bundle — its dataset identity (ADR-022).
    /// </summary>
    /// <param name="aBytes">The uploaded bytes.</param>
    /// <returns>Lower-case hex, 64 characters.</returns>
    public static string BundleSha256(byte[] aBytes)
    {
        ArgumentNullException.ThrowIfNull(aBytes);

        return Convert.ToHexStringLower(SHA256.HashData(aBytes));
    }

    /// <summary>
    /// Gates, reads, unpacks and recognises an upload — everything both entry points share.
    /// </summary>
    /// <remarks>
    /// The order is the security order and matters: extension and declared size before the body is
    /// read; the bounded read before anything is parsed; the rollup refusal before recognition, so a
    /// zip holding only a <c>tflens.json</c> is told what it actually is rather than that nothing was
    /// recognised (REQ-FN-086).
    /// </remarks>
    /// <param name="aUpload">The uploaded file.</param>
    /// <param name="aCancellationToken">Cancels the read.</param>
    /// <returns>The recognised bundle, or the refusal that stopped it.</returns>
    private static async Task<ResolvedBundle> ResolveAsync(
        ImportUpload aUpload,
        CancellationToken aCancellationToken)
    {
        // REQ-NFR-014 — extension and 25 MB cap, both judged before Content is touched at all.
        var vGate = UploadBounds.Gate(aUpload.FileName, aUpload.DeclaredLength);

        if (vGate is not null)
        {
            return new ResolvedBundle(null, vGate);
        }

        var vBytes = await UploadBounds.ReadBoundedAsync(aUpload.Content, aCancellationToken).ConfigureAwait(false);

        if (vBytes is null)
        {
            return new ResolvedBundle(
                null, new ImportRefusal(ImportRefusalReason.TooLarge, UploadBounds.SizeMessage));
        }

        if (vBytes.Length == 0)
        {
            return new ResolvedBundle(
                null,
                new ImportRefusal(
                    ImportRefusalReason.Empty,
                    "That upload is empty. Pick the telemetry .zip, or one of the .jsonl / .ndjson stream files."));
        }

        var vBundleSha = BundleSha256(vBytes);
        var vIsZip = string.Equals(
            Path.GetExtension(ImportStreamCatalog.FileNameOf(aUpload.FileName)),
            ".zip",
            StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<SafeZipEntry> vEntries;
        IReadOnlyList<string> vUnrecognised;

        if (vIsZip)
        {
            var vZip = SafeZipReader.Read(vBytes);

            if (vZip.Refusal is not null)
            {
                return new ResolvedBundle(null, vZip.Refusal);
            }

            // BRD-140 — before recognition, so a bundle of nothing but computed figures is told so.
            var vRollup = vZip.UnrecognisedEntries.Select(aN => RollupDetector.Detect(aN, null))
                .Concat(vZip.Entries.Select(aE => RollupDetector.Detect(aE.EntryName, aE.Content)))
                .FirstOrDefault(aR => aR is not null);

            if (vRollup is not null)
            {
                return new ResolvedBundle(null, vRollup);
            }

            vEntries = vZip.Entries;
            vUnrecognised = vZip.UnrecognisedEntries;
        }
        else
        {
            var vRollup = RollupDetector.Detect(aUpload.FileName, vBytes);

            if (vRollup is not null)
            {
                return new ResolvedBundle(null, vRollup);
            }

            if (ImportStreamCatalog.TryRecognise(aUpload.FileName, out var vStream))
            {
                vEntries = [new SafeZipEntry(ImportStreamCatalog.FileNameOf(aUpload.FileName), vStream, vBytes)];
                vUnrecognised = [];
            }
            else
            {
                vEntries = [];
                vUnrecognised = [ImportStreamCatalog.FileNameOf(aUpload.FileName)];
            }
        }

        if (vEntries.Count == 0)
        {
            return new ResolvedBundle(null, new ImportRefusal(ImportRefusalReason.NothingRecognised, NothingMessage));
        }

        if (!ImportStreamCatalog.TryResolveFramework(vEntries.Select(aE => aE.Stream!), out var vFramework))
        {
            return new ResolvedBundle(
                null,
                new ImportRefusal(
                    ImportRefusalReason.MixedFrameworks,
                    "That bundle mixes TechieFlow stream files with the Playbook's events.ndjson. They "
                    + "describe two different sources, so upload one directory per source."));
        }

        return new ResolvedBundle(new RecognisedBundle(vBundleSha, vEntries, vUnrecognised, vFramework), null);
    }

    /// <summary>The message a readable but unrecognised bundle is refused with; it names every accepted file.</summary>
    private static string NothingMessage =>
        "Nothing in that upload is a telemetry stream. TfLens reads "
        + string.Join(", ", ImportStreamCatalog.FileNames)
        + " — zip the telemetry directory itself (docs/metrics/ for TechieFlow, verification/telemetry/ "
        + "for the Playbook), or upload one of those files on its own.";

    /// <summary>Orders a bundle's entries the way the catalogue lists streams.</summary>
    /// <param name="aEntries">The recognised entries.</param>
    /// <returns>The entries in preview order.</returns>
    private static IEnumerable<SafeZipEntry> Ordered(IReadOnlyList<SafeZipEntry> aEntries) =>
        aEntries.OrderBy(aE => ImportStreamCatalog.OrderOf(aE.Stream ?? string.Empty))
            .ThenBy(aE => aE.EntryName, StringComparer.Ordinal);

    /// <summary>
    /// Decodes archived bytes into the text the parser reads.
    /// </summary>
    /// <param name="aBytes">The entry's bytes, exactly as they were uploaded.</param>
    /// <returns>The text, with a leading byte-order mark removed.</returns>
    private static string Decode(byte[] aBytes) =>
        RawEncoding.GetString(aBytes).TrimStart('﻿');

    /// <summary>
    /// Reads the earliest and latest record timestamp out of a parse result.
    /// </summary>
    /// <param name="aParsed">What the shared parser produced.</param>
    /// <returns>The range, ISO-8601 UTC, with nulls when the file held no timestamped record.</returns>
    private static (string? Earliest, string? Latest) RangeOf(ParseResult aParsed)
    {
        DateTimeOffset? vEarliest = null;
        DateTimeOffset? vLatest = null;

        foreach (var vProperty in RecordListProperties)
        {
            if (vProperty.GetValue(aParsed) is not IEnumerable vRecords)
            {
                continue;
            }

            foreach (var vRecord in vRecords)
            {
                var vTs = vRecord?.GetType().GetProperty("Ts")?.GetValue(vRecord) as string;

                if (!DateTimeOffset.TryParse(
                        vTs, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var vMoment))
                {
                    continue;
                }

                vEarliest = vEarliest is null || vMoment < vEarliest ? vMoment : vEarliest;
                vLatest = vLatest is null || vMoment > vLatest ? vMoment : vLatest;
            }
        }

        return (Format(vEarliest), Format(vLatest));
    }

    /// <summary>Formats an optional instant the way every stored timestamp in TfLens is formatted.</summary>
    /// <param name="aMoment">The instant, or <c>null</c>.</param>
    /// <returns>The ISO-8601 UTC text, or <c>null</c>.</returns>
    private static string? Format(DateTimeOffset? aMoment) =>
        aMoment?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>A bundle that passed every gate, ready to preview or commit.</summary>
    /// <param name="BundleSha">The sha256 of the uploaded bytes.</param>
    /// <param name="Entries">The recognised stream entries.</param>
    /// <param name="UnrecognisedEntries">Names of everything else the bundle held.</param>
    /// <param name="Framework">The provenance axis the recognised streams belong to.</param>
    private sealed record RecognisedBundle(
        string BundleSha,
        IReadOnlyList<SafeZipEntry> Entries,
        IReadOnlyList<string> UnrecognisedEntries,
        string Framework);

    /// <summary>Either a recognised bundle or the refusal that stopped it; never both, never neither.</summary>
    /// <param name="Bundle">The bundle, when it passed.</param>
    /// <param name="Refusal">The refusal, when it did not.</param>
    private sealed record ResolvedBundle(RecognisedBundle? Bundle, ImportRefusal? Refusal);
}
