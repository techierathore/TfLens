using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Services.Auth;

/// <summary>
/// Owns the TfLens session: cookie in the browser, AppManager tokens on the server.
/// </summary>
/// <remarks>
/// <para>
/// BRD-93. On a successful sign-in or registration the AppManager access and refresh tokens are written
/// to the <c>AuthSession</c> table under a random 256-bit session id, and the browser is given a cookie
/// carrying only that id plus display claims. <b>No AppManager token ever reaches the browser</b> — not
/// in the cookie, not in storage, not in markup.
/// </para>
/// <para>
/// The access token is renewed through <c>POST /AuthSvc/refresh</c> once it is within
/// <see cref="RefreshWindow"/> of expiry, rotating the stored refresh token; a resumed cookie is
/// revalidated through <c>POST /AuthSvc/validate</c> at most once per <see cref="ValidationInterval"/>.
/// A refresh or validation failure signs the user out rather than serving a stale session.
/// </para>
/// </remarks>
public sealed class AuthService
{
    /// <summary>The single application role TfLens ever issues (BRD-95).</summary>
    public const string ManagerRole = "Manager";

    /// <summary>How long before expiry the access token is renewed.</summary>
    public static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);

    /// <summary>How often a resumed cookie is revalidated against AppManager.</summary>
    public static readonly TimeSpan ValidationInterval = TimeSpan.FromHours(1);

    /// <summary>How long the auth cookie lives; sliding, so activity extends it (BRD-93).</summary>
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(12);

    private readonly IAppManagerClient objAppManagerClient;
    private readonly IAuthSessionStore objSessionStore;
    private readonly IHttpContextAccessor objHttpContextAccessor;
    private readonly CurrentUser objCurrentUser;
    private readonly ILogger<AuthService> objLogger;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="aAppManagerClient">The AppManager identity client.</param>
    /// <param name="aSessionStore">Server-side storage of the AppManager tokens.</param>
    /// <param name="aHttpContextAccessor">Supplies the request the cookie is written on.</param>
    /// <param name="aCurrentUser">Reads the session id and user id back out of the issued cookie.</param>
    /// <param name="aLogger">Diagnostics; never receives a token or a password.</param>
    public AuthService(
        IAppManagerClient aAppManagerClient,
        IAuthSessionStore aSessionStore,
        IHttpContextAccessor aHttpContextAccessor,
        CurrentUser aCurrentUser,
        ILogger<AuthService> aLogger)
    {
        objAppManagerClient = aAppManagerClient;
        objSessionStore = aSessionStore;
        objHttpContextAccessor = aHttpContextAccessor;
        objCurrentUser = aCurrentUser;
        objLogger = aLogger;
    }

    /// <summary>
    /// Signs a user in against AppManager and issues the TfLens cookie.
    /// </summary>
    /// <param name="aHttpContext">The request to write the cookie on; sign-in needs a real HTTP response.</param>
    /// <param name="aEmail">The user's email address.</param>
    /// <param name="aPassword">The plaintext password; encrypted by the client, never stored or logged.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The AppManager response, so the caller can decide where to land the user.</returns>
    /// <exception cref="AppManagerException">AppManager rejected the sign-in; the code says why.</exception>
    public async Task<AuthResponseData> SignInAsync(
        HttpContext aHttpContext,
        string aEmail,
        string aPassword,
        CancellationToken aCancellationToken = default)
    {
        var vAuth = await objAppManagerClient.LoginAsync(aEmail, aPassword, aCancellationToken);
        await IssueSessionAsync(aHttpContext, vAuth, aCancellationToken);
        return vAuth;
    }

    /// <summary>
    /// Registers a user as a Manager of Application 1 and issues the same session a sign-in would.
    /// </summary>
    /// <param name="aHttpContext">The request to write the cookie on.</param>
    /// <param name="aRequest">The registration details.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The AppManager response for the newly created user.</returns>
    /// <exception cref="AppManagerException">Registration failed — duplicate email, weak password, or decryption.</exception>
    public async Task<AuthResponseData> RegisterAsync(
        HttpContext aHttpContext,
        RegisterRequest aRequest,
        CancellationToken aCancellationToken = default)
    {
        var vAuth = await objAppManagerClient.RegisterAsync(aRequest, aCancellationToken);
        await IssueSessionAsync(aHttpContext, vAuth, aCancellationToken);
        return vAuth;
    }

    /// <summary>
    /// Signs the user out of AppManager and of TfLens.
    /// </summary>
    /// <param name="aHttpContext">The request to clear the cookie on.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes once the cookie is cleared and the session row is gone.</returns>
    /// <remarks>
    /// BRD-4: an AppManager logout failure never leaves the user signed in locally — the reason is
    /// logged by code and the local session is destroyed regardless.
    /// </remarks>
    public async Task SignOutAsync(HttpContext aHttpContext, CancellationToken aCancellationToken = default)
    {
        var vSessionId = objCurrentUser.SessionId;

        if (vSessionId is not null)
        {
            await RevokeAtAppManagerAsync(vSessionId, aCancellationToken);
            await objSessionStore.DeleteAsync(vSessionId, aCancellationToken);
        }

        await aHttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        objLogger.LogInformation("Signed out session {SessionPresent}.", vSessionId is not null);
    }

    /// <summary>
    /// Returns an access token that is valid now, refreshing it when it is about to expire.
    /// </summary>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The access token, or <c>null</c> when there is no usable session any more.</returns>
    /// <remarks>
    /// Every caller that needs to talk to AppManager on the user's behalf goes through here, so the
    /// refresh window and the rotation of the stored refresh token exist in exactly one place.
    /// </remarks>
    public async Task<string?> GetAccessTokenAsync(CancellationToken aCancellationToken = default)
    {
        var vSession = await LoadSessionAsync(aCancellationToken);
        if (vSession is null)
        {
            return null;
        }

        if (!IsExpiringSoon(vSession.TokenExpiresAt))
        {
            return vSession.AccessToken;
        }

        return await RefreshAsync(vSession, aCancellationToken);
    }

    /// <summary>
    /// Revalidates a resumed cookie against AppManager, at most once per <see cref="ValidationInterval"/>.
    /// </summary>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns><c>true</c> when the session is still good; <c>false</c> when the caller should sign the user out.</returns>
    public async Task<bool> ValidateResumedSessionAsync(CancellationToken aCancellationToken = default)
    {
        var vSession = await LoadSessionAsync(aCancellationToken);
        if (vSession is null)
        {
            return false;
        }

        if (!IsDueForValidation(vSession.LastValidatedTs))
        {
            return true;
        }

        var vIsValid = await objAppManagerClient.ValidateAsync(vSession.AccessToken, aCancellationToken);
        if (!vIsValid)
        {
            objLogger.LogInformation("AppManager no longer accepts the session for user {UserId}.", vSession.UserId);
            await objSessionStore.DeleteAsync(vSession.SessionId, aCancellationToken);
            return false;
        }

        await objSessionStore.UpdateAsync(
            vSession with { LastValidatedTs = Timestamp() },
            aCancellationToken);

        return true;
    }

    /// <summary>
    /// Reads the signed-in user's live AppManager profile.
    /// </summary>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The profile as AppManager holds it, or <c>null</c> when the session has gone.</returns>
    /// <remarks>REQ-FN-011: the Profile page renders this, not the cookie claims.</remarks>
    public async Task<UserProfile?> GetProfileAsync(CancellationToken aCancellationToken = default)
    {
        var vAccessToken = await GetAccessTokenAsync(aCancellationToken);
        return vAccessToken is null
            ? null
            : await objAppManagerClient.GetProfileAsync(vAccessToken, aCancellationToken);
    }

    /// <summary>
    /// Changes the signed-in user's AppManager password.
    /// </summary>
    /// <param name="aCurrentPassword">The current plaintext password; encrypted by the client, never logged.</param>
    /// <param name="aNewPassword">The new plaintext password; encrypted by the client, never logged.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when AppManager has accepted the change.</returns>
    /// <exception cref="AppManagerException"><c>INVALID_CURRENT_PASSWORD</c> or a complexity violation.</exception>
    /// <exception cref="InvalidOperationException">There is no signed-in session to change a password for.</exception>
    public async Task ChangePasswordAsync(
        string aCurrentPassword,
        string aNewPassword,
        CancellationToken aCancellationToken = default)
    {
        var vAccessToken = await GetAccessTokenAsync(aCancellationToken)
                           ?? throw new InvalidOperationException("No signed-in session.");

        await objAppManagerClient.ChangePasswordAsync(
            vAccessToken,
            aCurrentPassword,
            aNewPassword,
            aCancellationToken);
    }

    /// <summary>
    /// Writes the session row and the auth cookie for a successful AppManager response.
    /// </summary>
    /// <param name="aHttpContext">The request to write the cookie on.</param>
    /// <param name="aAuth">What AppManager returned.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes once the row exists and the cookie is written.</returns>
    private async Task IssueSessionAsync(
        HttpContext aHttpContext,
        AuthResponseData aAuth,
        CancellationToken aCancellationToken)
    {
        var vSessionId = NewSessionId();

        await objSessionStore.CreateAsync(
            new AuthSessionRow
            {
                SessionId = vSessionId,
                UserId = aAuth.UserId,
                Email = aAuth.Email,
                DisplayName = aAuth.DisplayName,
                AccessToken = aAuth.AccessToken,
                RefreshToken = aAuth.RefreshToken,
                TokenExpiresAt = aAuth.TokenExpiresAt,
                CreatedTs = Timestamp(),
                LastValidatedTs = Timestamp()
            },
            aCancellationToken);

        var vPrincipal = BuildPrincipal(vSessionId, aAuth);

        await aHttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            vPrincipal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(CookieLifetime),
                AllowRefresh = true
            });

        // So the rest of THIS request already sees the signed-in user.
        aHttpContext.User = vPrincipal;
    }

    /// <summary>
    /// Builds the cookie's claims principal.
    /// </summary>
    /// <param name="aSessionId">The server-side session id — the only pointer the browser gets.</param>
    /// <param name="aAuth">What AppManager returned.</param>
    /// <returns>A principal carrying the session id, user id, email, display name and the Manager role.</returns>
    /// <remarks>
    /// The claim types are <see cref="CurrentUser.SessionIdClaim"/>, <see cref="CurrentUser.UserIdClaim"/>,
    /// <see cref="ClaimTypes.Email"/> and <see cref="ClaimTypes.Name"/> — the shape
    /// <see cref="CurrentUser"/> reads, which every page in the app depends on. No token is a claim.
    /// </remarks>
    private static ClaimsPrincipal BuildPrincipal(string aSessionId, AuthResponseData aAuth)
    {
        var vClaims = new List<Claim>
        {
            new(CurrentUser.SessionIdClaim, aSessionId),
            new(CurrentUser.UserIdClaim, aAuth.UserId.ToString()),
            new(ClaimTypes.NameIdentifier, aAuth.UserId.ToString()),
            new(ClaimTypes.Name, aAuth.DisplayName),
            new(ClaimTypes.Email, aAuth.Email),

            // BRD-95: every TfLens user is a Manager; no other role is ever requested or persisted.
            new(ClaimTypes.Role, ManagerRole)
        };

        var vIdentity = new ClaimsIdentity(vClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(vIdentity);
    }

    /// <summary>
    /// Loads the current request's session row.
    /// </summary>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The row, or <c>null</c> when the cookie names no live session.</returns>
    private async Task<AuthSessionRow?> LoadSessionAsync(CancellationToken aCancellationToken)
    {
        var vSessionId = objCurrentUser.SessionId;
        return vSessionId is null ? null : await objSessionStore.GetAsync(vSessionId, aCancellationToken);
    }

    /// <summary>
    /// Renews the access token and rotates the stored refresh token.
    /// </summary>
    /// <param name="aSession">The session whose tokens are about to expire.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The new access token, or <c>null</c> when AppManager refused to renew.</returns>
    private async Task<string?> RefreshAsync(AuthSessionRow aSession, CancellationToken aCancellationToken)
    {
        try
        {
            var vRefreshed = await objAppManagerClient.RefreshAsync(aSession.RefreshToken, aCancellationToken);

            await objSessionStore.UpdateAsync(
                aSession with
                {
                    AccessToken = vRefreshed.AccessToken,
                    RefreshToken = vRefreshed.RefreshToken,
                    TokenExpiresAt = vRefreshed.TokenExpiresAt,
                    LastValidatedTs = Timestamp()
                },
                aCancellationToken);

            objLogger.LogInformation("Refreshed the AppManager session for user {UserId}.", aSession.UserId);
            return vRefreshed.AccessToken;
        }
        catch (AppManagerException vEx)
        {
            // A stale session is never served: the row goes, and the next authorization check signs out.
            objLogger.LogWarning(
                "Refresh failed with {Code} for user {UserId}; ending the session.",
                vEx.Code,
                aSession.UserId);

            await objSessionStore.DeleteAsync(aSession.SessionId, aCancellationToken);
            await SignOutCookieAsync();
            return null;
        }
    }

    /// <summary>
    /// Revokes the refresh token at AppManager, tolerating a failure.
    /// </summary>
    /// <param name="aSessionId">The session being closed.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the attempt is over, successful or not.</returns>
    private async Task RevokeAtAppManagerAsync(string aSessionId, CancellationToken aCancellationToken)
    {
        var vSession = await objSessionStore.GetAsync(aSessionId, aCancellationToken);
        if (vSession is null)
        {
            return;
        }

        try
        {
            await objAppManagerClient.LogoutAsync(
                vSession.RefreshToken,
                vSession.AccessToken,
                aCancellationToken);
        }
        catch (AppManagerException vEx)
        {
            objLogger.LogWarning(
                "AppManager logout answered {Code} ({Status}); the local session is cleared regardless.",
                vEx.Code,
                vEx.StatusCode);
        }
    }

    /// <summary>
    /// Clears the auth cookie when a request is available to clear it on.
    /// </summary>
    /// <returns>A task that completes when the cookie has been cleared, or immediately outside a request.</returns>
    private async Task SignOutCookieAsync()
    {
        if (objHttpContextAccessor.HttpContext is { } vHttpContext && !vHttpContext.Response.HasStarted)
        {
            await vHttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>
    /// Tests whether an access token is inside the pre-expiry refresh window.
    /// </summary>
    /// <param name="aTokenExpiresAt">The ISO-8601 expiry AppManager reported.</param>
    /// <returns><c>true</c> when the token should be renewed now, including when the expiry is unreadable.</returns>
    private static bool IsExpiringSoon(string aTokenExpiresAt) =>
        !DateTimeOffset.TryParse(
            aTokenExpiresAt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var vExpiry)
        || DateTimeOffset.UtcNow >= vExpiry - RefreshWindow;

    /// <summary>
    /// Tests whether a resumed session is due for revalidation.
    /// </summary>
    /// <param name="aLastValidatedTs">When the session was last validated, or <c>null</c> if never.</param>
    /// <returns><c>true</c> when AppManager should be asked again.</returns>
    private static bool IsDueForValidation(string? aLastValidatedTs) =>
        !DateTimeOffset.TryParse(
            aLastValidatedTs,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var vLast)
        || DateTimeOffset.UtcNow - vLast >= ValidationInterval;

    /// <summary>
    /// Mints a session id with 256 bits of entropy.
    /// </summary>
    /// <returns>A URL-safe base64 session id.</returns>
    private static string NewSessionId() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>
    /// The current instant in the ISO-8601 spelling every TfLens timestamp column uses.
    /// </summary>
    /// <returns>A round-trip UTC timestamp.</returns>
    private static string Timestamp() => DateTimeOffset.UtcNow.ToString("O");
}
