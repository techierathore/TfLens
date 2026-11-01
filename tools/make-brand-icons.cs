#:package SkiaSharp@3.119.0
#:package SkiaSharp.NativeAssets.Linux.NoDependencies@3.119.0
// make-brand-icons.cs — derives the two head bitmaps from the approved tile (REQ-NFR-026, BRD-206).
//
// WHY. BRD-206 allows exactly two files in src/TfLens/wwwroot/brand/ that are not byte-for-byte
// copies of the owner's brand folder: favicon-32.png (32 x 32) and apple-touch-icon.png (180 x 180),
// both made from TfLens.png and from nothing else. This script is the only way they are made, so
// they can be rebuilt the day the brand changes. It never touches the six approved files.
//
// RUN (from the repository root; .NET 10 file-based app):
//   dotnet run tools/make-brand-icons.cs
//
// The guardrail BrandFilesTests pins the result: each approved file's SHA-256 and the two sizes.
using SkiaSharp;

var vBrandFolder = Path.Combine("src", "TfLens", "wwwroot", "brand");
var vSourcePath = Path.Combine(vBrandFolder, "TfLens.png");

using var vSource = SKBitmap.Decode(vSourcePath)
    ?? throw new InvalidOperationException($"Could not decode {vSourcePath}.");

WriteResized(vSource, 32, Path.Combine(vBrandFolder, "favicon-32.png"));
WriteResized(vSource, 180, Path.Combine(vBrandFolder, "apple-touch-icon.png"));

// Scales the square tile to aSize x aSize with a cubic resampler (no crop, no recolour) and writes a PNG.
static void WriteResized(SKBitmap aSource, int aSize, string aTargetPath)
{
    var vInfo = new SKImageInfo(aSize, aSize, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var vResized = aSource.Resize(vInfo, new SKSamplingOptions(SKCubicResampler.Mitchell))
        ?? throw new InvalidOperationException($"Could not resize to {aSize}.");
    using var vImage = SKImage.FromBitmap(vResized);
    using var vData = vImage.Encode(SKEncodedImageFormat.Png, 100);
    using var vStream = File.Create(aTargetPath);
    vData.SaveTo(vStream);
    Console.WriteLine($"wrote {aTargetPath} ({aSize} x {aSize})");
}
