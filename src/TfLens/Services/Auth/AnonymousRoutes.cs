namespace TfLens.Services.Auth;

/// <summary>
/// The complete list of routes a signed-out visitor may reach.
/// </summary>
/// <remarks>
/// <para>
/// REQ-FN-005 / BRD-2: authorization is applied as a <b>fallback policy</b>, so a page added tomorrow is
/// protected because nobody opted it in — the only way to be anonymous is to appear here. That is the
/// opposite of route-by-route <c>[Authorize]</c>, where a forgotten attribute silently exposes a page.
/// </para>
/// <para>
/// The framework prefixes are here for a functional reason rather than a policy one: the Blazor Server
/// circuit (<c>/_blazor</c>) and the framework assets (<c>/_framework</c>, <c>/_content</c>) must be
/// reachable from the anonymous pages or the sign-in form cannot be interactive at all.
/// </para>
/// </remarks>
public static class AnonymousRoutes
{
    /// <summary>The five anonymous pages named by BRD-2, plus the form posts they submit to.</summary>
    public static readonly IReadOnlyList<string> Paths =
    [
        "/login",
        "/register",
        "/forgot-password",
        "/reset-password",
        "/healthz",
        "/auth/login",
        "/auth/register",
        "/auth/forgot-password",
        "/auth/reset-password"
    ];

    /// <summary>Prefixes the Blazor runtime needs before anybody has signed in.</summary>
    public static readonly IReadOnlyList<string> Prefixes =
    [
        "/_blazor",
        "/_framework",
        "/_content"
    ];

    /// <summary>
    /// Tests whether a request path may be served without an auth cookie.
    /// </summary>
    /// <param name="aPath">The request path, or <c>null</c> outside a request.</param>
    /// <returns><c>true</c> when the path is one of the anonymous routes or framework prefixes.</returns>
    /// <remarks>The comparison ignores case and a trailing slash, and never looks at the query string.</remarks>
    public static bool IsAnonymous(PathString aPath)
    {
        if (!aPath.HasValue)
        {
            return false;
        }

        var vPath = aPath.Value!.TrimEnd('/');
        if (vPath.Length == 0)
        {
            return false;
        }

        return Paths.Any(aCandidate => string.Equals(aCandidate, vPath, StringComparison.OrdinalIgnoreCase))
               || Prefixes.Any(aPrefix => vPath.StartsWith(aPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
