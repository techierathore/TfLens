using TfLens.Core.Repos;

namespace TfLens.Core.Import;

/// <summary>
/// The import half of the Add-source dialog: preview a bundle, then commit it (BRD-133, BRD-138).
/// </summary>
/// <remarks>
/// <para>
/// The interface lives here rather than in <c>Abstractions/Interfaces.cs</c> because import is one
/// module with one entry point; splitting the contract from the module it describes would buy nothing.
/// </para>
/// <para>
/// The two methods are deliberately asymmetric in what they touch. <see cref="PreviewAsync"/> writes
/// nothing — no row, no file, not even a temporary one — and is safe to call as often as a user
/// re-picks a file. <see cref="CommitAsync"/> archives the bytes verbatim and then hands them to the
/// <b>same</b> parser, dedupe and store the fetcher uses (REQ-FN-083). There is no second ingest path.
/// </para>
/// </remarks>
public interface ITelemetryImportService
{
    /// <summary>
    /// Dry-runs a bundle and reports what it holds, writing nothing.
    /// </summary>
    /// <remarks>
    /// The extension and the declared length are judged before the body is read; a rollup, a snapshot
    /// or an unsafe archive is refused here, before anything could be archived (REQ-FN-086).
    /// </remarks>
    /// <param name="aUserId">The signed-in user; the preview is theirs and reaches nobody else's data.</param>
    /// <param name="aUpload">The uploaded file.</param>
    /// <param name="aCancellationToken">Cancels the preview.</param>
    /// <returns>What the bundle holds, or why it was refused.</returns>
    Task<ImportPreview> PreviewAsync(
        int aUserId,
        ImportUpload aUpload,
        CancellationToken aCancellationToken = default);

    /// <summary>
    /// Archives a bundle's recognised bytes verbatim, then parses and stores them.
    /// </summary>
    /// <remarks>
    /// Re-running the identical bundle overwrites its own archive file and adds zero rows, because the
    /// streams' natural keys already collapse duplicates (BRD-135).
    /// </remarks>
    /// <param name="aUserId">The signed-in user the rows and the archive belong to.</param>
    /// <param name="aSource">The source the bundle describes; its <c>owner__name</c> folder holds the archive.</param>
    /// <param name="aUpload">The uploaded file.</param>
    /// <param name="aCancellationToken">Cancels the import.</param>
    /// <returns>What was archived, added and collapsed, per stream — or why the bundle was refused.</returns>
    Task<ImportCommitResult> CommitAsync(
        int aUserId,
        RepoRef aSource,
        ImportUpload aUpload,
        CancellationToken aCancellationToken = default);
}
