using System.Security.Claims;

namespace TfLens.Services.Auth;

/// <summary>
/// The signed-in user, as the cookie carries them.
/// </summary>
/// <remarks>
/// Every page and service that reads data scopes it by <see cref="UserId"/> — isolation is a parameter
/// on every store and engine call, not a filter someone remembers to add (ADR-013). This type is the
/// single place that parameter comes from, so there is one thing to audit rather than a claim lookup
/// scattered across thirty components. It exposes only display claims: the AppManager access and
/// refresh tokens live server-side in the <c>AuthSession</c> table and never reach the browser.
/// </remarks>
public sealed class CurrentUser
{
    /// <summary>Claim type holding the server-side session id.</summary>
    public const string SessionIdClaim = "tflens:sid";

    /// <summary>Claim type holding the AppManager user id.</summary>
    public const string UserIdClaim = "tflens:uid";

    private readonly IHttpContextAccessor objHttpContextAccessor;

    /// <summary>
    /// Creates the accessor.
    /// </summary>
    /// <param name="aHttpContextAccessor">Supplies the current request's principal.</param>
    public CurrentUser(IHttpContextAccessor aHttpContextAccessor)
    {
        objHttpContextAccessor = aHttpContextAccessor;
    }

    /// <summary>The current claims principal, or <c>null</c> outside a request.</summary>
    private ClaimsPrincipal? Principal => objHttpContextAccessor.HttpContext?.User;

    /// <summary>True when the request carries a valid TfLens auth cookie.</summary>
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <summary>
    /// The AppManager user id, or <c>null</c> when nobody is signed in.
    /// </summary>
    /// <remarks>Callers that need a value should use <see cref="RequireUserId"/> rather than defaulting it.</remarks>
    public int? UserId =>
        int.TryParse(Principal?.FindFirst(UserIdClaim)?.Value, out var vId) ? vId : null;

    /// <summary>The server-side session id the cookie points at.</summary>
    public string? SessionId => Principal?.FindFirst(SessionIdClaim)?.Value;

    /// <summary>The signed-in user's email, shown in the header user menu.</summary>
    public string? Email => Principal?.FindFirst(ClaimTypes.Email)?.Value;

    /// <summary>The signed-in user's display name, shown beside the avatar.</summary>
    public string DisplayName => Principal?.FindFirst(ClaimTypes.Name)?.Value ?? Email ?? string.Empty;

    /// <summary>
    /// Every TfLens account is a Manager — there is no other role and no licence check (BRD-95).
    /// </summary>
    public string Role => "Manager";

    /// <summary>The avatar's initials, derived from the display name.</summary>
    public string Initials
    {
        get
        {
            var vParts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return vParts.Length switch
            {
                0 => "?",
                1 => vParts[0][..1].ToUpperInvariant(),
                _ => string.Concat(vParts[0][..1], vParts[^1][..1]).ToUpperInvariant()
            };
        }
    }

    /// <summary>
    /// Returns the user id, or throws when nobody is signed in.
    /// </summary>
    /// <returns>The AppManager user id.</returns>
    /// <exception cref="InvalidOperationException">The request is anonymous — the caller reached data code without an authenticated user.</exception>
    public int RequireUserId() =>
        UserId ?? throw new InvalidOperationException(
            "No signed-in user. Data access requires an authenticated request.");
}
