using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.GitHub;

/// <summary>
/// The read-only GitHub REST client TfLens reads telemetry through.
/// </summary>
/// <remarks>
/// <para>
/// BRD-16 / REQ-FN-024 — structurally read-only. Every request this class issues is built by the one
/// private <c>SendGetAsync</c> helper, which hard-codes <see cref="HttpMethod.Get"/>; the type exposes
/// no write method to call and holds no write scope. The optional PAT from
/// <see cref="TfLensOptions.GitHubToken"/> is a fine-grained contents-read token whose only effect is
/// to lift the rate limit from 60 requests per hour per IP to 5,000 (Architecture §12).
/// </para>
/// <para>
/// A <c>404</c> on a stream file is a legitimate "stream absent" and answers <c>null</c>, never an
/// exception (BRD-14). An exhausted rate-limit window answers <see cref="GitHubRateLimitException"/>,
/// whose message names only the wait in minutes. Nothing this class logs or throws ever carries the
/// PAT or a URL containing one (Coding Standards, TfLens privacy rule).
/// </para>
/// </remarks>
public sealed class GitHubStreamFetcher : IGitHubStreamFetcher
{
    /// <summary>The GitHub REST base address the typed client defaults to.</summary>
    public const string DefaultBaseAddress = "https://api.github.com/";

    /// <summary>The <c>Accept</c> media type that makes <c>contents</c> answer the file bytes themselves.</summary>
    public const string RawMediaType = "application/vnd.github.raw";

    /// <summary>The <c>Accept</c> media type for the JSON metadata endpoints.</summary>
    public const string JsonMediaType = "application/vnd.github+json";

    private const string UserAgentName = "TfLens";
    private const string UserAgentVersion = "1.0";
    private const string ApiVersionHeader = "X-GitHub-Api-Version";
    private const string ApiVersionValue = "2022-11-28";
    private const string RemainingHeader = "x-ratelimit-remaining";
    private const string ResetHeader = "x-ratelimit-reset";
    private const string RetryAfterHeader = "retry-after";
    private const int DefaultWaitMinutes = 60;

    private static readonly UTF8Encoding RawEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly HttpClient objHttpClient;
    private readonly ILogger<GitHubStreamFetcher> objLogger;

    /// <summary>
    /// Creates the fetcher over a typed <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// The client is configured here rather than in the registration so a test that news up the class
    /// over a stub handler gets exactly the production headers.
    /// </remarks>
    /// <param name="aHttpClient">The typed client; its base address and headers are set if unset.</param>
    /// <param name="aOptions">TfLens configuration, read for the optional PAT.</param>
    /// <param name="aLogger">Logger; it records owners, names, paths, SHAs and status codes only.</param>
    public GitHubStreamFetcher(
        HttpClient aHttpClient,
        IOptions<TfLensOptions> aOptions,
        ILogger<GitHubStreamFetcher> aLogger)
    {
        objHttpClient = aHttpClient;
        objLogger = aLogger;
        Configure(aHttpClient, aOptions.Value);
    }

    /// <summary>
    /// Applies the base address, the required <c>User-Agent</c> and the optional PAT to a client.
    /// </summary>
    /// <remarks>
    /// GitHub rejects a request with no <c>User-Agent</c>, so one is always set. Every header is added
    /// only when absent, which keeps the method safe to call on a client a caller pre-configured.
    /// </remarks>
    /// <param name="aHttpClient">The client to configure.</param>
    /// <param name="aOptions">TfLens configuration, read for <see cref="TfLensOptions.GitHubToken"/>.</param>
    public static void Configure(HttpClient aHttpClient, TfLensOptions aOptions)
    {
        aHttpClient.BaseAddress ??= new Uri(DefaultBaseAddress);

        if (aHttpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            aHttpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(UserAgentName, UserAgentVersion));
        }

        if (!aHttpClient.DefaultRequestHeaders.Contains(ApiVersionHeader))
        {
            aHttpClient.DefaultRequestHeaders.Add(ApiVersionHeader, ApiVersionValue);
        }

        // The PAT raises the rate limit and nothing else; it is never logged and never put in a URL.
        if (!string.IsNullOrWhiteSpace(aOptions.GitHubToken) && aHttpClient.DefaultRequestHeaders.Authorization is null)
        {
            aHttpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", aOptions.GitHubToken);
        }
    }

    /// <inheritdoc />
    public async Task<string?> LatestShaAsync(
        string aOwner,
        string aName,
        string aBranch,
        string aPath,
        CancellationToken aCancellationToken = default)
    {
        var vUrl =
            $"repos/{Uri.EscapeDataString(aOwner)}/{Uri.EscapeDataString(aName)}/commits" +
            $"?sha={Uri.EscapeDataString(aBranch)}&path={Uri.EscapeDataString(aPath)}&per_page=1";

        using var vResponse = await SendGetAsync(vUrl, JsonMediaType, aCancellationToken).ConfigureAwait(false);

        if (vResponse.StatusCode == HttpStatusCode.NotFound)
        {
            objLogger.LogInformation(
                "GitHub has no commits for {Owner}/{Name} on {Branch} touching {Path}", aOwner, aName, aBranch, aPath);
            return null;
        }

        EnsureUsable(vResponse, aOwner, aName);

        var vBody = await vResponse.Content.ReadAsStringAsync(aCancellationToken).ConfigureAwait(false);
        var vSha = ReadFirstSha(vBody);

        objLogger.LogInformation(
            "GitHub latest telemetry SHA for {Owner}/{Name} on {Branch}: {Sha}",
            aOwner,
            aName,
            aBranch,
            vSha ?? "(none)");

        return vSha;
    }

    /// <inheritdoc />
    public async Task<string?> FetchFileAsync(
        string aOwner,
        string aName,
        string aPath,
        string aSha,
        CancellationToken aCancellationToken = default)
    {
        var vUrl =
            $"repos/{Uri.EscapeDataString(aOwner)}/{Uri.EscapeDataString(aName)}/contents/{EncodePath(aPath)}" +
            $"?ref={Uri.EscapeDataString(aSha)}";

        using var vResponse = await SendGetAsync(vUrl, RawMediaType, aCancellationToken).ConfigureAwait(false);

        // BRD-14: a missing stream file is a fact about the repository, not a failure.
        if (vResponse.StatusCode == HttpStatusCode.NotFound)
        {
            objLogger.LogInformation(
                "Stream absent: {Owner}/{Name} has no {Path} at {Sha}", aOwner, aName, aPath, aSha);
            return null;
        }

        EnsureUsable(vResponse, aOwner, aName);

        var vBytes = await vResponse.Content.ReadAsByteArrayAsync(aCancellationToken).ConfigureAwait(false);

        objLogger.LogInformation(
            "Fetched {Bytes} bytes of {Path} from {Owner}/{Name} at {Sha}",
            vBytes.Length,
            aPath,
            aOwner,
            aName,
            aSha);

        return RawEncoding.GetString(vBytes);
    }

    /// <inheritdoc />
    public async Task<GitHubRepoInfo?> GetRepoAsync(
        string aOwner,
        string aName,
        CancellationToken aCancellationToken = default)
    {
        var vUrl = $"repos/{Uri.EscapeDataString(aOwner)}/{Uri.EscapeDataString(aName)}";

        using var vResponse = await SendGetAsync(vUrl, JsonMediaType, aCancellationToken).ConfigureAwait(false);

        if (vResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden
            && !IsRateLimited(vResponse, out _, out _))
        {
            return null;
        }

        EnsureUsable(vResponse, aOwner, aName);

        var vBody = await vResponse.Content.ReadAsStringAsync(aCancellationToken).ConfigureAwait(false);
        return ReadRepoInfo(vBody, aOwner, aName);
    }

    /// <inheritdoc />
    public async Task<bool> PathExistsAsync(
        string aOwner,
        string aName,
        string aPath,
        string aRef,
        CancellationToken aCancellationToken = default)
    {
        var vUrl =
            $"repos/{Uri.EscapeDataString(aOwner)}/{Uri.EscapeDataString(aName)}/contents/{EncodePath(aPath)}" +
            $"?ref={Uri.EscapeDataString(aRef)}";

        using var vResponse = await SendGetAsync(vUrl, JsonMediaType, aCancellationToken).ConfigureAwait(false);

        if (vResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        EnsureUsable(vResponse, aOwner, aName);
        return true;
    }

    /// <summary>
    /// Issues the one and only kind of request this class makes.
    /// </summary>
    /// <remarks>
    /// REQ-FN-024 — every public method funnels through here, and here the verb is a literal
    /// <see cref="HttpMethod.Get"/>. There is no overload taking a method, so no caller can widen it.
    /// </remarks>
    /// <param name="aRelativeUrl">The URL relative to the API base address; it never carries a token.</param>
    /// <param name="aAccept">The <c>Accept</c> media type for this call.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The response, for the caller to inspect and dispose.</returns>
    private async Task<HttpResponseMessage> SendGetAsync(
        string aRelativeUrl,
        string aAccept,
        CancellationToken aCancellationToken)
    {
        using var vRequest = new HttpRequestMessage(HttpMethod.Get, aRelativeUrl);
        vRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(aAccept));

        return await objHttpClient
            .SendAsync(vRequest, HttpCompletionOption.ResponseHeadersRead, aCancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns an unusable response into a redacted exception.
    /// </summary>
    /// <param name="aResponse">The response to inspect.</param>
    /// <param name="aOwner">GitHub owner, for the message.</param>
    /// <param name="aName">GitHub repository name, for the message.</param>
    /// <exception cref="GitHubRateLimitException">The rate-limit window is exhausted.</exception>
    /// <exception cref="HttpRequestException">GitHub answered a non-success status.</exception>
    private void EnsureUsable(HttpResponseMessage aResponse, string aOwner, string aName)
    {
        if (IsRateLimited(aResponse, out var vResetsAt, out var vMinutes))
        {
            objLogger.LogWarning(
                "GitHub rate limit exhausted on {Owner}/{Name}; window reopens in {Minutes} minutes",
                aOwner,
                aName,
                vMinutes);

            throw new GitHubRateLimitException((int)aResponse.StatusCode, vResetsAt, vMinutes);
        }

        if (aResponse.IsSuccessStatusCode)
        {
            return;
        }

        // The message names the status and the repository only — never a URL, never the PAT.
        throw new HttpRequestException(
            $"GitHub answered {(int)aResponse.StatusCode} for {aOwner}/{aName}.",
            inner: null,
            statusCode: aResponse.StatusCode);
    }

    /// <summary>
    /// Tells whether a response is GitHub's exhausted-rate-limit answer, and when the window reopens.
    /// </summary>
    /// <param name="aResponse">The response to inspect.</param>
    /// <param name="aResetsAt">Receives the reset instant when GitHub supplied one.</param>
    /// <param name="aMinutesUntilReset">Receives the whole minutes to wait, at least one.</param>
    /// <returns><c>true</c> when the caller must wait rather than retry.</returns>
    private static bool IsRateLimited(
        HttpResponseMessage aResponse,
        out DateTimeOffset? aResetsAt,
        out int aMinutesUntilReset)
    {
        aResetsAt = null;
        aMinutesUntilReset = 0;

        if (aResponse.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return false;
        }

        var vRemaining = ReadHeader(aResponse, RemainingHeader);
        var vRetryAfter = ReadHeader(aResponse, RetryAfterHeader);

        if (vRemaining != "0" && vRetryAfter is null)
        {
            return false;
        }

        var vReset = ReadHeader(aResponse, ResetHeader);

        if (vReset is not null && long.TryParse(vReset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vEpoch))
        {
            aResetsAt = DateTimeOffset.FromUnixTimeSeconds(vEpoch);
            aMinutesUntilReset = ToWholeMinutes(aResetsAt.Value - DateTimeOffset.UtcNow);
            return true;
        }

        if (vRetryAfter is not null
            && int.TryParse(vRetryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vSeconds))
        {
            aResetsAt = DateTimeOffset.UtcNow.AddSeconds(vSeconds);
            aMinutesUntilReset = ToWholeMinutes(TimeSpan.FromSeconds(vSeconds));
            return true;
        }

        aMinutesUntilReset = DefaultWaitMinutes;
        return true;
    }

    /// <summary>Rounds a wait up to whole minutes, never below one.</summary>
    /// <param name="aWait">The remaining wait.</param>
    /// <returns>Whole minutes to report to the user.</returns>
    private static int ToWholeMinutes(TimeSpan aWait) => Math.Max(1, (int)Math.Ceiling(aWait.TotalMinutes));

    /// <summary>Reads one response header's first value.</summary>
    /// <param name="aResponse">The response.</param>
    /// <param name="aName">The header name, case-insensitively.</param>
    /// <returns>The first value, or <c>null</c> when the header is absent.</returns>
    private static string? ReadHeader(HttpResponseMessage aResponse, string aName) =>
        aResponse.Headers.TryGetValues(aName, out var vValues) ? vValues.FirstOrDefault() : null;

    /// <summary>Reads the <c>sha</c> of the first element of a commits response.</summary>
    /// <param name="aBody">The JSON array GitHub answered.</param>
    /// <returns>The SHA, or <c>null</c> when the array is empty or malformed.</returns>
    private static string? ReadFirstSha(string aBody)
    {
        using var vDocument = JsonDocument.Parse(aBody);

        if (vDocument.RootElement.ValueKind != JsonValueKind.Array || vDocument.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        return vDocument.RootElement[0].TryGetProperty("sha", out var vSha) && vSha.ValueKind == JsonValueKind.String
            ? vSha.GetString()
            : null;
    }

    /// <summary>Reads the repository metadata TfLens needs at connect time.</summary>
    /// <param name="aBody">The JSON object GitHub answered.</param>
    /// <param name="aOwner">The owner asked for, used when the payload omits it.</param>
    /// <param name="aName">The name asked for, used when the payload omits it.</param>
    /// <returns>The metadata.</returns>
    private static GitHubRepoInfo ReadRepoInfo(string aBody, string aOwner, string aName)
    {
        using var vDocument = JsonDocument.Parse(aBody);
        var vRoot = vDocument.RootElement;

        var vOwner = vRoot.TryGetProperty("owner", out var vOwnerElement)
            && vOwnerElement.TryGetProperty("login", out var vLogin)
            && vLogin.ValueKind == JsonValueKind.String
                ? vLogin.GetString() ?? aOwner
                : aOwner;

        var vName = vRoot.TryGetProperty("name", out var vNameElement) && vNameElement.ValueKind == JsonValueKind.String
            ? vNameElement.GetString() ?? aName
            : aName;

        var vIsPrivate = vRoot.TryGetProperty("private", out var vPrivate)
            && vPrivate.ValueKind == JsonValueKind.True;

        var vDefaultBranch = vRoot.TryGetProperty("default_branch", out var vBranch)
            && vBranch.ValueKind == JsonValueKind.String
                ? vBranch.GetString() ?? "main"
                : "main";

        return new GitHubRepoInfo(vOwner, vName, vIsPrivate, vDefaultBranch);
    }

    /// <summary>Percent-encodes a repository-relative path while keeping its separators.</summary>
    /// <param name="aPath">The repository-relative path.</param>
    /// <returns>The encoded path, safe to interpolate into a URL.</returns>
    private static string EncodePath(string aPath) =>
        string.Join('/', aPath.Split('/').Select(Uri.EscapeDataString));
}
