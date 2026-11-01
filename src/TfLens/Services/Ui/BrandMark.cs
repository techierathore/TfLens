namespace TfLens.Services.Ui;

/// <summary>
/// The served paths of the approved TfLens brand files and the share-card text that names them.
/// </summary>
/// <remarks>
/// BRD-206..BRD-209, ADR-033 — the files live in <c>wwwroot/brand/</c>, copied byte for byte, and are
/// served at the plain, un-fingerprinted <c>/brand/&lt;file&gt;</c> path because a share scraper and a
/// home-screen icon cache an image by its URL. Every screen that draws the mark takes its path from here,
/// so no screen can draw it from another source.
/// </remarks>
public static class BrandMark
{
    /// <summary>The white glyph on its <c>#0284C7</c> tile, square (BRD-208, BRD-209, the SVG site icon).</summary>
    public const string TileSvg = "/brand/TfLens-appicon.svg";

    /// <summary>
    /// The white glyph alone on a transparent ground, square — drawn inside the <c>#0284C7</c> tile box on
    /// the anonymous panel and the sidebar head, the box-plus-glyph structure the mockups draw (BRD-208/209).
    /// </summary>
    public const string GlyphSvg = "/brand/TfLens-appiconfg.svg";

    /// <summary>The tile and the sky-blue wordmark side by side, 976 × 320; the light sidebar wordmark is cut from it.</summary>
    public const string HorizontalSvg = "/brand/TfLens-horizontal.svg";

    /// <summary>The white wordmark for the dark theme (BRD-209).</summary>
    public const string WordmarkWhitePng = "/brand/TfLens-wordmark-white.png";

    /// <summary>The 1024 × 1024 tile bitmap — the Open Graph and Twitter card image (BRD-207).</summary>
    public const string SharePng = "/brand/TfLens.png";

    /// <summary>The 32 × 32 bitmap site icon made from the tile.</summary>
    public const string Favicon32Png = "/brand/favicon-32.png";

    /// <summary>The 180 × 180 home-screen icon made from the tile.</summary>
    public const string AppleTouchIconPng = "/brand/apple-touch-icon.png";

    /// <summary>The share image's edge in pixels; the tile is square.</summary>
    public const int SharePngSize = 1024;

    /// <summary>The default document title and the Open Graph title.</summary>
    public const string Title = "TfLens";

    /// <summary>The Open Graph description — the product's one-line promise, verbatim from BRD-207.</summary>
    public const string Description = "A read-only lens over TechieFlow and AI-First-Playbook telemetry";

    /// <summary>
    /// Builds the absolute URL of the share image from the request that is being answered.
    /// </summary>
    /// <remarks>
    /// BRD-207 — a share card needs an absolute <c>og:image</c>. It is built from the request's own scheme
    /// and host (after <c>UseForwardedHeaders</c> has applied the proxy's <c>X-Forwarded-Proto</c>), never
    /// from a configured constant, so it is right behind the reverse proxy and on localhost alike.
    /// </remarks>
    /// <param name="aScheme">The request scheme, such as <c>https</c>.</param>
    /// <param name="aHost">The request host with its port when it has one, such as <c>localhost:5099</c>.</param>
    /// <param name="aPathBase">The application's path base; empty when it is served at the root.</param>
    /// <returns>The absolute URL of <see cref="SharePng"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when the scheme or the host is blank.</exception>
    public static string ShareImageUrl(string aScheme, string aHost, string? aPathBase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(aHost);

        var vBase = (aPathBase ?? string.Empty).TrimEnd('/');
        return $"{aScheme}://{aHost}{vBase}{SharePng}";
    }
}
