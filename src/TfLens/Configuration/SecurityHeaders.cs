namespace TfLens.Configuration;

/// <summary>
/// The response headers that hold the browser to TfLens's security posture (BRD-83).
/// </summary>
/// <remarks>
/// TfLens has no inbound API, no capture endpoint and no cross-origin surface at all, so these are
/// all "deny" rather than "allow list". They are deliberately conservative: no Content-Security-Policy
/// is emitted here, because Blazor Server's framework script and TrBlazeUI's interop would need a
/// nonce pipeline to survive one, and a CSP that has to be relaxed to work is worse than none.
/// </remarks>
public static class SecurityHeaders
{
    /// <summary>
    /// Adds the static security headers to every response.
    /// </summary>
    /// <param name="aApp">The application to add the middleware to.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>Register this first, so the headers are present on error responses too.</remarks>
    public static WebApplication UseTfLensSecurityHeaders(this WebApplication aApp)
    {
        aApp.Use(async (aContext, aNext) =>
        {
            var vHeaders = aContext.Response.Headers;

            // No page of TfLens is ever meant to be framed: the whole app is behind a session cookie.
            vHeaders["X-Frame-Options"] = "DENY";

            // Sniffing a JSONL archive or an export as HTML would be a stored-XSS vector.
            vHeaders["X-Content-Type-Options"] = "nosniff";

            // A report URL can carry a user id; never hand it to a third-party origin.
            vHeaders["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // TfLens needs no device capability whatsoever.
            vHeaders["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

            await aNext();
        });

        return aApp;
    }
}
