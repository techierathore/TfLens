using Microsoft.AspNetCore.Antiforgery;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Services.Auth;

/// <summary>
/// The minimal-API endpoints the auth flow needs outside the Blazor circuit.
/// </summary>
/// <remarks>
/// <para>
/// Cookie sign-in and sign-out must run on a real HTTP response, which an interactive Blazor Server
/// component does not have — so the auth pages post here, and this is also where the AppManager logout
/// call is made before the session row is deleted.
/// </para>
/// <para>
/// Every endpoint validates the antiforgery token before it reads a field (BRD-83), and every failure
/// redirects back to the posting page with a short opaque reason: the AppManager error code is logged
/// but never handed to the browser, so a failed sign-in cannot be used to enumerate accounts (BRD-90).
/// </para>
/// </remarks>
public static class AuthEndpoints
{
    /// <summary>Reason token for "we are not saying which of the two was wrong".</summary>
    private const string ReasonInvalid = "invalid";

    /// <summary>Reason token for the one code with a user-facing meaning of its own.</summary>
    private const string ReasonLocked = "locked";

    /// <summary>Reason token for an email that is already registered.</summary>
    private const string ReasonDuplicate = "duplicate";

    /// <summary>Reason token for a password that fails the complexity rules.</summary>
    private const string ReasonWeakPassword = "weak";

    /// <summary>Reason token for a reset link that is unknown, expired or for another application.</summary>
    private const string ReasonExpiredLink = "expired";

    /// <summary>Reason token for an antiforgery failure or a missing required field.</summary>
    private const string ReasonBadRequest = "badrequest";

    /// <summary>
    /// Maps the sign-in, registration, password-reset and sign-out endpoints.
    /// </summary>
    /// <param name="aApp">The web application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapAuthEndpoints(this WebApplication aApp)
    {
        aApp.MapPost("/auth/login", LoginAsync).AllowAnonymous();
        aApp.MapPost("/auth/register", RegisterAsync).AllowAnonymous();
        aApp.MapPost("/auth/forgot-password", ForgotPasswordAsync).AllowAnonymous();
        aApp.MapPost("/auth/reset-password", ResetPasswordAsync).AllowAnonymous();
        aApp.MapPost("/auth/logout", LogoutAsync).RequireAuthorization();

        return aApp;
    }

    /// <summary>
    /// Signs a user in from the <c>/login</c> form post.
    /// </summary>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAuthService">The session owner.</param>
    /// <param name="aAntiforgery">Validates the form token.</param>
    /// <param name="aLogger">Diagnostics; receives the error code, never the credentials.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A redirect to the landing page on success, or back to <c>/login</c> with a reason.</returns>
    private static async Task<IResult> LoginAsync(
        HttpContext aHttpContext,
        AuthService aAuthService,
        IAntiforgery aAntiforgery,
        ILogger<AuthService> aLogger,
        CancellationToken aCancellationToken)
    {
        if (!await IsFormTrustedAsync(aHttpContext, aAntiforgery))
        {
            return Results.Redirect(Back("/login", null, ReasonBadRequest));
        }

        var vForm = await aHttpContext.Request.ReadFormAsync(aCancellationToken);
        var vReturnUrl = LocalReturnUrl(vForm["ReturnUrl"]);
        var vEmail = vForm["Email"].ToString().Trim();
        var vPassword = vForm["Password"].ToString();

        if (vEmail.Length == 0 || vPassword.Length == 0)
        {
            return Results.Redirect(Back("/login", vReturnUrl, ReasonInvalid));
        }

        try
        {
            var vAuth = await aAuthService.SignInAsync(aHttpContext, vEmail, vPassword, aCancellationToken);
            return Results.Redirect(await LandingUrlAsync(aHttpContext, vReturnUrl, vAuth.UserId, aCancellationToken));
        }
        catch (AppManagerException vEx)
        {
            aLogger.LogWarning("Sign-in refused with {Code} ({Status}).", vEx.Code, vEx.StatusCode);
            return Results.Redirect(Back("/login", vReturnUrl, ReasonFor(vEx)));
        }
    }

    /// <summary>
    /// Registers a user from the <c>/register</c> form post and signs them straight in.
    /// </summary>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAuthService">The session owner.</param>
    /// <param name="aAntiforgery">Validates the form token.</param>
    /// <param name="aLogger">Diagnostics; receives the error code, never the credentials.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A redirect to <c>/repos</c> on success, or back to <c>/register</c> with a reason.</returns>
    private static async Task<IResult> RegisterAsync(
        HttpContext aHttpContext,
        AuthService aAuthService,
        IAntiforgery aAntiforgery,
        ILogger<AuthService> aLogger,
        CancellationToken aCancellationToken)
    {
        if (!await IsFormTrustedAsync(aHttpContext, aAntiforgery))
        {
            return Results.Redirect(Back("/register", null, ReasonBadRequest));
        }

        var vForm = await aHttpContext.Request.ReadFormAsync(aCancellationToken);
        var vReturnUrl = LocalReturnUrl(vForm["ReturnUrl"]);

        var vRequest = new RegisterRequest(
            vForm["Email"].ToString().Trim(),
            vForm["Password"].ToString(),
            vForm["FirstName"].ToString().Trim(),
            vForm["LastName"].ToString().Trim());

        try
        {
            var vAuth = await aAuthService.RegisterAsync(aHttpContext, vRequest, aCancellationToken);
            return Results.Redirect(await LandingUrlAsync(aHttpContext, vReturnUrl, vAuth.UserId, aCancellationToken));
        }
        catch (AppManagerException vEx)
        {
            aLogger.LogWarning("Registration refused with {Code} ({Status}).", vEx.Code, vEx.StatusCode);
            return Results.Redirect(Back("/register", vReturnUrl, ReasonFor(vEx)));
        }
    }

    /// <summary>
    /// Starts a password reset from the <c>/forgot-password</c> form post.
    /// </summary>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAppManagerClient">The AppManager identity client.</param>
    /// <param name="aAntiforgery">Validates the form token.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The same neutral redirect whether or not the address exists (BRD-92).</returns>
    private static async Task<IResult> ForgotPasswordAsync(
        HttpContext aHttpContext,
        IAppManagerClient aAppManagerClient,
        IAntiforgery aAntiforgery,
        CancellationToken aCancellationToken)
    {
        if (!await IsFormTrustedAsync(aHttpContext, aAntiforgery))
        {
            return Results.Redirect(Back("/forgot-password", null, ReasonBadRequest));
        }

        var vForm = await aHttpContext.Request.ReadFormAsync(aCancellationToken);
        await aAppManagerClient.ForgotPasswordAsync(vForm["Email"].ToString().Trim(), aCancellationToken);

        // Enumeration safety: one outcome, always.
        return Results.Redirect("/forgot-password?sent=1");
    }

    /// <summary>
    /// Completes a password reset from the <c>/reset-password</c> form post.
    /// </summary>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAppManagerClient">The AppManager identity client.</param>
    /// <param name="aAntiforgery">Validates the form token.</param>
    /// <param name="aLogger">Diagnostics; receives the error code, never the reset token.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A redirect to <c>/login</c> on success, or back with a single "invalid or expired" reason.</returns>
    private static async Task<IResult> ResetPasswordAsync(
        HttpContext aHttpContext,
        IAppManagerClient aAppManagerClient,
        IAntiforgery aAntiforgery,
        ILogger<AuthService> aLogger,
        CancellationToken aCancellationToken)
    {
        if (!await IsFormTrustedAsync(aHttpContext, aAntiforgery))
        {
            return Results.Redirect(Back("/reset-password", null, ReasonBadRequest));
        }

        var vForm = await aHttpContext.Request.ReadFormAsync(aCancellationToken);

        try
        {
            await aAppManagerClient.ResetPasswordAsync(
                vForm["Token"].ToString(),
                vForm["Password"].ToString(),
                aCancellationToken);

            return Results.Redirect("/login?reset=1");
        }
        catch (AppManagerException vEx)
        {
            aLogger.LogWarning("Password reset refused with {Code} ({Status}).", vEx.Code, vEx.StatusCode);
            return Results.Redirect(Back("/reset-password", null, ReasonFor(vEx)));
        }
    }

    /// <summary>
    /// Signs the user out from the header's sign-out form post.
    /// </summary>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAuthService">The session owner.</param>
    /// <param name="aAntiforgery">Validates the form token.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A redirect to <c>/login</c> once the cookie and the session row are gone.</returns>
    private static async Task<IResult> LogoutAsync(
        HttpContext aHttpContext,
        AuthService aAuthService,
        IAntiforgery aAntiforgery,
        CancellationToken aCancellationToken)
    {
        if (!await IsFormTrustedAsync(aHttpContext, aAntiforgery))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        await aAuthService.SignOutAsync(aHttpContext, aCancellationToken);
        return Results.Redirect("/login");
    }

    /// <summary>
    /// Validates the antiforgery token on a form post.
    /// </summary>
    /// <param name="aHttpContext">The posting request.</param>
    /// <param name="aAntiforgery">The antiforgery service.</param>
    /// <returns><c>true</c> when the token is present and valid.</returns>
    private static async Task<bool> IsFormTrustedAsync(HttpContext aHttpContext, IAntiforgery aAntiforgery)
    {
        try
        {
            await aAntiforgery.ValidateRequestAsync(aHttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Chooses where a freshly signed-in user lands.
    /// </summary>
    /// <param name="aHttpContext">The request, used to resolve the store when one is registered.</param>
    /// <param name="aReturnUrl">The validated local return URL, or <c>null</c>.</param>
    /// <param name="aUserId">The AppManager user id whose repositories decide the fallback.</param>
    /// <param name="aCancellationToken">Cancels the read.</param>
    /// <returns>The return URL, else <c>/repos</c> when the user has no connected repositories, else <c>/</c>.</returns>
    /// <remarks>
    /// BRD-1 / REQ-FN-006. The store is resolved optionally so the auth area builds and runs before the
    /// storage area is registered; with no store the user lands on <c>/repos</c>, which is the correct
    /// answer for an account that demonstrably has nothing connected.
    /// </remarks>
    private static async Task<string> LandingUrlAsync(
        HttpContext aHttpContext,
        string? aReturnUrl,
        int aUserId,
        CancellationToken aCancellationToken)
    {
        if (aReturnUrl is not null)
        {
            return aReturnUrl;
        }

        var vStore = aHttpContext.RequestServices.GetService<ITelemetryStore>();
        if (vStore is null)
        {
            return "/repos";
        }

        var vRepos = await vStore.ReadUserReposAsync(aUserId, aCancellationToken);
        return vRepos.Count == 0 ? "/repos" : "/";
    }

    /// <summary>
    /// Accepts a return URL only when it is a local path.
    /// </summary>
    /// <param name="aCandidate">The value submitted with the form.</param>
    /// <returns>The path, or <c>null</c> when it is absent or would leave the site.</returns>
    /// <remarks>
    /// An open redirect is the classic way to turn a sign-in page into a phishing hop, so anything that
    /// is not a single-slash-rooted path — including <c>//evil.example</c> and <c>/\evil.example</c> —
    /// is discarded rather than sanitised.
    /// </remarks>
    private static string? LocalReturnUrl(string? aCandidate)
    {
        if (string.IsNullOrWhiteSpace(aCandidate) || aCandidate.Length < 2)
        {
            return null;
        }

        var vCandidate = aCandidate.Trim();
        var vIsLocal = vCandidate[0] == '/' && vCandidate[1] != '/' && vCandidate[1] != '\\';
        return vIsLocal ? vCandidate : null;
    }

    /// <summary>
    /// Builds the redirect back to a posting page, preserving the return URL.
    /// </summary>
    /// <param name="aPath">The page that was posted from.</param>
    /// <param name="aReturnUrl">The validated local return URL, or <c>null</c>.</param>
    /// <param name="aReason">The opaque reason token the page renders a message for.</param>
    /// <returns>The redirect target.</returns>
    private static string Back(string aPath, string? aReturnUrl, string aReason)
    {
        var vUrl = $"{aPath}?error={aReason}";
        return aReturnUrl is null ? vUrl : $"{vUrl}&returnUrl={Uri.EscapeDataString(aReturnUrl)}";
    }

    /// <summary>
    /// Maps an AppManager error code to the opaque reason the browser is allowed to see.
    /// </summary>
    /// <param name="aException">The exception the client threw.</param>
    /// <returns>A short reason token — never the AppManager code itself.</returns>
    /// <remarks>
    /// <c>INVALID_RESET_TOKEN</c> and <c>APP_ID_MISMATCH</c> deliberately collapse onto one outcome, so a
    /// wrong-tenant link is indistinguishable from a stale one (BRD-92).
    /// </remarks>
    private static string ReasonFor(AppManagerException aException) => aException.Code switch
    {
        AppManagerException.Codes.AccountLocked => ReasonLocked,
        AppManagerException.Codes.DuplicateEmail => ReasonDuplicate,
        AppManagerException.Codes.ValidationError => ReasonWeakPassword,
        "INVALID_PASSWORD" => ReasonWeakPassword,
        AppManagerException.Codes.InvalidResetToken => ReasonExpiredLink,
        AppManagerException.Codes.AppIdMismatch => ReasonExpiredLink,
        _ => ReasonInvalid
    };
}
