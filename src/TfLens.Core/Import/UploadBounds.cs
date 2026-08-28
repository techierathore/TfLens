namespace TfLens.Core.Import;

/// <summary>
/// The upload surface, bounded in code (REQ-NFR-014, BRD-139).
/// </summary>
/// <remarks>
/// <para>
/// This is the only inbound write path TfLens has, so every bound is a value in this class rather than
/// a check somebody remembered to write at a call site. Each one is pinned by a test.
/// </para>
/// <para>
/// Nothing here reads a file. The extension and the size are judged from the file <i>name</i> and the
/// <i>declared</i> length, which is what makes the cap enforceable before the body arrives.
/// </para>
/// </remarks>
public static class UploadBounds
{
    /// <summary>The only three extensions an upload may carry — the shapes the frameworks already write.</summary>
    public static readonly IReadOnlyList<string> AllowedExtensions = [".zip", ".jsonl", ".ndjson"];

    /// <summary>25 MB, enforced against the declared length before the body is read.</summary>
    public const long MaxUploadBytes = 25L * 1024 * 1024;

    /// <summary>The most entries an archive may hold, recognised or not.</summary>
    public const int MaxZipEntries = 512;

    /// <summary>The most uncompressed bytes an archive may expand to in total — the archive-bomb ceiling.</summary>
    public const long MaxUncompressedBytes = 100L * 1024 * 1024;

    /// <summary>The most uncompressed bytes any single entry may expand to.</summary>
    public const long MaxEntryUncompressedBytes = 50L * 1024 * 1024;

    /// <summary>The zip external-attribute bits that mark a Unix file type.</summary>
    private const int UnixFileTypeMask = 0xF000;

    /// <summary>The Unix file type of a symbolic link.</summary>
    private const int UnixSymbolicLink = 0xA000;

    /// <summary>
    /// Judges an upload by its file name and declared length, before a byte of it is read.
    /// </summary>
    /// <param name="aFileName">The client-supplied file name.</param>
    /// <param name="aDeclaredLength">The length the transport declared.</param>
    /// <returns>A refusal, or <c>null</c> when the upload may be read.</returns>
    public static ImportRefusal? Gate(string? aFileName, long aDeclaredLength)
    {
        if (string.IsNullOrWhiteSpace(aFileName))
        {
            return new ImportRefusal(ImportRefusalReason.UnsupportedExtension, ExtensionMessage);
        }

        if (!IsAllowedExtension(aFileName))
        {
            return new ImportRefusal(ImportRefusalReason.UnsupportedExtension, ExtensionMessage);
        }

        if (aDeclaredLength <= 0)
        {
            return new ImportRefusal(
                ImportRefusalReason.Empty,
                "That upload is empty. Pick the telemetry .zip, or one of the .jsonl / .ndjson stream files.");
        }

        if (aDeclaredLength > MaxUploadBytes)
        {
            return new ImportRefusal(ImportRefusalReason.TooLarge, SizeMessage);
        }

        return null;
    }

    /// <summary>The message an upload with the wrong extension is refused with.</summary>
    public static string ExtensionMessage =>
        "TfLens accepts a .zip of a telemetry directory, or a loose .jsonl / .ndjson stream file — "
        + "nothing else.";

    /// <summary>The message an over-sized upload is refused with.</summary>
    public static string SizeMessage =>
        $"That upload is larger than the {MaxUploadBytes / (1024 * 1024)} MB limit. Zip only the "
        + "telemetry directory — docs/metrics/ for TechieFlow, verification/telemetry/ for the Playbook.";

    /// <summary>
    /// Tests whether a file name carries one of the three allowed extensions.
    /// </summary>
    /// <param name="aFileName">The client-supplied file name.</param>
    /// <returns><c>true</c> when the extension is allowed.</returns>
    public static bool IsAllowedExtension(string? aFileName)
    {
        if (string.IsNullOrWhiteSpace(aFileName))
        {
            return false;
        }

        var vExtension = Path.GetExtension(ImportStreamCatalog.FileNameOf(aFileName));

        return AllowedExtensions.Any(aA => string.Equals(aA, vExtension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tests whether one archive entry name is safe to write under a confined root.
    /// </summary>
    /// <remarks>
    /// Rooted paths, drive-qualified paths, <c>..</c> segments and anything carrying a NUL are all
    /// refused outright rather than sanitised: a name that needed sanitising was hostile, and quietly
    /// repairing it hides that.
    /// </remarks>
    /// <param name="aEntryName">The entry name as the archive stores it.</param>
    /// <returns><c>true</c> when the name is a plain relative path.</returns>
    public static bool IsSafeEntryName(string? aEntryName)
    {
        if (string.IsNullOrWhiteSpace(aEntryName) || aEntryName.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        var vNormalised = aEntryName.Replace('\\', '/');

        if (vNormalised.StartsWith('/') || Path.IsPathRooted(vNormalised) || vNormalised.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return !vNormalised.Split('/').Any(aSegment => string.Equals(aSegment, "..", StringComparison.Ordinal));
    }

    /// <summary>
    /// Tests whether an archive entry's external attributes mark it a symbolic link.
    /// </summary>
    /// <remarks>
    /// A zip records the Unix mode in the high sixteen bits of the external attributes. A symlink whose
    /// target is <c>/etc</c> would otherwise turn a confined extraction into an unconfined one the
    /// moment anything followed it.
    /// </remarks>
    /// <param name="aExternalAttributes">The entry's external attributes.</param>
    /// <returns><c>true</c> when the entry is a symbolic link.</returns>
    public static bool IsSymbolicLink(int aExternalAttributes) =>
        ((aExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymbolicLink;

    /// <summary>
    /// Resolves a relative path under a root and proves the result is still inside it.
    /// </summary>
    /// <remarks>
    /// Every byte TfLens writes from an upload goes through this method. It is the one place the
    /// "inside <c>data/raw/&lt;userId&gt;/</c>" promise is kept (REQ-NFR-014).
    /// </remarks>
    /// <param name="aRoot">The directory the result must resolve inside.</param>
    /// <param name="aRelative">The relative path to place under it.</param>
    /// <param name="aFullPath">The resolved absolute path when the method returns <c>true</c>.</param>
    /// <returns><c>false</c> when the composed path escapes <paramref name="aRoot"/>.</returns>
    public static bool TryConfine(string aRoot, string aRelative, out string aFullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aRoot);

        aFullPath = string.Empty;

        if (!IsSafeEntryName(aRelative))
        {
            return false;
        }

        var vRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(aRoot));
        var vCandidate = Path.GetFullPath(Path.Combine(vRoot, aRelative));

        if (!vCandidate.StartsWith(vRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        aFullPath = vCandidate;
        return true;
    }

    /// <summary>
    /// Reads at most <see cref="MaxUploadBytes"/> from a stream, refusing anything longer.
    /// </summary>
    /// <remarks>
    /// The declared length has already been checked; this catches a transport that declared one length
    /// and sent another, so the cap does not depend on the client telling the truth.
    /// </remarks>
    /// <param name="aStream">The upload body.</param>
    /// <param name="aCancellationToken">Cancels the read.</param>
    /// <returns>The bytes, or <c>null</c> when the stream ran past the cap.</returns>
    public static async Task<byte[]?> ReadBoundedAsync(Stream aStream, CancellationToken aCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aStream);

        using var vBuffer = new MemoryStream();
        var vChunk = new byte[81920];

        while (true)
        {
            var vRead = await aStream.ReadAsync(vChunk, aCancellationToken).ConfigureAwait(false);

            if (vRead == 0)
            {
                break;
            }

            if (vBuffer.Length + vRead > MaxUploadBytes)
            {
                return null;
            }

            vBuffer.Write(vChunk, 0, vRead);
        }

        return vBuffer.ToArray();
    }
}
