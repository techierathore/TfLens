using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using TfLens.Core.Import;
using TfLens.Core.Repos;
using TfLens.Services.Auth;

namespace TfLens.Services.Import;

/// <summary>
/// The two authenticated endpoints the Add-source dialog's <b>Import metric files</b> mode posts to
/// (REQ-NFR-014, BRD-139).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the app's only inbound write path, and it is a file picker on a page a human is signed
/// into.</b> Both routes require authentication, both validate an antiforgery token, and neither takes
/// a user id — the only account either will ever write to is the one the auth cookie names, so there
/// is no parameter a caller could tamper with to reach somebody else's archive (ADR-013). There is no
/// anonymous variant, no API-key variant and no machine-to-machine variant; adding one would change
/// what TfLens is, so a guardrail test enumerates the routes and fails if one appears.
/// </para>
/// <para>
/// <b>The size cap is applied before the body is read.</b> The endpoint's own
/// <see cref="IHttpMaxRequestBodySizeFeature"/> is lowered first, so Kestrel refuses an oversized body
/// as it arrives rather than after it has been buffered; the declared <c>Content-Length</c> is then
/// checked, and only after both does anything call <c>ReadFormAsync</c>. A request that declares no
/// length at all is still bounded by the feature.
/// </para>
/// <para>
/// <b>Nothing uploaded is executed or rendered.</b> The responses are JSON built from counts, stream
/// names, hashes and TfLens's own refusal sentences. No uploaded byte, file name or field <i>value</i>
/// is echoed back — undocumented field <i>names</i> are, which is what REQ-NFR-004 already permits the
/// Coverage page to show.
/// </para>
/// </remarks>
public static class ImportEndpoints
{
    /// <summary>The route the dialog dry-runs a bundle at. Writes nothing, ever.</summary>
    public const string PreviewRoute = "/api/import/preview";

    /// <summary>The route the dialog commits a previewed bundle at.</summary>
    public const string CommitRoute = "/api/import/commit";

    /// <summary>The multipart field name both routes read the bundle from.</summary>
    public const string FileField = "file";

    /// <summary>The multipart field name <see cref="CommitRoute"/> reads the source's <c>owner/name</c> from.</summary>
    public const string SourceField = "source";

    /// <summary>
    /// Headroom over the 25 MB payload cap for multipart boundaries, headers and the source field.
    /// </summary>
    /// <remarks>
    /// The cap that matters is on the <i>file</i>, and <see cref="UploadBounds.Gate"/> applies it to
    /// the file's own declared length. This slightly larger number is what the transport is allowed to
    /// carry so that a 25 MB file plus its envelope is not refused for being 25 MB and a bit.
    /// </remarks>
    public const long MaxRequestBodyBytes = UploadBounds.MaxUploadBytes + (64 * 1024);

    /// <summary>
    /// Maps the preview and commit endpoints.
    /// </summary>
    /// <param name="aApp">The web application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aApp"/> was not supplied.</exception>
    public static WebApplication MapImportEndpoints(this WebApplication aApp)
    {
        ArgumentNullException.ThrowIfNull(aApp);

        aApp.MapPost(PreviewRoute, PreviewAsync).RequireAuthorization();
        aApp.MapPost(CommitRoute, CommitAsync).RequireAuthorization();

        return aApp;
    }

    /// <summary>
    /// Dry-runs an uploaded bundle for the signed-in user and reports what it holds.
    /// </summary>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAntiforgery">Validates the form token.</param>
    /// <param name="aCurrentUser">The signed-in user — the only account this call can describe.</param>
    /// <param name="aImport">The import service.</param>
    /// <param name="aCancellationToken">Cancels the preview.</param>
    /// <returns>The preview as JSON, or a refusal with the status the reason maps to.</returns>
    private static async Task<IResult> PreviewAsync(
        HttpContext aHttpContext,
        IAntiforgery aAntiforgery,
        CurrentUser aCurrentUser,
        ITelemetryImportService aImport,
        CancellationToken aCancellationToken)
    {
        var vUserId = aCurrentUser.RequireUserId();
        var vRead = await ReadUploadAsync(aHttpContext, aAntiforgery, aCancellationToken).ConfigureAwait(false);

        if (vRead.Failure is not null)
        {
            return vRead.Failure;
        }

        var vPreview = await aImport.PreviewAsync(vUserId, vRead.Upload!, aCancellationToken).ConfigureAwait(false);

        return vPreview.IsAccepted
            ? Results.Ok(PreviewBody(vPreview))
            : Refused(vPreview.Refusal!);
    }

    /// <summary>
    /// Archives, parses and stores an uploaded bundle for the signed-in user.
    /// </summary>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAntiforgery">Validates the form token.</param>
    /// <param name="aCurrentUser">The signed-in user — the only archive root this call can write into.</param>
    /// <param name="aImport">The import service.</param>
    /// <param name="aCancellationToken">Cancels the import.</param>
    /// <returns>What was written as JSON, or a refusal with the status the reason maps to.</returns>
    private static async Task<IResult> CommitAsync(
        HttpContext aHttpContext,
        IAntiforgery aAntiforgery,
        CurrentUser aCurrentUser,
        ITelemetryImportService aImport,
        CancellationToken aCancellationToken)
    {
        var vUserId = aCurrentUser.RequireUserId();
        var vRead = await ReadUploadAsync(aHttpContext, aAntiforgery, aCancellationToken).ConfigureAwait(false);

        if (vRead.Failure is not null)
        {
            return vRead.Failure;
        }

        var vSource = aHttpContext.Request.Form[SourceField].ToString();

        if (!RepoInputParser.TryParse(vSource, out var vRef, out var vSourceError))
        {
            return Results.BadRequest(new { accepted = false, reason = "InvalidSource", message = vSourceError });
        }

        var vResult = await aImport
            .CommitAsync(vUserId, vRef!, vRead.Upload!, aCancellationToken)
            .ConfigureAwait(false);

        return vResult.IsAccepted
            ? Results.Ok(CommitBody(vResult))
            : Refused(vResult.Refusal!);
    }

    /// <summary>
    /// Bounds the request, validates the token and takes the one file field — in that order.
    /// </summary>
    /// <remarks>
    /// The body-size feature is lowered <b>first</b>, before the token is validated and long before the
    /// form is read, because that is the only ordering in which a 4 GB post costs nothing.
    /// </remarks>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAntiforgery">The antiforgery service.</param>
    /// <param name="aCancellationToken">Cancels the read.</param>
    /// <returns>The upload, or the result to return instead.</returns>
    private static async Task<UploadRead> ReadUploadAsync(
        HttpContext aHttpContext,
        IAntiforgery aAntiforgery,
        CancellationToken aCancellationToken)
    {
        var vSizeFeature = aHttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (vSizeFeature is { IsReadOnly: false })
        {
            vSizeFeature.MaxRequestBodySize = MaxRequestBodyBytes;
        }

        if (aHttpContext.Request.ContentLength > MaxRequestBodyBytes)
        {
            return UploadRead.Rejected(TooLarge());
        }

        if (!aHttpContext.Request.HasFormContentType)
        {
            return UploadRead.Rejected(Results.BadRequest(new
            {
                accepted = false,
                reason = nameof(ImportRefusalReason.UnsupportedExtension),
                message = UploadBounds.ExtensionMessage
            }));
        }

        try
        {
            await aAntiforgery.ValidateRequestAsync(aHttpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return UploadRead.Rejected(Results.StatusCode(StatusCodes.Status400BadRequest));
        }

        IFormCollection vForm;

        try
        {
            vForm = await aHttpContext.Request.ReadFormAsync(aCancellationToken).ConfigureAwait(false);
        }
        catch (BadHttpRequestException)
        {
            // Kestrel refused the body at the cap set above; the client never got to send it all.
            return UploadRead.Rejected(TooLarge());
        }

        var vFile = vForm.Files.GetFile(FileField);

        if (vFile is null)
        {
            return UploadRead.Rejected(Results.BadRequest(new
            {
                accepted = false,
                reason = nameof(ImportRefusalReason.Empty),
                message = "No file was attached."
            }));
        }

        return UploadRead.Accepted(new ImportUpload
        {
            // Only the extension and the base name are ever used; the path a browser may prepend is
            // discarded here rather than trusted anywhere downstream.
            FileName = ImportStreamCatalog.FileNameOf(vFile.FileName ?? string.Empty),
            DeclaredLength = vFile.Length,
            Content = vFile.OpenReadStream()
        });
    }

    /// <summary>The 413 an oversized post is answered with, before its body has been read.</summary>
    /// <returns>The result to return.</returns>
    private static IResult TooLarge() => Results.Json(
        new
        {
            accepted = false,
            reason = nameof(ImportRefusalReason.TooLarge),
            message = UploadBounds.SizeMessage
        },
        statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>
    /// Maps a refusal onto its HTTP status and body.
    /// </summary>
    /// <param name="aRefusal">Why the bundle was refused.</param>
    /// <returns>The result to return.</returns>
    private static IResult Refused(ImportRefusal aRefusal)
    {
        var vStatus = aRefusal.Reason == ImportRefusalReason.TooLarge
            ? StatusCodes.Status413PayloadTooLarge
            : StatusCodes.Status400BadRequest;

        return Results.Json(
            new { accepted = false, reason = aRefusal.Reason.ToString(), message = aRefusal.Message },
            statusCode: vStatus);
    }

    /// <summary>
    /// Shapes an accepted preview for the dialog.
    /// </summary>
    /// <param name="aPreview">What the dry run found.</param>
    /// <returns>The JSON body.</returns>
    private static object PreviewBody(ImportPreview aPreview) => new
    {
        accepted = true,
        bundleSha = aPreview.BundleSha,
        framework = aPreview.Framework,
        totalRecords = aPreview.TotalRecords,
        totalInvalidLines = aPreview.TotalInvalidLines,
        earliestTs = aPreview.EarliestTs,
        latestTs = aPreview.LatestTs,
        unknownFields = aPreview.UnknownFields,
        unrecognisedEntries = aPreview.UnrecognisedEntries,
        streams = aPreview.Streams.Select(aS => new
        {
            stream = aS.Stream,
            entryName = aS.EntryName,
            bytes = aS.Bytes,
            records = aS.Records,
            duplicatesCollapsed = aS.DuplicatesCollapsed,
            invalidLines = aS.InvalidLines,
            recordsAboveSchemaV1 = aS.RecordsAboveSchemaV1,
            earliestTs = aS.EarliestTs,
            latestTs = aS.LatestTs,
            unknownFields = aS.UnknownFields,
            parseSupported = aS.IsParseSupported
        })
    };

    /// <summary>
    /// Shapes a completed import for the dialog.
    /// </summary>
    /// <remarks>
    /// The archive paths are server-side absolute paths and are deliberately not returned: they say
    /// nothing the user needs and everything an attacker would like.
    /// </remarks>
    /// <param name="aResult">What the import wrote.</param>
    /// <returns>The JSON body.</returns>
    private static object CommitBody(ImportCommitResult aResult) => new
    {
        accepted = true,
        bundleSha = aResult.BundleSha,
        framework = aResult.Framework,
        importedTs = aResult.ImportedTs,
        recordsAdded = aResult.RecordsAdded,
        duplicatesCollapsed = aResult.DuplicatesCollapsed,
        streams = aResult.Streams.Select(aS => new
        {
            stream = aS.Stream,
            presented = aS.Presented,
            added = aS.Added,
            duplicatesCollapsed = aS.DuplicatesCollapsed,
            invalidLines = aS.InvalidLines
        })
    };

    /// <summary>Either the upload to import or the response to return instead.</summary>
    /// <param name="Upload">The upload, when the request was well formed.</param>
    /// <param name="Failure">The response to return, when it was not.</param>
    private sealed record UploadRead(ImportUpload? Upload, IResult? Failure)
    {
        /// <summary>Wraps a well-formed upload.</summary>
        /// <param name="aUpload">The upload.</param>
        /// <returns>An accepted read.</returns>
        public static UploadRead Accepted(ImportUpload aUpload) => new(aUpload, null);

        /// <summary>Wraps the response a malformed request gets.</summary>
        /// <param name="aFailure">The response.</param>
        /// <returns>A rejected read.</returns>
        public static UploadRead Rejected(IResult aFailure) => new(null, aFailure);
    }
}
