using System.IO.Compression;

namespace TfLens.Core.Import;

/// <summary>One entry read out of an uploaded archive, with its bytes exactly as they were stored.</summary>
/// <param name="EntryName">The entry's path inside the archive, as the archive stored it.</param>
/// <param name="Stream">The wire stream name the entry was recognised as, or <c>null</c>.</param>
/// <param name="Content">The uncompressed bytes, or an empty array when the entry was not materialised.</param>
public sealed record SafeZipEntry(string EntryName, string? Stream, byte[] Content);

/// <summary>What reading an archive found, or why it was refused.</summary>
/// <param name="Entries">The recognised stream entries, with their bytes.</param>
/// <param name="UnrecognisedEntries">Every other entry's name, so a preview can say what it found.</param>
/// <param name="Refusal">Why the archive was refused, or <c>null</c>.</param>
public sealed record SafeZipResult(
    IReadOnlyList<SafeZipEntry> Entries,
    IReadOnlyList<string> UnrecognisedEntries,
    ImportRefusal? Refusal);

/// <summary>
/// Reads an uploaded zip in memory, refusing anything that could escape a directory or exhaust a disk
/// (REQ-NFR-014, BRD-139).
/// </summary>
/// <remarks>
/// <para>
/// Extraction is <b>into memory</b>, not onto disk. That is what lets a preview promise it writes
/// nothing (REQ-FN-082) while still reporting exactly what the bundle holds, and it means a hostile
/// archive never touches the filesystem at any point in the refusal path.
/// </para>
/// <para>
/// Only entries whose file name names a stream are materialised. Every other entry is checked for
/// safety and then reported by name alone, so a zip of an entire repository costs its entry list
/// rather than its contents.
/// </para>
/// <para>
/// <c>entry.Length</c> is the archive's own claim about the uncompressed size and is never trusted:
/// the reads below are bounded and stop at the cap, so a 42 KB zip claiming 4 GB is refused rather
/// than allocated.
/// </para>
/// </remarks>
public static class SafeZipReader
{
    /// <summary>The message an unsafe or oversized archive is refused with.</summary>
    /// <param name="aDetail">What specifically was wrong; never echoes an uploaded byte.</param>
    /// <returns>The user-facing sentence.</returns>
    public static string UnsafeMessage(string aDetail) =>
        $"That archive was refused: {aDetail}. Zip the telemetry directory itself — docs/metrics/ for "
        + "TechieFlow, verification/telemetry/ for the Playbook — and upload that.";

    /// <summary>
    /// Reads an archive's recognised stream entries.
    /// </summary>
    /// <param name="aBytes">The uploaded archive's bytes.</param>
    /// <returns>The recognised entries and unrecognised names, or a refusal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aBytes"/> was not supplied.</exception>
    public static SafeZipResult Read(byte[] aBytes)
    {
        ArgumentNullException.ThrowIfNull(aBytes);

        var vEntries = new List<SafeZipEntry>();
        var vUnrecognised = new List<string>();
        var vTotalUncompressed = 0L;

        ZipArchive vArchive;

        try
        {
            vArchive = new ZipArchive(new MemoryStream(aBytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            return Refuse("it is not a readable zip file");
        }

        using (vArchive)
        {
            if (vArchive.Entries.Count > UploadBounds.MaxZipEntries)
            {
                return Refuse($"it holds more than {UploadBounds.MaxZipEntries} entries");
            }

            foreach (var vEntry in vArchive.Entries)
            {
                if (!UploadBounds.IsSafeEntryName(vEntry.FullName))
                {
                    return Refuse("an entry carried an absolute path or a '..' segment");
                }

                if (UploadBounds.IsSymbolicLink(vEntry.ExternalAttributes))
                {
                    return Refuse("an entry is a symbolic link");
                }

                // A directory entry has no bytes and names no stream.
                if (vEntry.FullName.EndsWith('/') || vEntry.FullName.EndsWith('\\'))
                {
                    continue;
                }

                if (!ImportStreamCatalog.TryRecognise(vEntry.FullName, out var vStream))
                {
                    vUnrecognised.Add(vEntry.FullName);
                    continue;
                }

                var vRemaining = Math.Min(
                    UploadBounds.MaxEntryUncompressedBytes,
                    UploadBounds.MaxUncompressedBytes - vTotalUncompressed);

                var vContent = ReadBounded(vEntry, vRemaining);

                if (vContent is null)
                {
                    return Refuse(
                        $"its entries expand past the {UploadBounds.MaxUncompressedBytes / (1024 * 1024)} MB "
                        + "uncompressed limit");
                }

                vTotalUncompressed += vContent.LongLength;
                vEntries.Add(new SafeZipEntry(vEntry.FullName, vStream, vContent));
            }
        }

        return new SafeZipResult(vEntries, vUnrecognised, null);
    }

    /// <summary>
    /// Reads one entry, stopping the moment it passes its allowance.
    /// </summary>
    /// <param name="aEntry">The archive entry.</param>
    /// <param name="aAllowance">How many uncompressed bytes this entry may still spend.</param>
    /// <returns>The bytes, or <c>null</c> when the entry ran past its allowance.</returns>
    private static byte[]? ReadBounded(ZipArchiveEntry aEntry, long aAllowance)
    {
        if (aAllowance <= 0)
        {
            return null;
        }

        using var vSource = aEntry.Open();
        using var vBuffer = new MemoryStream();
        var vChunk = new byte[81920];

        while (true)
        {
            var vRead = vSource.Read(vChunk, 0, vChunk.Length);

            if (vRead == 0)
            {
                break;
            }

            if (vBuffer.Length + vRead > aAllowance)
            {
                return null;
            }

            vBuffer.Write(vChunk, 0, vRead);
        }

        return vBuffer.ToArray();
    }

    /// <summary>Builds the refused result for one detail.</summary>
    /// <param name="aDetail">What was wrong with the archive.</param>
    /// <returns>A refused result holding no entry.</returns>
    private static SafeZipResult Refuse(string aDetail) =>
        new([], [], new ImportRefusal(ImportRefusalReason.UnsafeArchive, UnsafeMessage(aDetail)));
}
