using System.Security.Claims;
using TfLens.Services.Auth;

namespace TfLens.Services.Ui;

/// <summary>
/// Reads the shell's display claims straight off a principal.
/// </summary>
/// <remarks>
/// <see cref="CurrentUser"/> is the accessor for request-scoped code, but it resolves the principal through
/// <c>IHttpContextAccessor</c>, which is null once an interactive Blazor circuit is running. The shell is
/// interactive, so it takes the principal the cascading <c>AuthenticationState</c> hands it and reads the
/// same claim types from that — one set of claim names, two ways in.
/// </remarks>
public static class ShellIdentity
{
    /// <summary>
    /// Reads the AppManager user id from a principal.
    /// </summary>
    /// <param name="aPrincipal">The signed-in principal, or <c>null</c>.</param>
    /// <returns>The user id, or <c>null</c> when nobody is signed in.</returns>
    public static int? UserId(ClaimsPrincipal? aPrincipal) =>
        int.TryParse(aPrincipal?.FindFirst(CurrentUser.UserIdClaim)?.Value, out var vId) ? vId : null;

    /// <summary>
    /// Reads the signed-in user's email, shown as the user-menu label.
    /// </summary>
    /// <param name="aPrincipal">The signed-in principal, or <c>null</c>.</param>
    /// <returns>The email, or an empty string when nobody is signed in.</returns>
    public static string Email(ClaimsPrincipal? aPrincipal) =>
        aPrincipal?.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

    /// <summary>
    /// Reads the display name shown beside the avatar.
    /// </summary>
    /// <param name="aPrincipal">The signed-in principal, or <c>null</c>.</param>
    /// <returns>The name claim, falling back to the email and then to an empty string.</returns>
    public static string DisplayName(ClaimsPrincipal? aPrincipal) =>
        aPrincipal?.FindFirst(ClaimTypes.Name)?.Value
        ?? aPrincipal?.FindFirst(ClaimTypes.Email)?.Value
        ?? string.Empty;

    /// <summary>
    /// Derives the avatar initials from a display name, the same way <see cref="CurrentUser.Initials"/> does.
    /// </summary>
    /// <param name="aDisplayName">The display name.</param>
    /// <returns>One or two upper-case letters, or <c>?</c> when there is no name.</returns>
    public static string Initials(string aDisplayName)
    {
        var vParts = (aDisplayName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return vParts.Length switch
        {
            0 => "?",
            1 => vParts[0][..1].ToUpperInvariant(),
            _ => string.Concat(vParts[0][..1], vParts[^1][..1]).ToUpperInvariant()
        };
    }
}
