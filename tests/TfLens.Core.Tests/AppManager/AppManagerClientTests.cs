using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core.AppManager;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// Covers the three invariants every AppManager call depends on — the cached public key, the RSA
/// padding, and the whole-or-nothing API-key header rule — plus the documented error-code mapping.
/// </summary>
public sealed class AppManagerClientTests : IDisposable
{
    private const string LoginPath = "/AuthSvc/login";
    private const string PublicKeyPath = "/AuthSvc/public-key";
    private const string DemoEmail = "tflensdemo@techierathore.com";
    private const string DemoPassword = "TfLensDemo!23";

    private readonly RSA objServerKey = RSA.Create(2048);

    /// <inheritdoc />
    public void Dispose() => objServerKey.Dispose();

    /// <summary>The public key is fetched once and reused across calls until it is invalidated.</summary>
    [Fact]
    public async Task PublicKeyIsFetchedOnce()
    {
        var vHandler = ScriptedHandler().Script(LoginPath, SuccessLoginJson());
        var vClient = BuildClient(vHandler);

        await vClient.LoginAsync(DemoEmail, DemoPassword);
        await vClient.LoginAsync(DemoEmail, DemoPassword);

        vHandler.Requests.Count(aRequest => aRequest.Path == PublicKeyPath).Should().Be(1);
    }

    /// <summary>The password field is RSA-OAEP-SHA256 ciphertext, not any other padding.</summary>
    [Fact]
    public async Task PasswordIsEncryptedWithOaepSha256()
    {
        var vHandler = ScriptedHandler().Script(LoginPath, SuccessLoginJson());
        var vClient = BuildClient(vHandler);

        await vClient.LoginAsync(DemoEmail, DemoPassword);

        var vCipher = Convert.FromBase64String(BodyField(vHandler.RequestFor(LoginPath).Body, "encryptedPassword"));
        var vPlain = objServerKey.Decrypt(vCipher, RSAEncryptionPadding.OaepSHA256);

        Encoding.UTF8.GetString(vPlain).Should().Be(DemoPassword);
    }

    /// <summary>Any padding other than OAEP-SHA256 fails to decrypt what the client sent.</summary>
    [Fact]
    public async Task PasswordIsNotEncryptedWithAnyOtherPadding()
    {
        var vHandler = ScriptedHandler().Script(LoginPath, SuccessLoginJson());
        var vClient = BuildClient(vHandler);

        await vClient.LoginAsync(DemoEmail, DemoPassword);

        var vCipher = Convert.FromBase64String(BodyField(vHandler.RequestFor(LoginPath).Body, "encryptedPassword"));
        var vWrongPadding = () => objServerKey.Decrypt(vCipher, RSAEncryptionPadding.OaepSHA1);

        vWrongPadding.Should().Throw<CryptographicException>();
    }

    /// <summary>The plaintext password never appears anywhere in the request body.</summary>
    [Fact]
    public async Task PlaintextPasswordNeverLeavesTheProcess()
    {
        var vHandler = ScriptedHandler().Script(LoginPath, SuccessLoginJson());
        var vClient = BuildClient(vHandler);

        await vClient.LoginAsync(DemoEmail, DemoPassword);

        vHandler.Requests.Should().OnlyContain(aRequest => !aRequest.Body.Contains(DemoPassword));
    }

    /// <summary>With no API-key pair configured, neither header is attached to any request.</summary>
    [Fact]
    public async Task ApiKeyHeadersAreAbsentWhenUnconfigured()
    {
        var vHandler = ScriptedHandler().Script(LoginPath, SuccessLoginJson());
        var vClient = BuildClient(vHandler);

        await vClient.LoginAsync(DemoEmail, DemoPassword);

        vHandler.Requests.Should().OnlyContain(aRequest =>
            !aRequest.Headers.ContainsKey("X-Api-Key") && !aRequest.Headers.ContainsKey("X-Api-Secret"));
    }

    /// <summary>With a whole API-key pair configured, both headers are attached together.</summary>
    [Fact]
    public async Task ApiKeyHeadersAreSentWhenConfigured()
    {
        var vHandler = ScriptedHandler().Script(LoginPath, SuccessLoginJson());
        var vOptions = NewOptions();
        vOptions.AppManagerApiKey = "ak-live-test";
        vOptions.AppManagerApiSecret = "secret-test";

        await BuildClient(vHandler, vOptions).LoginAsync(DemoEmail, DemoPassword);

        var vRequest = vHandler.RequestFor(LoginPath);
        vRequest.Headers["X-Api-Key"].Should().Be("ak-live-test");
        vRequest.Headers["X-Api-Secret"].Should().Be("secret-test");
    }

    /// <summary>Half a pair is never sent, because the client asks the options, not the fields.</summary>
    [Fact]
    public async Task HalfAnApiKeyPairIsNeverSent()
    {
        var vHandler = ScriptedHandler().Script(LoginPath, SuccessLoginJson());
        var vOptions = NewOptions();
        vOptions.AppManagerApiKey = "ak-live-test";

        await BuildClient(vHandler, vOptions).LoginAsync(DemoEmail, DemoPassword);

        vHandler.RequestFor(LoginPath).Headers.Should().NotContainKey("X-Api-Key");
    }

    /// <summary>Every request body carries the application id, which is how the app resolves without a key.</summary>
    [Fact]
    public async Task ApplicationIdIsInEveryBody()
    {
        var vHandler = ScriptedHandler()
            .Script(LoginPath, SuccessLoginJson())
            .Script("/AuthSvc/refresh", RefreshJson())
            .Script("/AuthSvc/validate", """{"success":true,"data":{"isValid":true}}""")
            .Script("/AuthSvc/logout", """{"success":true,"data":null}""");

        var vClient = BuildClient(vHandler);
        await vClient.LoginAsync(DemoEmail, DemoPassword);
        await vClient.RefreshAsync("rt-1");
        await vClient.ValidateAsync("at-1");
        await vClient.LogoutAsync("rt-1", "at-1");

        vHandler.Requests
            .Where(aRequest => aRequest.Method == "POST")
            .Should().OnlyContain(aRequest => aRequest.Body.Contains("\"applicationId\":1"));
    }

    /// <summary>Registration always asks for the Manager role and never offers a caller override.</summary>
    [Fact]
    public async Task RegisterAlwaysAsksForManagerRole()
    {
        var vHandler = ScriptedHandler().Script("/AuthSvc/register", SuccessLoginJson());
        var vClient = BuildClient(vHandler);

        await vClient.RegisterAsync(new RegisterRequest(DemoEmail, DemoPassword, "TfLens", "Demo"));

        BodyField(vHandler.RequestFor("/AuthSvc/register").Body, "applicationRoleCode").Should().Be("Manager");
    }

    /// <summary>A password failing a complexity rule is refused before any HTTP call is made.</summary>
    [Theory]
    [InlineData("short1!")]
    [InlineData("nouppercase1!")]
    [InlineData("NoDigitHere!")]
    [InlineData("NoSpecial123")]
    public async Task RegisterRejectsWeakPasswordBeforeTheCall(string aPassword)
    {
        var vHandler = ScriptedHandler().Script("/AuthSvc/register", SuccessLoginJson());
        var vClient = BuildClient(vHandler);

        var vAct = async () => await vClient.RegisterAsync(new RegisterRequest(DemoEmail, aPassword, "A", "B"));

        (await vAct.Should().ThrowAsync<AppManagerException>())
            .Which.Code.Should().Be(AppManagerException.Codes.ValidationError);
        vHandler.Requests.Should().NotContain(aRequest => aRequest.Path == "/AuthSvc/register");
    }

    /// <summary>Each documented error code arrives as its own value on the typed exception.</summary>
    [Theory]
    [InlineData("INVALID_CREDENTIALS", 401)]
    [InlineData("ACCOUNT_LOCKED", 423)]
    [InlineData("ACCOUNT_DISABLED", 403)]
    [InlineData("NO_APP_ACCESS", 403)]
    [InlineData("EXPIRED_REFRESH_TOKEN", 401)]
    public async Task ErrorCodesMapToDistinctTypedErrors(string aCode, int aStatus)
    {
        var vHandler = ScriptedHandler().Script(
            LoginPath,
            $$"""{"success":false,"error":"{{aCode}}","message":"rejected","statusCode":{{aStatus}}}""",
            (HttpStatusCode)aStatus);

        var vAct = async () => await BuildClient(vHandler).LoginAsync(DemoEmail, DemoPassword);

        var vThrown = await vAct.Should().ThrowAsync<AppManagerException>();
        vThrown.Which.Code.Should().Be(aCode);
        vThrown.Which.StatusCode.Should().Be(aStatus);
    }

    /// <summary>A body with no readable JSON still produces a typed error rather than an HTTP exception.</summary>
    [Fact]
    public async Task EmptyErrorBodyStillProducesTypedError()
    {
        var vHandler = ScriptedHandler().Script(LoginPath, string.Empty, HttpStatusCode.Unauthorized);

        var vAct = async () => await BuildClient(vHandler).LoginAsync(DemoEmail, DemoPassword);

        (await vAct.Should().ThrowAsync<AppManagerException>()).Which.Code.Should().Be("HTTP401");
    }

    /// <summary>A rotated server key is detected once, refetched, and the call retried exactly once.</summary>
    [Fact]
    public async Task DecryptionFailureRefetchesTheKeyAndRetriesOnce()
    {
        var vHandler = new RotatingKeyHandler(objServerKey);
        var vClient = BuildClient(vHandler);

        var vResult = await vClient.LoginAsync(DemoEmail, DemoPassword);

        vResult.UserId.Should().Be(2);
        vHandler.PublicKeyRequests.Should().Be(2);
        vHandler.LoginRequests.Should().Be(2);
    }

    /// <summary>A refresh recovers the identity claims from the reissued token, since the body omits them.</summary>
    [Fact]
    public async Task RefreshRecoversIdentityFromTheToken()
    {
        var vHandler = ScriptedHandler().Script("/AuthSvc/refresh", RefreshJson());

        var vResult = await BuildClient(vHandler).RefreshAsync("rt-1");

        vResult.UserId.Should().Be(2);
        vResult.Email.Should().Be(DemoEmail);
        vResult.RefreshToken.Should().Be("rt-rotated");
        vResult.ApplicationRole.Should().Be("Manager");
    }

    /// <summary>A rejected access token answers false rather than throwing at the caller.</summary>
    [Fact]
    public async Task ValidateReturnsFalseWhenRejected()
    {
        var vHandler = ScriptedHandler().Script(
            "/AuthSvc/validate",
            """{"success":false,"error":"UNAUTHORIZED","message":"no","statusCode":401}""",
            HttpStatusCode.Unauthorized);

        (await BuildClient(vHandler).ValidateAsync("at-1")).Should().BeFalse();
    }

    /// <summary>Logout carries the bearer token, without which AppManager revokes nothing.</summary>
    [Fact]
    public async Task LogoutSendsTheBearerToken()
    {
        var vHandler = ScriptedHandler().Script("/AuthSvc/logout", """{"success":true,"data":null}""");

        await BuildClient(vHandler).LogoutAsync("rt-1", "at-1");

        vHandler.RequestFor("/AuthSvc/logout").BearerToken.Should().Be("at-1");
    }

    /// <summary>The profile reports Manager whatever role the server scoped to the application.</summary>
    [Fact]
    public async Task ProfileAlwaysReportsManager()
    {
        var vProfileJson = JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                userId = 2,
                email = DemoEmail,
                firstName = "TfLens",
                lastName = "Demo",
                applicationRole = string.Empty,
                createdDate = "2026-08-26T17:43:05Z"
            }
        });

        var vHandler = ScriptedHandler().Script("/UserSvc/profile", vProfileJson);

        var vProfile = await BuildClient(vHandler).GetProfileAsync("at-1");

        vProfile.ApplicationRole.Should().Be("Manager");
        vProfile.UserId.Should().Be(2);
        vProfile.IdentityProvider.Should().Be("AppManager");
    }

    /// <summary>Changing a password encrypts both the current and the new value.</summary>
    [Fact]
    public async Task ChangePasswordEncryptsBothFields()
    {
        var vHandler = ScriptedHandler().Script("/UserSvc/change-password", """{"success":true,"data":null}""");

        await BuildClient(vHandler).ChangePasswordAsync("at-1", DemoPassword, "NewPass!234");

        var vBody = vHandler.RequestFor("/UserSvc/change-password").Body;
        Decrypt(BodyField(vBody, "encryptedCurrentPassword")).Should().Be(DemoPassword);
        Decrypt(BodyField(vBody, "encryptedNewPassword")).Should().Be("NewPass!234");
    }

    /// <summary>
    /// Builds a client over a stub handler.
    /// </summary>
    /// <param name="aHandler">The handler answering the calls.</param>
    /// <param name="aOptions">Configuration to use, or the credential-free default.</param>
    /// <returns>The client under test.</returns>
    private static AppManagerClient BuildClient(HttpMessageHandler aHandler, TfLensOptions? aOptions = null)
    {
        var vOptions = aOptions ?? NewOptions();
        var vHttpClient = new HttpClient(aHandler) { BaseAddress = new Uri(vOptions.AppManagerBaseUrl) };
        return new AppManagerClient(vHttpClient, Options.Create(vOptions), NullLogger<AppManagerClient>.Instance);
    }

    /// <summary>
    /// Produces the default, credential-free options.
    /// </summary>
    /// <returns>Options pointing at a stub host with no API-key pair.</returns>
    private static TfLensOptions NewOptions() => new() { AppManagerBaseUrl = "https://appmanager.invalid" };

    /// <summary>
    /// Produces a handler already scripted with this test's public key.
    /// </summary>
    /// <returns>The handler, ready for further scripting.</returns>
    private StubHttpMessageHandler ScriptedHandler() =>
        new StubHttpMessageHandler().Script(PublicKeyPath, PublicKeyJson(objServerKey));

    /// <summary>
    /// Decrypts a base64 ciphertext the client produced.
    /// </summary>
    /// <param name="aBase64Cipher">The value the client put in the body.</param>
    /// <returns>The recovered plaintext.</returns>
    private string Decrypt(string aBase64Cipher) => Encoding.UTF8.GetString(
        objServerKey.Decrypt(Convert.FromBase64String(aBase64Cipher), RSAEncryptionPadding.OaepSHA256));

    /// <summary>
    /// Reads one field out of a captured JSON body.
    /// </summary>
    /// <param name="aBody">The captured request body.</param>
    /// <param name="aField">The field name.</param>
    /// <returns>The field's string value.</returns>
    internal static string BodyField(string aBody, string aField) =>
        JsonDocument.Parse(aBody).RootElement.GetProperty(aField).GetString() ?? string.Empty;

    /// <summary>
    /// Renders the public-key envelope for a key pair.
    /// </summary>
    /// <param name="aKey">The key to publish.</param>
    /// <returns>The JSON the endpoint would answer with.</returns>
    internal static string PublicKeyJson(RSA aKey) => JsonSerializer.Serialize(new
    {
        success = true,
        data = new { publicKey = aKey.ExportSubjectPublicKeyInfoPem(), algorithm = "RSA-OAEP-256", encoding = "base64" }
    });

    /// <summary>
    /// Renders a successful login envelope for the documented demo user.
    /// </summary>
    /// <returns>The JSON the endpoint would answer with.</returns>
    internal static string SuccessLoginJson() => JsonSerializer.Serialize(new
    {
        success = true,
        data = new
        {
            userId = 2,
            email = DemoEmail,
            firstName = "TfLens",
            lastName = "Demo",
            applicationRole = string.Empty,
            accessToken = FakeJwt(),
            refreshToken = "rt-initial",
            tokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
        }
    });

    /// <summary>
    /// Renders a refresh envelope, which carries tokens but no identity fields.
    /// </summary>
    /// <returns>The JSON the endpoint would answer with.</returns>
    private static string RefreshJson() => JsonSerializer.Serialize(new
    {
        success = true,
        data = new
        {
            accessToken = FakeJwt(),
            refreshToken = "rt-rotated",
            expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
        }
    });

    /// <summary>
    /// Builds a JWT whose payload matches the live server's claim set.
    /// </summary>
    /// <returns>An unsigned three-part token.</returns>
    private static string FakeJwt()
    {
        var vPayload = JsonSerializer.Serialize(new
        {
            userId = "2",
            email = DemoEmail,
            firstName = "TfLens",
            lastName = "Demo"
        });

        var vEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(vPayload))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return $"header.{vEncoded}.signature";
    }
}
