using System.Net;
using System.Net.Http.Headers;

namespace TfLens.Core.Tests.GitHub;

/// <summary>
/// A stub transport that records every request and refuses anything that is not a <c>GET</c>.
/// </summary>
/// <remarks>
/// This is the structural proof behind REQ-FN-024: the fetcher is driven through every one of its
/// public methods over this handler, and the handler throws the moment a write verb appears. It also
/// keeps the request log so a test can assert what was — and was not — called.
/// </remarks>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> objResponder;

    /// <summary>
    /// Creates the handler.
    /// </summary>
    /// <param name="aResponder">Produces the response for each recorded request.</param>
    public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> aResponder)
    {
        objResponder = aResponder;
    }

    /// <summary>Every request the fetcher issued, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The absolute URIs of every request, in order.</summary>
    public IReadOnlyList<string> Urls => Requests.Select(aR => aR.RequestUri!.ToString()).ToList();

    /// <summary>
    /// Builds a JSON response.
    /// </summary>
    /// <param name="aStatusCode">The status to answer with.</param>
    /// <param name="aBody">The body text.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Json(HttpStatusCode aStatusCode, string aBody) =>
        new(aStatusCode) { Content = new StringContent(aBody) };

    /// <summary>
    /// Builds a response carrying raw bytes, exactly as the <c>contents</c> endpoint answers.
    /// </summary>
    /// <param name="aBytes">The bytes to answer with.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Raw(byte[] aBytes)
    {
        var vResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(aBytes) };
        vResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return vResponse;
    }

    /// <summary>
    /// Builds GitHub's exhausted-rate-limit answer.
    /// </summary>
    /// <param name="aStatusCode">403 for the primary limit, 429 for the secondary one.</param>
    /// <param name="aResetsAt">When the window reopens.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage RateLimited(HttpStatusCode aStatusCode, DateTimeOffset aResetsAt)
    {
        var vResponse = new HttpResponseMessage(aStatusCode) { Content = new StringContent("{}") };
        vResponse.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
        vResponse.Headers.TryAddWithoutValidation("x-ratelimit-reset", aResetsAt.ToUnixTimeSeconds().ToString());
        return vResponse;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage aRequest,
        CancellationToken aCancellationToken)
    {
        if (aRequest.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException(
                $"REQ-FN-024 violated: the GitHub client issued {aRequest.Method} {aRequest.RequestUri}.");
        }

        Requests.Add(aRequest);
        return Task.FromResult(objResponder(aRequest));
    }
}
