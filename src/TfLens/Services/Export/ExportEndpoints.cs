using System.Globalization;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Services.Auth;

namespace TfLens.Services.Export;

/// <summary>
/// The one authenticated endpoint that hands a written snapshot file back to its owner (REQ-UI-032).
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is written to the server's disk under <c>{DataRoot}/reports/{userId}/{date}/{framework}/</c>,
/// which a browser cannot read; the Past-snapshots table therefore links here rather than at a file path.
/// </para>
/// <para>
/// The endpoint takes <b>no</b> user id. The only user it will ever serve is the one the auth cookie
/// names, so there is no parameter a caller could tamper with to reach another user's folder — isolation
/// is the shape of the route, not a check someone remembered to write (ADR-013). The three values that
/// <i>are</i> read from the query are each validated against a closed set (a <c>yyyy-MM-dd</c> date, one
/// of the two framework names, one of the two file names) and the composed path is then re-checked
/// against the caller's own reports root, so neither <c>..</c> nor an absolute path can escape it.
/// </para>
/// </remarks>
public static class ExportEndpoints
{
    /// <summary>Route the Past-snapshots table links at.</summary>
    public const string DownloadRoute = "/api/export/download";

    /// <summary>The folder-name format a snapshot date is written and read as.</summary>
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>The two file names a snapshot consists of; nothing else is servable.</summary>
    private static readonly string[] ServableFiles =
        [SnapshotExporter.MarkdownFileName, SnapshotExporter.JsonFileName];

    /// <summary>
    /// Maps the snapshot download endpoint.
    /// </summary>
    /// <param name="aApp">The web application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aApp"/> was not supplied.</exception>
    public static WebApplication MapExportEndpoints(this WebApplication aApp)
    {
        ArgumentNullException.ThrowIfNull(aApp);

        aApp.MapGet(DownloadRoute, Download).RequireAuthorization();

        return aApp;
    }

    /// <summary>
    /// Builds the link the Past-snapshots table renders for one file of one snapshot.
    /// </summary>
    /// <param name="aDate">The snapshot's date.</param>
    /// <param name="aFramework">The provenance axis the snapshot was taken on.</param>
    /// <param name="aFile">One of <c>snapshot.md</c> or <c>tflens.json</c>.</param>
    /// <returns>A relative URL for an anchor's <c>href</c>.</returns>
    public static string DownloadUrl(DateOnly aDate, string aFramework, string aFile) =>
        string.Concat(
            DownloadRoute,
            "?date=", Uri.EscapeDataString(aDate.ToString(DateFormat, CultureInfo.InvariantCulture)),
            "&framework=", Uri.EscapeDataString(aFramework ?? string.Empty),
            "&file=", Uri.EscapeDataString(aFile ?? string.Empty));

    /// <summary>
    /// Serves one snapshot file belonging to the signed-in user.
    /// </summary>
    /// <remarks>
    /// A file that belongs to somebody else is indistinguishable from a file that does not exist: both
    /// answer 404, so the endpoint cannot be used to discover which dates another account has exported.
    /// </remarks>
    /// <param name="aContext">The request, read only for its query string.</param>
    /// <param name="aCurrentUser">The signed-in user — the only folder root this call can reach.</param>
    /// <param name="aOptions">Configuration, for the data root the reports live under.</param>
    /// <param name="aLogger">Logger; ids and paths only, never a file body (privacy rule).</param>
    /// <returns>The file, or <c>400</c> for a malformed request and <c>404</c> for anything not the caller's.</returns>
    private static IResult Download(
        HttpContext aContext,
        CurrentUser aCurrentUser,
        IOptions<TfLensOptions> aOptions,
        ILogger<TfLensOptions> aLogger)
    {
        var vUserId = aCurrentUser.RequireUserId();
        var vQuery = aContext.Request.Query;

        var vDateText = vQuery["date"].ToString();
        var vFramework = vQuery["framework"].ToString();
        var vFile = vQuery["file"].ToString();

        if (!DateOnly.TryParseExact(
                vDateText, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return Results.BadRequest("Unknown snapshot date.");
        }

        if (!FrameworkNames.All.Contains(vFramework, StringComparer.Ordinal))
        {
            return Results.BadRequest("Unknown framework.");
        }

        if (!ServableFiles.Contains(vFile, StringComparer.Ordinal))
        {
            return Results.BadRequest("Unknown snapshot file.");
        }

        // The root is resolved first and the composed path is checked against it, so even a value that
        // slipped through the closed-set checks above cannot address a byte outside this user's folder.
        var vRoot = Path.GetFullPath(aOptions.Value.ReportsPath(vUserId));
        var vPath = Path.GetFullPath(Path.Combine(vRoot, vDateText, vFramework, vFile));

        if (!vPath.StartsWith(vRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            aLogger.LogWarning(
                "Refused a snapshot download for user {UserId} that resolved outside their reports root.",
                vUserId);

            return Results.NotFound();
        }

        if (!File.Exists(vPath))
        {
            return Results.NotFound();
        }

        var vContentType = string.Equals(vFile, SnapshotExporter.JsonFileName, StringComparison.Ordinal)
            ? "application/json"
            : "text/markdown";

        return Results.File(vPath, vContentType, $"{vDateText}-{vFramework}-{vFile}");
    }
}
