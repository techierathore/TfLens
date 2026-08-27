namespace TfLens.Core.GitHub;

/// <summary>
/// Thrown when GitHub refuses a read because the caller's rate-limit window is exhausted.
/// </summary>
/// <remarks>
/// GitHub answers an exhausted window with <c>403</c> (classic) or <c>429</c> (secondary limit) plus
/// <c>x-ratelimit-remaining: 0</c>. The unauthenticated budget is 60 requests per hour per IP; the
/// optional server PAT lifts it to 5,000 (Architecture §12). The message carries only the wait in
/// minutes — never a token, never a URL — so it is safe to render on the Repos page verbatim.
/// </remarks>
public sealed class GitHubRateLimitException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="aStatusCode">The HTTP status GitHub answered with — 403 or 429.</param>
    /// <param name="aResetsAt">When the window reopens, when GitHub said so.</param>
    /// <param name="aMinutesUntilReset">Whole minutes until the window reopens, never below one.</param>
    public GitHubRateLimitException(int aStatusCode, DateTimeOffset? aResetsAt, int aMinutesUntilReset)
        : base(BuildMessage(aMinutesUntilReset))
    {
        StatusCode = aStatusCode;
        ResetsAt = aResetsAt;
        MinutesUntilReset = aMinutesUntilReset;
    }

    /// <summary>The HTTP status GitHub answered with.</summary>
    public int StatusCode { get; }

    /// <summary>When the rate-limit window reopens, or <c>null</c> when GitHub did not say.</summary>
    public DateTimeOffset? ResetsAt { get; }

    /// <summary>Whole minutes until the window reopens; at least one.</summary>
    public int MinutesUntilReset { get; }

    /// <summary>
    /// Builds the user-facing sentence the Repos page renders.
    /// </summary>
    /// <param name="aMinutesUntilReset">Whole minutes until the window reopens.</param>
    /// <returns>A redacted message naming only the wait.</returns>
    public static string BuildMessage(int aMinutesUntilReset) =>
        aMinutesUntilReset == 1
            ? "GitHub rate limit reached — try again in 1 minute"
            : $"GitHub rate limit reached — try again in {aMinutesUntilReset} minutes";
}
