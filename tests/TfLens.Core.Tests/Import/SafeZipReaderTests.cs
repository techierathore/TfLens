using System.IO.Compression;
using System.Text;
using TfLens.Core.Import;

namespace TfLens.Core.Tests.Import;

/// <summary>
/// REQ-NFR-014 — zip extraction refuses absolute paths, <c>..</c> segments and symlinks, and caps
/// entry count and total uncompressed size. One test per clause.
/// </summary>
public sealed class SafeZipReaderTests
{
    /// <summary>A normal telemetry zip is read, and only the recognised entries are materialised.</summary>
    [Fact]
    public void ATelemetryZipIsReadAndOnlyStreamsAreMaterialised()
    {
        var vZip = ImportTestSupport.Zip(
            ("docs/metrics/gates.jsonl", ImportTestSupport.GateLines),
            ("docs/metrics/runs.jsonl", ImportTestSupport.RunLinesWithOneInvalid),
            ("docs/metrics/README.md", "not a stream"));

        var vResult = SafeZipReader.Read(vZip);

        Assert.Null(vResult.Refusal);
        Assert.Equal(2, vResult.Entries.Count);
        Assert.Contains("docs/metrics/README.md", vResult.UnrecognisedEntries);
        Assert.Contains(vResult.Entries, aE => aE.Stream == "gates");
        Assert.Contains(vResult.Entries, aE => aE.Stream == "runs");
    }

    /// <summary>An entry that escapes its directory is refused, and nothing is returned.</summary>
    [Theory]
    [InlineData("../../runs.jsonl")]
    [InlineData("/etc/runs.jsonl")]
    public void AnEscapingEntryIsRefused(string aEntryName)
    {
        var vResult = SafeZipReader.Read(WriteRawEntry(aEntryName, ImportTestSupport.GateLines, 0));

        Assert.NotNull(vResult.Refusal);
        Assert.Equal(ImportRefusalReason.UnsafeArchive, vResult.Refusal.Reason);
        Assert.Empty(vResult.Entries);
    }

    /// <summary>A symlink entry is refused on its Unix mode, before its bytes are touched.</summary>
    [Fact]
    public void ASymlinkEntryIsRefused()
    {
        // S_IFLNK | 0777, as a Unix zip writes a symbolic link.
        var vZip = WriteRawEntry("runs.jsonl", "/etc/passwd", unchecked((int)0xA1FF0000));

        var vResult = SafeZipReader.Read(vZip);

        Assert.NotNull(vResult.Refusal);
        Assert.Equal(ImportRefusalReason.UnsafeArchive, vResult.Refusal.Reason);
        Assert.Contains("symbolic link", vResult.Refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>More entries than the cap is refused before any of them is read.</summary>
    [Fact]
    public void TooManyEntriesIsRefused()
    {
        var vEntries = Enumerable.Range(0, UploadBounds.MaxZipEntries + 1)
            .Select(aIndex => ($"file{aIndex}.txt", "x"))
            .ToArray();

        var vResult = SafeZipReader.Read(ImportTestSupport.Zip(vEntries));

        Assert.NotNull(vResult.Refusal);
        Assert.Equal(ImportRefusalReason.UnsafeArchive, vResult.Refusal.Reason);
    }

    /// <summary>
    /// An archive bomb is refused on its expansion, not on its compressed size.
    /// </summary>
    /// <remarks>
    /// The entry below compresses a highly repetitive payload far past
    /// <see cref="UploadBounds.MaxEntryUncompressedBytes"/> into a few hundred kilobytes. The reader
    /// never allocates the expansion, because the read is bounded and stops at the cap.
    /// </remarks>
    [Fact]
    public void AnArchiveBombIsRefusedOnItsExpansion()
    {
        var vLine = new string('a', 1024) + "\n";
        var vRepeats = (int)(UploadBounds.MaxEntryUncompressedBytes / vLine.Length) + 16;
        var vPayload = new StringBuilder(vRepeats * vLine.Length);

        for (var vIndex = 0; vIndex < vRepeats; vIndex++)
        {
            vPayload.Append(vLine);
        }

        var vZip = ImportTestSupport.Zip(("runs.jsonl", vPayload.ToString()));

        Assert.True(vZip.LongLength < UploadBounds.MaxUploadBytes, "The bomb must fit inside the upload cap.");

        var vResult = SafeZipReader.Read(vZip);

        Assert.NotNull(vResult.Refusal);
        Assert.Equal(ImportRefusalReason.UnsafeArchive, vResult.Refusal.Reason);
        Assert.Contains("uncompressed limit", vResult.Refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Something that is not a zip at all is refused rather than throwing.</summary>
    [Fact]
    public void SomethingThatIsNotAZipIsRefused()
    {
        var vResult = SafeZipReader.Read(Encoding.UTF8.GetBytes("this is not a zip"));

        Assert.NotNull(vResult.Refusal);
        Assert.Equal(ImportRefusalReason.UnsafeArchive, vResult.Refusal.Reason);
    }

    /// <summary>
    /// Writes a zip carrying one entry with an exact name and external attributes.
    /// </summary>
    /// <remarks>
    /// <see cref="ZipArchive.CreateEntry(string)"/> sanitises some names and writes no Unix mode, so
    /// the hostile cases are built by setting the fields directly.
    /// </remarks>
    /// <param name="aEntryName">The entry name to store verbatim.</param>
    /// <param name="aText">The entry's content.</param>
    /// <param name="aExternalAttributes">The external attributes to store.</param>
    /// <returns>The archive's bytes.</returns>
    private static byte[] WriteRawEntry(string aEntryName, string aText, int aExternalAttributes)
    {
        using var vBuffer = new MemoryStream();

        using (var vArchive = new ZipArchive(vBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var vEntry = vArchive.CreateEntry(aEntryName);
            vEntry.ExternalAttributes = aExternalAttributes;

            using var vStream = vEntry.Open();
            var vBytes = Encoding.UTF8.GetBytes(aText);
            vStream.Write(vBytes, 0, vBytes.Length);
        }

        return vBuffer.ToArray();
    }
}
