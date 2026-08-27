using System.Net;
using System.Text;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// A recording <see cref="HttpMessageHandler"/> that answers from a scripted table.
/// </summary>
/// <remarks>
/// The tests need to assert on what the client <i>sent</i> — which headers were attached, what the body
/// carried — as much as on what it did with the answer, so every request is captured whole (path,
/// headers and body text) before the canned response is returned.
/// </remarks>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, StubResponse> objScript = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every request the client made, in order.</summary>
    public List<CapturedRequest> Requests { get; } = [];

    /// <summary>
    /// Scripts the answer for one path.
    /// </summary>
    /// <param name="aPath">The path the client will ask for, e.g. <c>/AuthSvc/login</c>.</param>
    /// <param name="aJson">The response body.</param>
    /// <param name="aStatusCode">The status to answer with.</param>
    /// <returns>The same handler, for chaining.</returns>
    public StubHttpMessageHandler Script(string aPath, string aJson, HttpStatusCode aStatusCode = HttpStatusCode.OK)
    {
        objScript[aPath] = new StubResponse(aJson, aStatusCode);
        return this;
    }

    /// <summary>
    /// Returns the first captured request for a path.
    /// </summary>
    /// <param name="aPath">The path to look for.</param>
    /// <returns>The captured request.</returns>
    /// <exception cref="InvalidOperationException">The client never asked for that path.</exception>
    public CapturedRequest RequestFor(string aPath) =>
        Requests.FirstOrDefault(aRequest => string.Equals(aRequest.Path, aPath, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"The client never requested {aPath}.");

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage aRequest,
        CancellationToken aCancellationToken)
    {
        var vPath = aRequest.RequestUri?.AbsolutePath ?? string.Empty;
        var vBody = aRequest.Content is null
            ? string.Empty
            : await aRequest.Content.ReadAsStringAsync(aCancellationToken);

        Requests.Add(new CapturedRequest(
            vPath,
            aRequest.Method.Method,
            vBody,
            aRequest.Headers
                .ToDictionary(aHeader => aHeader.Key, aHeader => string.Join(',', aHeader.Value), StringComparer.OrdinalIgnoreCase),
            aRequest.Headers.Authorization?.Parameter));

        var vScripted = objScript.TryGetValue(vPath, out var vFound)
            ? vFound
            : new StubResponse("""{"success":false,"error":"NOT_SCRIPTED","message":"no stub","statusCode":404}""", HttpStatusCode.NotFound);

        return new HttpResponseMessage(vScripted.StatusCode)
        {
            Content = new StringContent(vScripted.Json, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>One scripted answer.</summary>
    /// <param name="Json">The response body.</param>
    /// <param name="StatusCode">The status to answer with.</param>
    private sealed record StubResponse(string Json, HttpStatusCode StatusCode);
}

/// <summary>One request the client made, captured whole.</summary>
/// <param name="Path">The absolute path asked for.</param>
/// <param name="Method">The HTTP method used.</param>
/// <param name="Body">The request body as text, or empty when there was none.</param>
/// <param name="Headers">The request headers, keyed case-insensitively.</param>
/// <param name="BearerToken">The bearer token, when one was attached.</param>
public sealed record CapturedRequest(
    string Path,
    string Method,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    string? BearerToken);
