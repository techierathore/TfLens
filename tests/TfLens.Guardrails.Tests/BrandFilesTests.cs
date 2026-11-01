using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-026 (BRD-206, ADR-033) — the approved TfLens brand files sit in
/// <c>src/TfLens/wwwroot/brand/</c> byte for byte, and the two head bitmaps made from the tile
/// measure 32 × 32 and 180 × 180.
/// </summary>
/// <remarks>
/// The expected hashes are read from the P3 BRD's brand table rather than repeated here, so the
/// check is literally "the table's value": an amendment that changes the brand changes the table,
/// and a file that drifts from it fails the build. The two derived PNGs have no fixed hash (a
/// re-encode may differ by a byte), so they are judged by the width and height in their IHDR chunk,
/// which needs no imaging library. <c>tools/make-brand-icons.cs</c> is the only thing that makes them.
/// </remarks>
public sealed class BrandFilesTests
{
    /// <summary>The six files copied unaltered from the owner's brand folder.</summary>
    private static readonly string[] ApprovedFiles =
    [
        "TfLens-appicon.svg", "TfLens-appiconfg.svg", "TfLens-horizontal.svg",
        "TfLens-horizontal.png", "TfLens-wordmark-white.png", "TfLens.png"
    ];

    private static string BrandFolder =>
        Path.Combine(RepoTree.Root.FullName, "src", "TfLens", "wwwroot", "brand");

    private static string BrdPath =>
        Path.Combine(RepoTree.Root.FullName, "docs", "TfLens-P3-BRD.md");

    /// <summary>
    /// Every approved brand file's SHA-256 equals the value in the P3 BRD's brand table.
    /// </summary>
    /// <param name="aFileName">One of the six approved file names.</param>
    [Theory(DisplayName = "REQ-NFR-026 — each copied brand file's SHA-256 equals the BRD table's value")]
    [MemberData(nameof(ApprovedFileNames))]
    public void BrandFileMatchesApprovedHash(string aFileName)
    {
        var vExpected = BrdHashes();
        Assert.True(vExpected.ContainsKey(aFileName), $"The BRD brand table has no SHA-256 row for {aFileName}.");

        var vPath = Path.Combine(BrandFolder, aFileName);
        Assert.True(File.Exists(vPath), $"REQ-NFR-026 — {RepoTree.Relative(vPath)} is missing.");

        var vActual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(vPath)));

        Assert.True(
            vActual == vExpected[aFileName],
            $"REQ-NFR-026 — {aFileName} is not the approved file byte for byte: " +
            $"SHA-256 {vActual}, the BRD table says {vExpected[aFileName]}.");
    }

    /// <summary>The favicon made from the tile measures exactly 32 × 32.</summary>
    [Fact(DisplayName = "REQ-NFR-026 — favicon-32.png measures 32 × 32")]
    public void FaviconIsThirtyTwoSquare()
    {
        Assert.Equal((32, 32), PngSize(Path.Combine(BrandFolder, "favicon-32.png")));
    }

    /// <summary>The home-screen icon made from the tile measures exactly 180 × 180.</summary>
    [Fact(DisplayName = "REQ-NFR-026 — apple-touch-icon.png measures 180 × 180")]
    public void AppleTouchIconIsOneEightySquare()
    {
        Assert.Equal((180, 180), PngSize(Path.Combine(BrandFolder, "apple-touch-icon.png")));
    }

    /// <summary>
    /// The brand folder holds the eight named files and nothing else, so no screen can draw the mark
    /// from an unapproved re-export sitting beside them.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-026 — the brand folder holds only the eight named files")]
    public void BrandFolderHoldsOnlyTheNamedFiles()
    {
        var vExpected = ApprovedFiles.Concat(["favicon-32.png", "apple-touch-icon.png"])
            .OrderBy(aName => aName, StringComparer.Ordinal);
        var vActual = Directory.EnumerateFiles(BrandFolder)
            .Select(Path.GetFileName)
            .OrderBy(aName => aName, StringComparer.Ordinal);

        Assert.Equal(vExpected, vActual);
    }

    /// <summary>The six approved file names, as xUnit theory rows.</summary>
    /// <returns>One row per approved file.</returns>
    public static TheoryData<string> ApprovedFileNames() => new(ApprovedFiles);

    /// <summary>Reads the file → SHA-256 pairs out of the P3 BRD's brand table.</summary>
    /// <returns>The expected lower-case hex hash per file name.</returns>
    private static Dictionary<string, string> BrdHashes()
    {
        var vRow = new Regex(@"^\|\s*`(?<name>[^`]+)`\s*\|.*\|\s*`(?<sha>[0-9a-f]{64})`\s*\|\s*$", RegexOptions.Multiline);

        return vRow.Matches(File.ReadAllText(BrdPath))
            .GroupBy(aMatch => aMatch.Groups["name"].Value)
            .ToDictionary(aGroup => aGroup.Key, aGroup => aGroup.First().Groups["sha"].Value);
    }

    /// <summary>Reads a PNG's width and height from its IHDR chunk.</summary>
    /// <param name="aPath">An absolute path to a PNG file.</param>
    /// <returns>The image's width and height in pixels.</returns>
    /// <exception cref="InvalidDataException">The file is not a PNG.</exception>
    private static (int Width, int Height) PngSize(string aPath)
    {
        var vBytes = File.ReadAllBytes(aPath);
        byte[] vSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        if (vBytes.Length < 24 || !vBytes.AsSpan(0, 8).SequenceEqual(vSignature))
        {
            throw new InvalidDataException($"{RepoTree.Relative(aPath)} is not a PNG.");
        }

        return (BinaryPrimitives.ReadInt32BigEndian(vBytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(vBytes.AsSpan(20, 4)));
    }
}
