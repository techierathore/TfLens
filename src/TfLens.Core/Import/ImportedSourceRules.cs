namespace TfLens.Core.Import;

using TfLens.Core.Contracts;

/// <summary>
/// The rules that follow from a source having been imported rather than fetched (REQ-FN-084,
/// REQ-FN-085, ADR-021, ADR-022).
/// </summary>
/// <remarks>
/// <para>
/// Origin is a property of <i>delivery</i>, not of the data. It is displayed everywhere it could
/// matter and it divides no figure (ADR-021), so the rules here are deliberately few: which sources
/// the poller visits, and which value stands as a source's dataset identity. Nothing else in TfLens
/// branches on <c>SourceKind</c>.
/// </para>
/// <para>
/// The methods take the <b>values</b> rather than a <c>UserRepo</c> so the invariant can be pinned by
/// a test independently of the row's shape, and so the callers that hold the row need one line each.
/// </para>
/// </remarks>
public static class ImportedSourceRules
{
    /// <summary><c>UserRepo.SourceKind</c> for a source TfLens fetches from GitHub — the default.</summary>
    /// <remarks>
    /// Aliases <see cref="SourceKinds.Api"/> so the vocabulary is defined once. BRD-132 fixes the
    /// stored value as <c>api</c> and the badge wording as <i>Synced</i>; do not conflate them.
    /// </remarks>
    public const string SyncedKind = SourceKinds.Api;

    /// <summary><c>UserRepo.SourceKind</c> for a source whose telemetry a user uploaded.</summary>
    /// <remarks>Aliases <see cref="SourceKinds.Import"/> — stored as <c>import</c>, shown as <i>Imported</i>.</remarks>
    public const string ImportedKind = SourceKinds.Import;

    /// <summary>
    /// Tests whether a stored source kind names an imported source.
    /// </summary>
    /// <remarks>A null or unrecognised value reads as <see cref="SyncedKind"/>, matching the column default.</remarks>
    /// <param name="aSourceKind">The stored <c>SourceKind</c>.</param>
    /// <returns><c>true</c> when the source's data was uploaded.</returns>
    public static bool IsImported(string? aSourceKind) => SourceKinds.IsImport(aSourceKind);

    /// <summary>
    /// Tests whether the poller and the header's Sync may visit a source.
    /// </summary>
    /// <remarks>
    /// REQ-FN-085 — an imported source has no repository TfLens can reach, so a sync would make an
    /// outbound request that could only fail, and would then write an error onto a row that is
    /// perfectly healthy. Its row action is <b>Re-import</b>, and a poller tick must leave its counts,
    /// its <c>LastImportTs</c> and its error state exactly as it found them.
    /// </remarks>
    /// <param name="aSourceKind">The stored <c>SourceKind</c>.</param>
    /// <returns><c>false</c> for an imported source.</returns>
    public static bool CanSync(string? aSourceKind) => !IsImported(aSourceKind);

    /// <summary>The message shown where a fetched source would offer Sync.</summary>
    public const string CannotSyncMessage =
        "This source can't refresh itself — re-import to update.";

    /// <summary>
    /// Tests the <c>LastSha</c>-or-<c>BundleSha</c> invariant (REQ-FN-084, ADR-022).
    /// </summary>
    /// <remarks>
    /// A source has exactly one dataset identity. Carrying both would mean two answers to "which bytes
    /// produced these figures", which is precisely the ambiguity a parity run has to pin down.
    /// </remarks>
    /// <param name="aLastSha">The commit SHA a fetched source was last read at, or <c>null</c>.</param>
    /// <param name="aBundleSha">The sha256 of the bundle an imported source was last given, or <c>null</c>.</param>
    /// <returns><c>true</c> when at most one of the two is set.</returns>
    public static bool HasSingleDatasetIdentity(string? aLastSha, string? aBundleSha) =>
        string.IsNullOrWhiteSpace(aLastSha) || string.IsNullOrWhiteSpace(aBundleSha);

    /// <summary>
    /// Reads a source's dataset identity — the value that stands where a commit SHA stands.
    /// </summary>
    /// <param name="aLastSha">The commit SHA a fetched source was last read at, or <c>null</c>.</param>
    /// <param name="aBundleSha">The sha256 of the bundle an imported source was last given, or <c>null</c>.</param>
    /// <returns>The identity, or <c>null</c> when the source has never been read.</returns>
    /// <exception cref="InvalidOperationException">Both were set, which the invariant forbids.</exception>
    public static string? DatasetIdentity(string? aLastSha, string? aBundleSha)
    {
        AssertSingleDatasetIdentity(aLastSha, aBundleSha);

        if (!string.IsNullOrWhiteSpace(aBundleSha))
        {
            return aBundleSha;
        }

        return string.IsNullOrWhiteSpace(aLastSha) ? null : aLastSha;
    }

    /// <summary>
    /// Enforces the <c>LastSha</c>-or-<c>BundleSha</c> invariant.
    /// </summary>
    /// <param name="aLastSha">The commit SHA a fetched source was last read at, or <c>null</c>.</param>
    /// <param name="aBundleSha">The sha256 of the bundle an imported source was last given, or <c>null</c>.</param>
    /// <exception cref="InvalidOperationException">Both were set.</exception>
    public static void AssertSingleDatasetIdentity(string? aLastSha, string? aBundleSha)
    {
        if (!HasSingleDatasetIdentity(aLastSha, aBundleSha))
        {
            throw new InvalidOperationException(
                "A source carries LastSha or BundleSha, never both (REQ-FN-084, ADR-022): a dataset has "
                + "exactly one identity.");
        }
    }
}
