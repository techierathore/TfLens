using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using TfLens.Core.AppManager;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// REQ-FN-003 / BRD-92 — the client half of the password-reset round trip.
/// </summary>
/// <remarks>
/// <para>
/// Three claims are proved here, none of which needs a live AppManager. First, that
/// <c>ForgotPasswordAsync</c> is enumeration-safe by construction: whatever AppManager answers, the
/// caller observes one outcome and the bytes on the wire differ only in the address that was typed.
/// Second, that <c>INVALID_RESET_TOKEN</c> and <c>APP_ID_MISMATCH</c> arrive as codes the caller can
/// collapse onto a single "invalid or expired" outcome. Third, that a reset token never reaches a log
/// on any path — including the failure and retry paths, which are the ones that log at all.
/// </para>
/// <para>
/// The AppManager answers are stubbed on purpose. The mapping under test is TfLens's, not the
/// server's, and a stub is the only way to exercise <c>APP_ID_MISMATCH</c> at all: producing one live
/// needs a reset token minted for a different tenant, which no client can obtain.
/// </para>
/// </remarks>
public sealed class PasswordResetTests : IDisposable
{
    /// <summary>The reset path under test.</summary>
    private const string ForgotPath = "/AuthSvc/forgot-password";

    /// <summary>The completion path under test.</summary>
    private const string ResetPath = "/AuthSvc/reset-password";

    /// <summary>The public-key path every encrypting call fetches first.</summary>
    private const string PublicKeyPath = "/AuthSvc/public-key";

    /// <summary>The documented demo account — an address that exists.</summary>
    private const string KnownEmail = "tflensdemo@techierathore.com";

    /// <summary>An address that does not exist, and must be indistinguishable from one that does.</summary>
    private const string UnknownEmail = "nobody.at.all@techierathore.invalid";

    /// <summary>A reset token distinctive enough that any leak into a log is unmistakable.</summary>
    private const string SecretResetToken = "rst-CANARY-9f3ac71e-do-not-log";

    /// <summary>A password that satisfies AppManager's complexity rules.</summary>
    private const string NewPassword = "TfLensReset!23";

    /// <summary>Envelope for an accepted request.</summary>
    private const string AcceptedJson = """{"success":true,"data":null,"message":"ok"}""";

    private readonly RSA objServerKey = RSA.Create(2048);

    /// <inheritdoc />
    public void Dispose() => objServerKey.Dispose();

    /// <summary>
    /// Whatever AppManager answers, the caller of <c>ForgotPasswordAsync</c> observes the same nothing.
    /// </summary>
    /// <param name="aCode">The AppManager error code to answer with, or empty for an accepted request.</param>
    /// <param name="aStatus">The HTTP status to answer with.</param>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// This is the structural half of enumeration safety. A method that returns <c>void</c> and cannot
    /// throw has no channel through which "this address exists" could travel — not a return value, not
    /// an exception type, not a message.
    /// </remarks>
    [Theory]
    [InlineData("", 200)]
    [InlineData("USER_NOT_FOUND", 404)]
    [InlineData("APPLICATION_ID_REQUIRED", 400)]
    [InlineData("INVALID_API_KEY", 401)]
    [InlineData("VALIDATION_ERROR", 400)]
    [InlineData("INTERNAL_ERROR", 500)]
    public async Task ForgotPasswordSwallowsEveryAnswerAppManagerCanGive(string aCode, int aStatus)
    {
        var vHandler = ScriptedHandler().Script(ForgotPath, EnvelopeFor(aCode, aStatus), (HttpStatusCode)aStatus);

        var vAct = async () => await BuildClient(vHandler).ForgotPasswordAsync(KnownEmail);

        await vAct.Should().NotThrowAsync();
    }

    /// <summary>A forgot-password call that never reaches AppManager is still silent at the caller.</summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// A transport failure is the one answer that is not an envelope, and it is exactly the answer a
    /// naive implementation lets escape — turning "the network blipped" into a signal about the address.
    /// </remarks>
    [Fact]
    public async Task ForgotPasswordSwallowsATransportFailure()
    {
        var vAct = async () => await BuildClient(new UnreachableHandler()).ForgotPasswordAsync(KnownEmail);

        await vAct.Should().NotThrowAsync();
    }

    /// <summary>
    /// The request TfLens sends is byte-identical for a known and an unknown address but for the address.
    /// </summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// The observable outcome being equal is only half the guarantee: if the two calls took different
    /// shapes on the wire — a different path, an extra header, a differently ordered body — an observer
    /// on the network would learn what the caller was not told. Substituting the address into the
    /// unknown-address body must reproduce the known-address body exactly, which also pins the field
    /// order and the <c>applicationId</c> the endpoint is scoped by.
    /// </remarks>
    [Fact]
    public async Task ForgotPasswordSendsTheSameRequestForAKnownAndAnUnknownAddress()
    {
        var vKnownHandler = ScriptedHandler().Script(ForgotPath, AcceptedJson);
        var vUnknownHandler = ScriptedHandler().Script(
            ForgotPath,
            EnvelopeFor("USER_NOT_FOUND", 404),
            HttpStatusCode.NotFound);

        await BuildClient(vKnownHandler).ForgotPasswordAsync(KnownEmail);
        await BuildClient(vUnknownHandler).ForgotPasswordAsync(UnknownEmail);

        var vKnown = vKnownHandler.RequestFor(ForgotPath);
        var vUnknown = vUnknownHandler.RequestFor(ForgotPath);

        vUnknown.Path.Should().Be(vKnown.Path);
        vUnknown.Method.Should().Be(vKnown.Method);
        vUnknown.Headers.Should().BeEquivalentTo(vKnown.Headers);
        vUnknown.Body.Replace(UnknownEmail, KnownEmail, StringComparison.Ordinal).Should().Be(vKnown.Body);
        vKnown.Body.Should().Contain("\"applicationId\":1");
    }

    /// <summary>Neither address produces a request the other does not, so no extra call leaks the answer.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ForgotPasswordMakesTheSameCallsForAKnownAndAnUnknownAddress()
    {
        var vKnownHandler = ScriptedHandler().Script(ForgotPath, AcceptedJson);
        var vUnknownHandler = ScriptedHandler().Script(
            ForgotPath,
            EnvelopeFor("USER_NOT_FOUND", 404),
            HttpStatusCode.NotFound);

        await BuildClient(vKnownHandler).ForgotPasswordAsync(KnownEmail);
        await BuildClient(vUnknownHandler).ForgotPasswordAsync(UnknownEmail);

        vUnknownHandler.Requests.Select(aRequest => aRequest.Path)
            .Should().Equal(vKnownHandler.Requests.Select(aRequest => aRequest.Path));
    }

    /// <summary>Nothing the client logs about a forgot-password call says whether the address existed.</summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// The failure branch logs deliberately — swallowing an error without a trace would make a genuine
    /// outage invisible — so what matters is that the line carries a code and a status and never the
    /// address that was typed.
    /// </remarks>
    [Fact]
    public async Task ForgotPasswordNeverLogsTheAddressItWasGiven()
    {
        var vLogger = new CapturingLogger<AppManagerClient>();
        var vHandler = ScriptedHandler().Script(
            ForgotPath,
            EnvelopeFor("USER_NOT_FOUND", 404),
            HttpStatusCode.NotFound);

        await BuildClient(vHandler, aLogger: vLogger).ForgotPasswordAsync(UnknownEmail);

        vLogger.Everything.Should().NotContain(UnknownEmail);
    }

    /// <summary>
    /// <c>INVALID_RESET_TOKEN</c> and <c>APP_ID_MISMATCH</c> reach the caller as the two codes the UI
    /// collapses, and as nothing else.
    /// </summary>
    /// <param name="aCode">The AppManager code answered.</param>
    /// <param name="aStatus">The HTTP status answered.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("INVALID_RESET_TOKEN", 400)]
    [InlineData("APP_ID_MISMATCH", 400)]
    public async Task ResetPasswordSurfacesBothDeadLinkCodesAsThemselves(string aCode, int aStatus)
    {
        var vHandler = ScriptedHandler().Script(ResetPath, EnvelopeFor(aCode, aStatus), (HttpStatusCode)aStatus);

        var vAct = async () => await BuildClient(vHandler).ResetPasswordAsync(SecretResetToken, NewPassword);

        var vThrown = await vAct.Should().ThrowAsync<AppManagerException>();
        vThrown.Which.Code.Should().Be(aCode);
        vThrown.Which.StatusCode.Should().Be(aStatus);
    }

    /// <summary>
    /// The two dead-link codes are indistinguishable once mapped: same outcome, same message, same
    /// status — only the code the log records differs.
    /// </summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// This is the acceptance clause stated directly. The mapping lives in the head's endpoint and in
    /// the page, and both consult exactly this pair; proving here that the pair is jointly recognised
    /// and identically shaped is what makes the single "invalid or expired" outcome inevitable rather
    /// than a coincidence of two independently written <c>switch</c> arms.
    /// </remarks>
    [Fact]
    public async Task BothDeadLinkCodesProduceIdenticallyShapedFailures()
    {
        var vInvalid = await CaptureResetFailureAsync(AppManagerException.Codes.InvalidResetToken);
        var vMismatch = await CaptureResetFailureAsync(AppManagerException.Codes.AppIdMismatch);

        vMismatch.GetType().Should().Be(vInvalid.GetType());
        vMismatch.StatusCode.Should().Be(vInvalid.StatusCode);
        vMismatch.Message.Should().Be(vInvalid.Message);

        new[] { vInvalid.Code, vMismatch.Code }
            .Should().BeEquivalentTo([
                AppManagerException.Codes.InvalidResetToken,
                AppManagerException.Codes.AppIdMismatch
            ]);
    }

    /// <summary>The reset token reaches AppManager in the body and nowhere else.</summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// A token in a path or a query string is recorded by every proxy and access log between here and
    /// the server, which is a leak no amount of care inside the process can undo.
    /// </remarks>
    [Fact]
    public async Task ResetPasswordPutsTheTokenInTheBodyAndNotInTheUrl()
    {
        var vHandler = ScriptedHandler().Script(ResetPath, AcceptedJson);

        await BuildClient(vHandler).ResetPasswordAsync(SecretResetToken, NewPassword);

        var vRequest = vHandler.RequestFor(ResetPath);
        vRequest.Path.Should().Be(ResetPath).And.NotContain(SecretResetToken);
        AppManagerClientTests.BodyField(vRequest.Body, "token").Should().Be(SecretResetToken);
        vRequest.Headers.Values.Should().OnlyContain(aValue => !aValue.Contains(SecretResetToken));
    }

    /// <summary>The new password is encrypted, so the reset never carries a plaintext password either.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ResetPasswordEncryptsTheNewPassword()
    {
        var vHandler = ScriptedHandler().Script(ResetPath, AcceptedJson);

        await BuildClient(vHandler).ResetPasswordAsync(SecretResetToken, NewPassword);

        var vBody = vHandler.RequestFor(ResetPath).Body;
        vBody.Should().NotContain(NewPassword);

        var vCipher = Convert.FromBase64String(AppManagerClientTests.BodyField(vBody, "encryptedNewPassword"));
        Encoding.UTF8.GetString(objServerKey.Decrypt(vCipher, RSAEncryptionPadding.OaepSHA256))
            .Should().Be(NewPassword);
    }

    /// <summary>
    /// No path through a password reset writes the token to a log — success, either dead-link code, a
    /// rotated key, or a transport failure.
    /// </summary>
    /// <param name="aCode">The AppManager code answered, or empty for an accepted reset.</param>
    /// <param name="aStatus">The HTTP status answered.</param>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// The failure branches are the ones that log, and the rotated-key branch logs and then rebuilds the
    /// body a second time, so it is the path most likely to carry the token somewhere new. Every capture
    /// includes the structured state and the full exception text, not only the rendered line.
    /// </remarks>
    [Theory]
    [InlineData("", 200)]
    [InlineData("INVALID_RESET_TOKEN", 400)]
    [InlineData("APP_ID_MISMATCH", 400)]
    [InlineData("VALIDATION_ERROR", 400)]
    [InlineData("INTERNAL_ERROR", 500)]
    public async Task NoResetPathEverLogsTheToken(string aCode, int aStatus)
    {
        var vLogger = new CapturingLogger<AppManagerClient>();
        var vHandler = ScriptedHandler().Script(ResetPath, EnvelopeFor(aCode, aStatus), (HttpStatusCode)aStatus);

        try
        {
            await BuildClient(vHandler, aLogger: vLogger).ResetPasswordAsync(SecretResetToken, NewPassword);
        }
        catch (AppManagerException)
        {
            // The refusal itself is the subject of other tests; here only the log matters.
        }

        vLogger.Everything.Should().NotContain(SecretResetToken);
        vLogger.Everything.Should().NotContain(NewPassword);
    }

    /// <summary>A rotated server key logs and retries, and still never writes the token.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARotatedKeyRetryNeverLogsTheToken()
    {
        var vLogger = new CapturingLogger<AppManagerClient>();
        using var vHandler = new RotatingKeyHandler(objServerKey);

        await BuildClient(vHandler, aLogger: vLogger).ResetPasswordAsync(SecretResetToken, NewPassword);

        vHandler.PublicKeyRequests.Should().Be(2, "the retry path must refetch the rotated key");
        vLogger.Everything.Should().Contain(
            "DECRYPTION_FAILED",
            "swallowing a rotation silently would hide a real outage");
        vLogger.Everything.Should().NotContain(SecretResetToken);
    }

    /// <summary>A transport failure during a reset never writes the token either.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ATransportFailureNeverLogsTheToken()
    {
        var vLogger = new CapturingLogger<AppManagerClient>();

        var vAct = async () =>
            await BuildClient(new UnreachableHandler(), aLogger: vLogger)
                .ResetPasswordAsync(SecretResetToken, NewPassword);

        await vAct.Should().ThrowAsync<AppManagerException>();
        vLogger.Everything.Should().NotContain(SecretResetToken);
    }

    /// <summary>
    /// Drives one refused reset and returns the exception it produced.
    /// </summary>
    /// <param name="aCode">The AppManager code to answer with.</param>
    /// <returns>The exception the client threw.</returns>
    private async Task<AppManagerException> CaptureResetFailureAsync(string aCode)
    {
        var vHandler = ScriptedHandler().Script(ResetPath, EnvelopeFor(aCode, 400), HttpStatusCode.BadRequest);

        try
        {
            await BuildClient(vHandler).ResetPasswordAsync(SecretResetToken, NewPassword);
        }
        catch (AppManagerException vException)
        {
            return vException;
        }

        throw new InvalidOperationException($"The client accepted a reset AppManager refused with {aCode}.");
    }

    /// <summary>
    /// Renders the envelope AppManager answers with for a code.
    /// </summary>
    /// <param name="aCode">The error code, or empty for an accepted request.</param>
    /// <param name="aStatus">The HTTP status.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// The message is deliberately the same string for every code: it is what makes "identically
    /// shaped failures" a statement about the mapping rather than about AppManager's prose.
    /// </remarks>
    private static string EnvelopeFor(string aCode, int aStatus) => aCode.Length == 0
        ? AcceptedJson
        : JsonSerializer.Serialize(new
        {
            success = false,
            error = aCode,
            message = "rejected",
            statusCode = aStatus
        });

    /// <summary>
    /// Builds a client over a stub handler.
    /// </summary>
    /// <param name="aHandler">The handler answering the calls.</param>
    /// <param name="aOptions">Configuration to use, or the credential-free default.</param>
    /// <param name="aLogger">The logger to capture with, or a silent one.</param>
    /// <returns>The client under test.</returns>
    private AppManagerClient BuildClient(
        HttpMessageHandler aHandler,
        TfLensOptions? aOptions = null,
        CapturingLogger<AppManagerClient>? aLogger = null)
    {
        var vOptions = aOptions ?? new TfLensOptions { AppManagerBaseUrl = "https://appmanager.invalid" };
        var vHttpClient = new HttpClient(aHandler) { BaseAddress = new Uri(vOptions.AppManagerBaseUrl) };

        return new AppManagerClient(
            vHttpClient,
            Options.Create(vOptions),
            aLogger ?? new CapturingLogger<AppManagerClient>());
    }

    /// <summary>
    /// Produces a handler already scripted with this test's public key.
    /// </summary>
    /// <returns>The handler, ready for further scripting.</returns>
    private StubHttpMessageHandler ScriptedHandler() =>
        new StubHttpMessageHandler().Script(PublicKeyPath, AppManagerClientTests.PublicKeyJson(objServerKey));

    /// <summary>A handler that fails the way an unreachable AppManager does.</summary>
    private sealed class UnreachableHandler : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage aRequest,
            CancellationToken aCancellationToken) =>
            throw new HttpRequestException("AppManager is unreachable.");
    }
}
