using TfLens.Core.Contracts;

namespace TfLens.Core.Import;

/// <summary>
/// Why an upload was refused. Every value is a structural refusal — none of them has an override
/// (REQ-NFR-014, REQ-FN-086).
/// </summary>
public enum ImportRefusalReason
{
    /// <summary>Nothing was refused.</summary>
    None = 0,

    /// <summary>The file name did not end in <c>.zip</c>, <c>.jsonl</c> or <c>.ndjson</c>.</summary>
    UnsupportedExtension,

    /// <summary>The upload was larger than <see cref="UploadBounds.MaxUploadBytes"/>.</summary>
    TooLarge,

    /// <summary>The archive carried an absolute path, a <c>..</c> segment, a symlink, too many entries or too many bytes.</summary>
    UnsafeArchive,

    /// <summary>The upload held computed figures rather than raw records (BRD-140).</summary>
    PrecomputedRollup,

    /// <summary>The upload held no byte at all.</summary>
    Empty,

    /// <summary>The upload was readable but held no recognised stream file.</summary>
    NothingRecognised,

    /// <summary>The upload mixed TechieFlow and Playbook streams, which belong to two different sources.</summary>
    MixedFrameworks
}

/// <summary>
/// A refusal, carrying the reason and the message the dialog renders verbatim.
/// </summary>
/// <param name="Reason">Which structural rule refused the upload.</param>
/// <param name="Message">
/// A user-facing sentence that names what to do instead. It is plain text and is never rendered as
/// HTML — no uploaded byte ever reaches it (REQ-NFR-014).
/// </param>
public sealed record ImportRefusal(ImportRefusalReason Reason, string Message);

/// <summary>
/// One upload, as the endpoint hands it to the service.
/// </summary>
/// <remarks>
/// <see cref="DeclaredLength"/> is carried separately from <see cref="Content"/> on purpose: the size
/// cap is applied to the declared length <b>before</b> a single byte of the stream is read
/// (REQ-NFR-014). A stream whose declared length lies is caught a second time by the bounded read.
/// </remarks>
public sealed class ImportUpload
{
    /// <summary>The client-supplied file name; only its extension and its base name are ever used.</summary>
    public required string FileName { get; init; }

    /// <summary>The length the transport declared, checked before the body is read.</summary>
    public required long DeclaredLength { get; init; }

    /// <summary>The body. Never opened until the extension and the declared length have passed.</summary>
    public required Stream Content { get; init; }

    /// <summary>
    /// Builds an upload over an in-memory buffer — the shape tests and the CLI use.
    /// </summary>
    /// <param name="aFileName">The file name, including its extension.</param>
    /// <param name="aBytes">The bytes to import.</param>
    /// <returns>An upload whose declared length matches its content exactly.</returns>
    public static ImportUpload FromBytes(string aFileName, byte[] aBytes)
    {
        ArgumentNullException.ThrowIfNull(aBytes);

        return new ImportUpload
        {
            FileName = aFileName,
            DeclaredLength = aBytes.LongLength,
            Content = new MemoryStream(aBytes, writable: false)
        };
    }
}

/// <summary>What one recognised stream file in a bundle turned out to hold, on a dry run.</summary>
public sealed record ImportStreamPreview
{
    /// <summary>The stream's wire name — <c>runs</c>, <c>gates</c>, <c>sessions</c>, <c>commits</c>, <c>misses</c> or <c>events</c>.</summary>
    public required string Stream { get; init; }

    /// <summary>The entry inside the bundle this stream was read from, for the dialog's "what I found" list.</summary>
    public required string EntryName { get; init; }

    /// <summary>Uncompressed bytes of the entry.</summary>
    public required long Bytes { get; init; }

    /// <summary>Records the shared parser produced, after its natural-key dedupe.</summary>
    public int Records { get; init; }

    /// <summary>Records the file presented before the dedupe collapsed any of them.</summary>
    public int DuplicatesCollapsed { get; init; }

    /// <summary>Lines that were not valid JSON. Counted and reported, never fatal (REQ-FN-032).</summary>
    public int InvalidLines { get; init; }

    /// <summary>Records whose schema version was greater than 1.</summary>
    public int RecordsAboveSchemaV1 { get; init; }

    /// <summary>Earliest record timestamp in the file, ISO-8601 UTC, or <c>null</c> when the file held no record.</summary>
    public string? EarliestTs { get; init; }

    /// <summary>Latest record timestamp in the file, ISO-8601 UTC, or <c>null</c> when the file held no record.</summary>
    public string? LatestTs { get; init; }

    /// <summary>Field names SCHEMA.md does not document. Names only — never a value (REQ-NFR-004).</summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = [];

    /// <summary>
    /// False when this build of TfLens has no <see cref="StreamKind"/> for the stream yet.
    /// </summary>
    /// <remarks>
    /// The entry is still recognised and still reported, so the preview tells the truth about what the
    /// bundle holds rather than silently dropping it; it is simply not parsed, and
    /// <see cref="TelemetryImportService.CommitAsync"/> does not archive or store it. The flag turns
    /// itself true the moment the enum gains the member, because the kind is resolved by name.
    /// </remarks>
    public bool IsParseSupported { get; init; } = true;
}

/// <summary>
/// What a dry run found in an uploaded bundle. Producing one writes nothing, anywhere (REQ-FN-082).
/// </summary>
public sealed record ImportPreview
{
    /// <summary>True when the bundle may be committed; false when <see cref="Refusal"/> says why not.</summary>
    public required bool IsAccepted { get; init; }

    /// <summary>Why the bundle was refused, or <c>null</c> when it was accepted.</summary>
    public ImportRefusal? Refusal { get; init; }

    /// <summary>
    /// The sha256 of the uploaded bytes, lower-case hex — the imported source's dataset identity
    /// wherever a fetched source uses its commit SHA (ADR-022). <c>null</c> when the upload was
    /// refused before it could be read.
    /// </summary>
    public string? BundleSha { get; init; }

    /// <summary>The provenance axis the recognised streams belong to, or <c>null</c> when nothing was recognised.</summary>
    public string? Framework { get; init; }

    /// <summary>One entry per recognised stream file, in the catalogue's order.</summary>
    public IReadOnlyList<ImportStreamPreview> Streams { get; init; } = [];

    /// <summary>
    /// Entry names the bundle held that name no stream — reported so an unrecognised bundle says what
    /// it found instead of failing silently.
    /// </summary>
    public IReadOnlyList<string> UnrecognisedEntries { get; init; } = [];

    /// <summary>Records across every recognised stream.</summary>
    public int TotalRecords => Streams.Sum(aS => aS.Records);

    /// <summary>Invalid lines across every recognised stream.</summary>
    public int TotalInvalidLines => Streams.Sum(aS => aS.InvalidLines);

    /// <summary>Earliest record timestamp across every recognised stream, ISO-8601 UTC.</summary>
    public string? EarliestTs { get; init; }

    /// <summary>Latest record timestamp across every recognised stream, ISO-8601 UTC.</summary>
    public string? LatestTs { get; init; }

    /// <summary>Every undocumented field name across every recognised stream, sorted and de-duplicated.</summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = [];

    /// <summary>Builds the refused preview for one reason.</summary>
    /// <param name="aRefusal">Why the bundle was refused.</param>
    /// <returns>A refused preview carrying nothing else.</returns>
    public static ImportPreview Refused(ImportRefusal aRefusal) =>
        new() { IsAccepted = false, Refusal = aRefusal };
}

/// <summary>What one stream contributed when a bundle was committed.</summary>
/// <param name="Stream">The stream's wire name.</param>
/// <param name="ArchivePath">Where the bytes were archived, verbatim, before anything was parsed.</param>
/// <param name="Presented">Records the file presented to the store.</param>
/// <param name="Added">Rows the store newly wrote — zero for an exact re-import (BRD-135).</param>
/// <param name="DuplicatesCollapsed">Records collapsed, within this file and against what was already stored.</param>
/// <param name="InvalidLines">Lines that were not valid JSON.</param>
public sealed record ImportStreamCommit(
    string Stream,
    string ArchivePath,
    int Presented,
    int Added,
    int DuplicatesCollapsed,
    int InvalidLines);

/// <summary>What committing a bundle did.</summary>
public sealed record ImportCommitResult
{
    /// <summary>True when the bundle was archived, parsed and stored.</summary>
    public required bool IsAccepted { get; init; }

    /// <summary>Why the bundle was refused, or <c>null</c> when it was accepted.</summary>
    public ImportRefusal? Refusal { get; init; }

    /// <summary>The bundle's sha256, lower-case hex — its dataset identity (ADR-022).</summary>
    public string? BundleSha { get; init; }

    /// <summary>The provenance axis the streams belong to.</summary>
    public string? Framework { get; init; }

    /// <summary>One entry per stream that was archived and stored.</summary>
    public IReadOnlyList<ImportStreamCommit> Streams { get; init; } = [];

    /// <summary>Rows the store newly wrote across every stream.</summary>
    public int RecordsAdded => Streams.Sum(aS => aS.Added);

    /// <summary>Records collapsed across every stream — what a re-import reports (BRD-135).</summary>
    public int DuplicatesCollapsed => Streams.Sum(aS => aS.DuplicatesCollapsed);

    /// <summary>ISO-8601 UTC instant the import completed — the value <c>UserRepo.LastImportTs</c> carries.</summary>
    public string? ImportedTs { get; init; }

    /// <summary>Builds the refused result for one reason.</summary>
    /// <param name="aRefusal">Why the bundle was refused.</param>
    /// <returns>A refused result that wrote nothing.</returns>
    public static ImportCommitResult Refused(ImportRefusal aRefusal) =>
        new() { IsAccepted = false, Refusal = aRefusal };
}
