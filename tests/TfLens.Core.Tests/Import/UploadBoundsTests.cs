using TfLens.Core.Import;

namespace TfLens.Core.Tests.Import;

/// <summary>
/// REQ-NFR-014 (BRD-139) — the upload surface is bounded in code. One test per clause.
/// </summary>
public sealed class UploadBoundsTests
{
    /// <summary>The allow-list is exactly the three shapes the frameworks already write.</summary>
    [Fact]
    public void OnlyThreeExtensionsAreAccepted()
    {
        Assert.Equal([".zip", ".jsonl", ".ndjson"], UploadBounds.AllowedExtensions);

        Assert.True(UploadBounds.IsAllowedExtension("metrics.zip"));
        Assert.True(UploadBounds.IsAllowedExtension("runs.jsonl"));
        Assert.True(UploadBounds.IsAllowedExtension("events.ndjson"));
    }

    /// <summary>Everything else is refused on its extension alone, before anything is read.</summary>
    [Theory]
    [InlineData("tflens.json")]
    [InlineData("snapshot.md")]
    [InlineData("payload.tar.gz")]
    [InlineData("script.sh")]
    [InlineData("metrics.zip.exe")]
    [InlineData("noextension")]
    [InlineData("runs.jsonl.bak")]
    public void AnyOtherExtensionIsRefused(string aFileName)
    {
        Assert.False(UploadBounds.IsAllowedExtension(aFileName));

        var vRefusal = UploadBounds.Gate(aFileName, 10);

        Assert.NotNull(vRefusal);
        Assert.Equal(ImportRefusalReason.UnsupportedExtension, vRefusal.Reason);
    }

    /// <summary>The cap is 25 MB and it is judged from the declared length, not from any byte.</summary>
    [Fact]
    public void TwentyFiveMegabytesIsTheCapAndItIsJudgedBeforeTheBody()
    {
        Assert.Equal(25L * 1024 * 1024, UploadBounds.MaxUploadBytes);

        Assert.Null(UploadBounds.Gate("metrics.zip", UploadBounds.MaxUploadBytes));

        var vRefusal = UploadBounds.Gate("metrics.zip", UploadBounds.MaxUploadBytes + 1);

        Assert.NotNull(vRefusal);
        Assert.Equal(ImportRefusalReason.TooLarge, vRefusal.Reason);
    }

    /// <summary>An upload that declares no bytes at all is refused as empty rather than parsed.</summary>
    [Fact]
    public void AnEmptyUploadIsRefused()
    {
        var vRefusal = UploadBounds.Gate("runs.jsonl", 0);

        Assert.NotNull(vRefusal);
        Assert.Equal(ImportRefusalReason.Empty, vRefusal.Reason);
    }

    /// <summary>A bounded read stops at the cap, so a lying declared length buys nothing.</summary>
    [Fact]
    public async Task ABoundedReadRefusesAStreamLongerThanTheCap()
    {
        var vOversized = new MemoryStream(new byte[UploadBounds.MaxUploadBytes + 1024]);

        Assert.Null(await UploadBounds.ReadBoundedAsync(vOversized, CancellationToken.None));
    }

    /// <summary>Absolute paths, drive letters and <c>..</c> segments are refused, never repaired.</summary>
    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../../etc/passwd")]
    [InlineData("docs/../../metrics/runs.jsonl")]
    [InlineData("C:\\Windows\\system32\\cmd.exe")]
    [InlineData("..\\..\\runs.jsonl")]
    public void AnUnsafeEntryNameIsRefused(string aEntryName) =>
        Assert.False(UploadBounds.IsSafeEntryName(aEntryName));

    /// <summary>A plain relative path is safe.</summary>
    [Theory]
    [InlineData("runs.jsonl")]
    [InlineData("docs/metrics/runs.jsonl")]
    [InlineData("metrics/gates.jsonl")]
    public void APlainRelativeEntryNameIsSafe(string aEntryName) =>
        Assert.True(UploadBounds.IsSafeEntryName(aEntryName));

    /// <summary>A zip entry whose Unix mode says symbolic link is recognised as one.</summary>
    [Fact]
    public void ASymbolicLinkIsRecognisedFromTheExternalAttributes()
    {
        // 0xA1FF0000 == S_IFLNK | 0777 in the high sixteen bits, as a Unix zip writes it.
        Assert.True(UploadBounds.IsSymbolicLink(unchecked((int)0xA1FF0000)));

        // 0x81A40000 == S_IFREG | 0644 — an ordinary file.
        Assert.False(UploadBounds.IsSymbolicLink(unchecked((int)0x81A40000)));
        Assert.False(UploadBounds.IsSymbolicLink(0));
    }

    /// <summary>Every write is proven to land inside the root it was given.</summary>
    [Fact]
    public void ConfinementAcceptsOnlyPathsInsideTheRoot()
    {
        var vRoot = Path.Combine(Path.GetTempPath(), "tflens-confine", "raw", "2");

        Assert.True(UploadBounds.TryConfine(vRoot, Path.Combine("owner__name", "runs-abc.jsonl"), out var vInside));
        Assert.StartsWith(Path.GetFullPath(vRoot), vInside, StringComparison.Ordinal);

        Assert.False(UploadBounds.TryConfine(vRoot, Path.Combine("..", "3", "runs-abc.jsonl"), out _));
        Assert.False(UploadBounds.TryConfine(vRoot, "/etc/passwd", out _));
    }

    /// <summary>The archive-bomb ceilings exist and are finite.</summary>
    [Fact]
    public void ArchiveBombCeilingsAreSet()
    {
        Assert.Equal(512, UploadBounds.MaxZipEntries);
        Assert.Equal(100L * 1024 * 1024, UploadBounds.MaxUncompressedBytes);
        Assert.Equal(50L * 1024 * 1024, UploadBounds.MaxEntryUncompressedBytes);
    }
}
