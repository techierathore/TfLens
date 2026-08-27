using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// A handler that simulates the server rotating its RSA key between the key fetch and the login.
/// </summary>
/// <remarks>
/// The first login is answered <c>DECRYPTION_FAILED</c> — exactly what AppManager returns when the
/// ciphertext was produced with a key it no longer holds — and the key endpoint then publishes the new
/// key. The client is expected to notice, refetch once, and succeed on the retry rather than either
/// giving up or looping.
/// </remarks>
public sealed class RotatingKeyHandler : HttpMessageHandler
{
    private readonly RSA objStaleKey;
    private readonly RSA objFreshKey = RSA.Create(2048);

    private bool objHasRotated;

    /// <summary>
    /// Creates the handler.
    /// </summary>
    /// <param name="aStaleKey">The key published before the rotation.</param>
    public RotatingKeyHandler(RSA aStaleKey)
    {
        objStaleKey = aStaleKey;
    }

    /// <summary>How many times the client asked for the public key.</summary>
    public int PublicKeyRequests { get; private set; }

    /// <summary>How many times the client posted a sign-in.</summary>
    public int LoginRequests { get; private set; }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage aRequest,
        CancellationToken aCancellationToken)
    {
        var vPath = aRequest.RequestUri?.AbsolutePath ?? string.Empty;

        if (vPath == "/AuthSvc/public-key")
        {
            PublicKeyRequests++;
            var vKey = objHasRotated ? objFreshKey : objStaleKey;
            return Task.FromResult(Json(AppManagerClientTests.PublicKeyJson(vKey), HttpStatusCode.OK));
        }

        LoginRequests++;

        if (!objHasRotated)
        {
            objHasRotated = true;
            return Task.FromResult(Json(
                """{"success":false,"error":"DECRYPTION_FAILED","message":"stale key","statusCode":400}""",
                HttpStatusCode.BadRequest));
        }

        return Task.FromResult(Json(AppManagerClientTests.SuccessLoginJson(), HttpStatusCode.OK));
    }

    /// <inheritdoc />
    protected override void Dispose(bool aDisposing)
    {
        if (aDisposing)
        {
            objFreshKey.Dispose();
        }

        base.Dispose(aDisposing);
    }

    /// <summary>
    /// Wraps a JSON body in a response.
    /// </summary>
    /// <param name="aJson">The body text.</param>
    /// <param name="aStatusCode">The status to answer with.</param>
    /// <returns>The response message.</returns>
    private static HttpResponseMessage Json(string aJson, HttpStatusCode aStatusCode) =>
        new(aStatusCode) { Content = new StringContent(aJson, Encoding.UTF8, "application/json") };
}
