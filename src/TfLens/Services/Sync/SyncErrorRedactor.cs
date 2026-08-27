using System.Text.RegularExpressions;
using TfLens.Core.GitHub;

namespace TfLens.Services.Sync;

/// <summary>
/// Turns a per-repository sync failure into the short, safe sentence that lands in
/// <c>SyncState.LastError</c> and is rendered on the Coverage page.
/// </summary>
/// <remarks>
/// BRD-15 / REQ-FN-023 — the recorded reason is a status code plus a short reason and never contains a
/// token, a secret or a URL carrying one. Redaction is belt-and-braces: TfLens never puts the PAT in a
/// URL in the first place, but a message that arrives from a dependency is scrubbed before it is
/// stored, because <c>LastError</c> is displayed.
/// </remarks>
public static partial class SyncErrorRedactor
{
    /// <summary>The longest message ever written to <c>SyncState.LastError</c>.</summary>
    public const int MaxLength = 200;

    /// <summary>What replaces anything that might be a secret.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>
    /// Reduces an exception to a redacted status-code-plus-short-reason.
    /// </summary>
    /// <param name="aException">The failure raised while syncing one repository.</param>
    /// <returns>A sentence safe to store and display.</returns>
    public static string Redact(Exception aException) => aException switch
    {
        GitHubRateLimitException vRateLimit => vRateLimit.Message,
        HttpRequestException { StatusCode: { } vStatus } => Describe((int)vStatus),
        HttpRequestException => "Network error reaching GitHub.",
        TaskCanceledException => "Timed out reaching GitHub.",
        _ => Scrub($"{aException.GetType().Name}: {aException.Message}")
    };

    /// <summary>
    /// Names an HTTP status in the vocabulary BRD-15 lists.
    /// </summary>
    /// <param name="aStatusCode">The status GitHub answered with.</param>
    /// <returns>The status code plus a short reason.</returns>
    public static string Describe(int aStatusCode) => aStatusCode switch
    {
        401 => "HTTP 401 — GitHub rejected the credential; the server PAT may have expired.",
        403 => "HTTP 403 — GitHub refused the read.",
        404 => "HTTP 404 — the repository or branch is no longer reachable.",
        _ => $"HTTP {aStatusCode} — GitHub refused the read."
    };

    /// <summary>
    /// Strips anything token-shaped or URL-shaped out of a message and trims it to length.
    /// </summary>
    /// <param name="aMessage">The raw message.</param>
    /// <returns>The scrubbed, truncated message.</returns>
    public static string Scrub(string aMessage)
    {
        if (string.IsNullOrWhiteSpace(aMessage))
        {
            return "Sync failed.";
        }

        var vScrubbed = TokenPattern().Replace(aMessage, Placeholder);
        vScrubbed = BearerPattern().Replace(vScrubbed, $"Bearer {Placeholder}");
        vScrubbed = UrlPattern().Replace(vScrubbed, Placeholder);
        vScrubbed = vScrubbed.Replace('\r', ' ').Replace('\n', ' ').Trim();

        return vScrubbed.Length <= MaxLength ? vScrubbed : vScrubbed[..MaxLength] + "…";
    }

    /// <summary>Matches a GitHub personal access token in any of its documented prefixes.</summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{16,}|github_pat_[A-Za-z0-9_]{20,}", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();

    /// <summary>Matches an <c>Authorization: Bearer</c> value.</summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    /// <summary>Matches any absolute URL, which could carry a credential in its userinfo or query.</summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"[a-z][a-z0-9+.-]*://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
