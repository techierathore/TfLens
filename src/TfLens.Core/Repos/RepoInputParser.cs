using System.Text.RegularExpressions;

namespace TfLens.Core.Repos;

/// <summary>
/// Turns whatever the Connect dialog was given into a <see cref="RepoRef"/>, or says why it cannot.
/// </summary>
/// <remarks>
/// Accepted forms are a full github.com URL (<c>https://github.com/owner/name</c>, with or without a
/// <c>.git</c> suffix or a trailing slash), the same URL without its scheme
/// (<c>github.com/owner/name</c>), and a bare <c>owner/name</c>. Everything else is refused with a
/// message the dialog can render as-is: a wrong host, a deep link into a tree or a file, an SSH
/// remote, or a name carrying characters GitHub does not allow. The parser never contacts GitHub —
/// existence and visibility are <see cref="RepoRegistry"/>'s job.
/// </remarks>
public static partial class RepoInputParser
{
    /// <summary>The message shown when nothing was typed at all.</summary>
    private const string EmptyMessage =
        "Enter a GitHub repository as https://github.com/owner/name or as owner/name.";

    /// <summary>
    /// Parses a repository input.
    /// </summary>
    /// <param name="aInput">A GitHub URL or an <c>owner/name</c> pair.</param>
    /// <param name="aRepo">The parsed reference when the method returns <c>true</c>; otherwise <c>null</c>.</param>
    /// <param name="aError">A user-facing reason when the method returns <c>false</c>; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when the input named exactly one github.com repository.</returns>
    /// <remarks>
    /// The method allocates nothing on the failure path beyond the message, and never throws: a bad
    /// input is an expected outcome of a dialog, not an exceptional condition.
    /// </remarks>
    public static bool TryParse(string? aInput, out RepoRef? aRepo, out string? aError)
    {
        aRepo = null;
        aError = null;

        if (string.IsNullOrWhiteSpace(aInput))
        {
            aError = EmptyMessage;
            return false;
        }

        var vPath = StripHost(aInput.Trim(), out aError);
        return vPath is not null && TrySplit(aInput.Trim(), vPath, out aRepo, out aError);
    }

    /// <summary>
    /// Removes an accepted github.com prefix, leaving the repository path.
    /// </summary>
    /// <param name="aInput">The trimmed input.</param>
    /// <param name="aError">A user-facing reason when the host is not github.com.</param>
    /// <returns>The path portion, or <c>null</c> when the input names another host.</returns>
    private static string? StripHost(string aInput, out string? aError)
    {
        aError = null;

        if (aInput.Contains("://", StringComparison.Ordinal))
        {
            return StripUrl(aInput, out aError);
        }

        if (aInput.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            aError = "SSH remotes are not supported — paste the https://github.com/owner/name URL instead.";
            return null;
        }

        return aInput.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)
            ? aInput["github.com/".Length..]
            : aInput;
    }

    /// <summary>
    /// Validates a full URL and returns its path.
    /// </summary>
    /// <param name="aInput">The trimmed input, known to carry a scheme.</param>
    /// <param name="aError">A user-facing reason when the URL is not a github.com repository URL.</param>
    /// <returns>The URL's path, or <c>null</c> when the URL is unusable.</returns>
    private static string? StripUrl(string aInput, out string? aError)
    {
        aError = null;

        if (!Uri.TryCreate(aInput, UriKind.Absolute, out var vUri)
            || (vUri.Scheme != Uri.UriSchemeHttp && vUri.Scheme != Uri.UriSchemeHttps))
        {
            aError = $"'{aInput}' is not an http(s) URL — use https://github.com/owner/name.";
            return null;
        }

        var vHost = vUri.Host.ToLowerInvariant();
        if (vHost is not ("github.com" or "www.github.com"))
        {
            aError = $"Only github.com repositories are supported — '{vUri.Host}' is not github.com.";
            return null;
        }

        return vUri.AbsolutePath;
    }

    /// <summary>
    /// Splits a repository path into owner and name and validates both.
    /// </summary>
    /// <param name="aInput">The original trimmed input, quoted back in any error message.</param>
    /// <param name="aPath">The path portion, with the host already removed.</param>
    /// <param name="aRepo">The parsed reference on success.</param>
    /// <param name="aError">A user-facing reason on failure.</param>
    /// <returns><c>true</c> when the path was exactly two valid segments.</returns>
    private static bool TrySplit(string aInput, string aPath, out RepoRef? aRepo, out string? aError)
    {
        aRepo = null;
        aError = null;

        var vTrimmed = aPath.Trim('/');
        if (vTrimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            vTrimmed = vTrimmed[..^4];
        }

        var vParts = vTrimmed.Split('/', StringSplitOptions.None);
        if (vParts.Length != 2 || !IsSegment(vParts[0]) || !IsSegment(vParts[1]))
        {
            aError = $"'{aInput}' is not a repository — use owner/name or https://github.com/owner/name.";
            return false;
        }

        aRepo = new RepoRef(vParts[0], vParts[1]);
        return true;
    }

    /// <summary>
    /// Tests one path segment against GitHub's owner / repository-name character rules.
    /// </summary>
    /// <param name="aSegment">The segment to test.</param>
    /// <returns><c>true</c> when the segment could be a GitHub owner or repository name.</returns>
    private static bool IsSegment(string aSegment) =>
        aSegment.Length is > 0 and <= 100 && aSegment is not ("." or "..") && SegmentPattern().IsMatch(aSegment);

    /// <summary>
    /// The character rule for an owner or repository name: alphanumeric start, then word characters,
    /// dots and hyphens.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPattern();
}
