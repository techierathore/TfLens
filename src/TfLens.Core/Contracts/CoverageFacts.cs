namespace TfLens.Core.Contracts;

/// <summary>
/// Everything the Coverage / health page needs that the sync bookkeeping cannot answer (REQ-UI-014,
/// REQ-UI-015, REQ-UI-016).
/// </summary>
/// <remarks>
/// <para>
/// <c>"SyncState"</c> records how many rows each stream holds, but not <b>when</b> the newest of them
/// happened, and nothing at all about the fields a record carried that SCHEMA.md does not document. Both
/// facts are properties of the stored rows, so they are read from the stream tables directly rather than
/// re-derived from a parse that has already been forgotten: the parser writes them into each row's
/// <c>Overflow</c> jsonb column and its <c>"V"</c> column, and this is the aggregate over those columns.
/// </para>
/// <para>
/// <b>Field names only.</b> <see cref="UnknownFields"/> carries names, never values — the Coverage page
/// must be able to say <i>which</i> fields were observed without ever rendering an <c>Overflow</c>
/// payload (REQ-UI-016). The store filters the documented names out before the list leaves it, so a
/// caller cannot accidentally report <c>ts</c> as undocumented.
/// </para>
/// </remarks>
/// <param name="Streams">One entry per repository and stream that holds at least one row.</param>
/// <param name="UnknownFields">Distinct undocumented field names, per repository and stream.</param>
/// <param name="AboveSchemaV1">Repositories and streams carrying a record whose <c>v</c> is greater than 1.</param>
public sealed record CoverageFacts(
    IReadOnlyList<StreamCoverage> Streams,
    IReadOnlyList<UnknownFieldFact> UnknownFields,
    IReadOnlyList<SchemaVersionFact> AboveSchemaV1)
{
    /// <summary>What a store that cannot answer the question returns — never <c>null</c>.</summary>
    public static CoverageFacts Empty { get; } = new([], [], []);
}

/// <summary>What one repository's one stream holds, as the Coverage stream table renders it.</summary>
/// <param name="Repo"><c>owner/name</c> of the repository.</param>
/// <param name="Stream">The stream's wire name — <c>runs</c>, <c>gates</c>, <c>sessions</c>, <c>commits</c> or <c>events</c>.</param>
/// <param name="Records">Rows stored for the stream.</param>
/// <param name="Backfilled">How many of those rows are backfilled; always zero on a stream with no <c>backfilled</c> column.</param>
/// <param name="NewestTs">ISO-8601 timestamp of the newest row, or <c>null</c> when the stream is empty.</param>
public sealed record StreamCoverage(
    string Repo,
    string Stream,
    int Records,
    int Backfilled,
    string? NewestTs);

/// <summary>One field name SCHEMA.md does not document, as observed in stored rows.</summary>
/// <param name="Repo"><c>owner/name</c> of the repository that carried it.</param>
/// <param name="Stream">The stream's wire name.</param>
/// <param name="Field">The observed field name. A name — never a value.</param>
/// <param name="Records">How many stored rows carried it.</param>
public sealed record UnknownFieldFact(string Repo, string Stream, string Field, int Records);

/// <summary>A repository and stream that carried a record from a newer schema version.</summary>
/// <param name="Repo"><c>owner/name</c> of the repository.</param>
/// <param name="Stream">The stream's wire name.</param>
/// <param name="MaxVersion">The highest <c>v</c> observed.</param>
/// <param name="Records">How many stored rows carry a <c>v</c> greater than 1.</param>
public sealed record SchemaVersionFact(string Repo, string Stream, int MaxVersion, int Records);
