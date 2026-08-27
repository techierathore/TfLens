using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.AppManager;

/// <summary>
/// The typed client over the AppManager v1.4 REST surface for Application Id 1.
/// </summary>
/// <remarks>
/// <para>
/// Three rules govern every call and were each verified against the live server on 2026-08-26.
/// </para>
/// <para>
/// <b>Password transport.</b> No password ever leaves the process in clear: the server's RSA public key
/// is fetched once from <c>GET /AuthSvc/public-key</c>, cached for the client's lifetime, and every
/// password field is encrypted with RSA-OAEP-SHA256 and base64-encoded (BRD-90). A
/// <c>DECRYPTION_FAILED</c> answer means the cached key has been rotated, so the key is refetched and
/// the call is retried exactly once.
/// </para>
/// <para>
/// <b>API-key headers.</b> <c>X-Api-Key</c> / <c>X-Api-Secret</c> are optional on the AppManager side
/// but must be sent whole or not at all — a partial or bogus pair is answered <c>INVALID_API_KEY</c> on
/// every call. They are therefore sent only when <see cref="TfLensOptions.HasAppManagerApiCredentials"/>
/// is true, and the application is otherwise resolved from the <c>applicationId</c> this client puts in
/// every request body.
/// </para>
/// <para>
/// <b>Privacy.</b> Nothing here logs a password, an access or refresh token, a reset token or the API
/// secret — only paths, status codes, error codes and user ids (Coding Standards §Logging).
/// </para>
/// <para>
/// The client never calls LicenseSvc, FeatureSvc, PaymentSvc or IssueSvc (BRD-95, REQ-FN-008).
/// </para>
/// </remarks>
public sealed class AppManagerClient : IAppManagerClient
{
    /// <summary>The only application role TfLens ever requests or persists (BRD-95).</summary>
    public const string ManagerRoleCode = "Manager";

    private const string PublicKeyPath = "/AuthSvc/public-key";
    private const string LoginPath = "/AuthSvc/login";
    private const string RegisterPath = "/AuthSvc/register";
    private const string RefreshPath = "/AuthSvc/refresh";
    private const string ValidatePath = "/AuthSvc/validate";
    private const string LogoutPath = "/AuthSvc/logout";
    private const string ForgotPasswordPath = "/AuthSvc/forgot-password";
    private const string ResetPasswordPath = "/AuthSvc/reset-password";
    private const string ProfilePath = "/UserSvc/profile";
    private const string ChangePasswordPath = "/UserSvc/change-password";

    private readonly HttpClient objHttpClient;
    private readonly TfLensOptions objOptions;
    private readonly ILogger<AppManagerClient> objLogger;
    private readonly SemaphoreSlim objPublicKeyLock = new(1, 1);

    private string? objCachedPublicKey;

    /// <summary>
    /// Creates the client.
    /// </summary>
    /// <param name="aHttpClient">The typed <see cref="HttpClient"/>; its base address is the AppManager root.</param>
    /// <param name="aOptions">TfLens configuration — base URL, application id and the optional API-key pair.</param>
    /// <param name="aLogger">Diagnostics; never receives a secret.</param>
    public AppManagerClient(
        HttpClient aHttpClient,
        IOptions<TfLensOptions> aOptions,
        ILogger<AppManagerClient> aLogger)
    {
        objHttpClient = aHttpClient;
        objOptions = aOptions.Value;
        objLogger = aLogger;

        objHttpClient.BaseAddress ??= new Uri(objOptions.AppManagerBaseUrl);
    }

    /// <inheritdoc />
    public async Task<AuthResponseData> LoginAsync(
        string aEmail,
        string aPassword,
        CancellationToken aCancellationToken = default)
    {
        var vData = await PostEncryptedAsync(
            LoginPath,
            aEncrypt => new Dictionary<string, object?>
            {
                ["email"] = aEmail,
                ["encryptedPassword"] = aEncrypt(aPassword)
            },
            null,
            aCancellationToken).ConfigureAwait(false);

        var vResult = ReadAuthResponse(vData);
        objLogger.LogInformation("AppManager sign-in succeeded for user {UserId}.", vResult.UserId);
        return vResult;
    }

    /// <inheritdoc />
    public async Task<AuthResponseData> RegisterAsync(
        RegisterRequest aRequest,
        CancellationToken aCancellationToken = default)
    {
        // BRD-91: a password that cannot pass AppManager's rules is refused before the round trip.
        if (PasswordRules.Describe(aRequest.Password) is { } vViolation)
        {
            throw new AppManagerException(AppManagerException.Codes.ValidationError, vViolation, 400);
        }

        var vData = await PostEncryptedAsync(
            RegisterPath,
            aEncrypt => new Dictionary<string, object?>
            {
                ["email"] = aRequest.Email,
                ["encryptedPassword"] = aEncrypt(aRequest.Password),
                ["firstName"] = aRequest.FirstName,
                ["lastName"] = aRequest.LastName,
                ["applicationRoleCode"] = ManagerRoleCode
            },
            null,
            aCancellationToken).ConfigureAwait(false);

        var vResult = ReadAuthResponse(vData);
        objLogger.LogInformation("AppManager registration succeeded for user {UserId}.", vResult.UserId);
        return vResult;
    }

    /// <inheritdoc />
    public async Task ForgotPasswordAsync(string aEmail, CancellationToken aCancellationToken = default)
    {
        var vBody = new Dictionary<string, object?> { ["email"] = aEmail };

        try
        {
            await SendAsync(HttpMethod.Post, ForgotPasswordPath, vBody, null, aCancellationToken)
                .ConfigureAwait(false);
        }
        catch (AppManagerException vEx)
        {
            // Enumeration safety (BRD-92): the caller gets the same outcome for a known and an unknown
            // address, so a failure here is logged by code and swallowed rather than surfaced.
            objLogger.LogWarning(
                "AppManager forgot-password answered {Code} ({Status}); the caller sees the neutral outcome.",
                vEx.Code,
                vEx.StatusCode);
        }
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(
        string aToken,
        string aNewPassword,
        CancellationToken aCancellationToken = default)
    {
        if (PasswordRules.Describe(aNewPassword) is { } vViolation)
        {
            throw new AppManagerException(AppManagerException.Codes.ValidationError, vViolation, 400);
        }

        // The reset token is a credential: it is put in the body and never written to a log.
        await PostEncryptedAsync(
            ResetPasswordPath,
            aEncrypt => new Dictionary<string, object?>
            {
                ["token"] = aToken,
                ["encryptedNewPassword"] = aEncrypt(aNewPassword)
            },
            null,
            aCancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuthResponseData> RefreshAsync(
        string aRefreshToken,
        CancellationToken aCancellationToken = default)
    {
        var vBody = new Dictionary<string, object?> { ["refreshToken"] = aRefreshToken };
        var vData = await SendAsync(HttpMethod.Post, RefreshPath, vBody, null, aCancellationToken)
            .ConfigureAwait(false);

        return ReadRefreshResponse(vData);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(string aAccessToken, CancellationToken aCancellationToken = default)
    {
        var vBody = new Dictionary<string, object?> { ["accessToken"] = aAccessToken };

        try
        {
            var vData = await SendAsync(HttpMethod.Post, ValidatePath, vBody, null, aCancellationToken)
                .ConfigureAwait(false);

            return vData.ValueKind == JsonValueKind.Object
                   && vData.TryGetProperty("isValid", out var vIsValid)
                   && vIsValid.ValueKind == JsonValueKind.True;
        }
        catch (AppManagerException vEx)
        {
            objLogger.LogInformation("AppManager rejected a resumed session with {Code}.", vEx.Code);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task LogoutAsync(
        string aRefreshToken,
        string? aAccessToken = null,
        CancellationToken aCancellationToken = default)
    {
        var vBody = new Dictionary<string, object?>
        {
            ["refreshToken"] = aRefreshToken,
            ["logoutAllDevices"] = false
        };

        await SendAsync(HttpMethod.Post, LogoutPath, vBody, aAccessToken, aCancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserProfile> GetProfileAsync(
        string aAccessToken,
        CancellationToken aCancellationToken = default)
    {
        var vData = await SendAsync(HttpMethod.Get, ProfilePath, null, aAccessToken, aCancellationToken)
            .ConfigureAwait(false);

        return new UserProfile(
            ReadInt(vData, "userId"),
            ReadString(vData, "email") ?? string.Empty,
            ReadString(vData, "firstName"),
            ReadString(vData, "lastName"),
            ManagerRoleCode,
            ReadString(vData, "createdDate"),
            "AppManager");
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(
        string aAccessToken,
        string aCurrentPassword,
        string aNewPassword,
        CancellationToken aCancellationToken = default)
    {
        if (PasswordRules.Describe(aNewPassword) is { } vViolation)
        {
            throw new AppManagerException(AppManagerException.Codes.ValidationError, vViolation, 400);
        }

        await PostEncryptedAsync(
            ChangePasswordPath,
            aEncrypt => new Dictionary<string, object?>
            {
                ["encryptedCurrentPassword"] = aEncrypt(aCurrentPassword),
                ["encryptedNewPassword"] = aEncrypt(aNewPassword)
            },
            aAccessToken,
            aCancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Encrypts one password with a PEM public key, exactly as AppManager expects it.
    /// </summary>
    /// <param name="aPassword">The plaintext password; never logged.</param>
    /// <param name="aPublicKeyPem">The server's PEM-encoded RSA public key.</param>
    /// <returns>The base64 of the RSA-OAEP-SHA256 ciphertext.</returns>
    /// <remarks>
    /// Both the OAEP digest and the MGF1 digest are SHA-256; <see cref="RSAEncryptionPadding.OaepSHA256"/>
    /// is the single .NET value that means exactly that. Any other padding is answered
    /// <c>DECRYPTION_FAILED</c>.
    /// </remarks>
    public static string EncryptPassword(string aPassword, string aPublicKeyPem)
    {
        using var vRsa = RSA.Create();
        vRsa.ImportFromPem(aPublicKeyPem);
        var vCipher = vRsa.Encrypt(Encoding.UTF8.GetBytes(aPassword), RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(vCipher);
    }

    /// <summary>
    /// Posts a body carrying one or more encrypted passwords, retrying once on a rotated key.
    /// </summary>
    /// <param name="aPath">The AppManager path to post to.</param>
    /// <param name="aBodyFactory">Builds the body from an encryption function bound to the current key.</param>
    /// <param name="aAccessToken">Bearer token when the endpoint is authenticated; otherwise <c>null</c>.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The <c>data</c> element of the response envelope.</returns>
    /// <exception cref="AppManagerException">AppManager rejected the request.</exception>
    private async Task<JsonElement> PostEncryptedAsync(
        string aPath,
        Func<Func<string, string>, IDictionary<string, object?>> aBodyFactory,
        string? aAccessToken,
        CancellationToken aCancellationToken)
    {
        var vKey = await GetPublicKeyAsync(false, aCancellationToken).ConfigureAwait(false);

        try
        {
            var vBody = aBodyFactory(aPassword => EncryptPassword(aPassword, vKey));
            return await SendAsync(HttpMethod.Post, aPath, vBody, aAccessToken, aCancellationToken)
                .ConfigureAwait(false);
        }
        catch (AppManagerException vEx) when (vEx.Code == AppManagerException.Codes.DecryptionFailed)
        {
            objLogger.LogWarning(
                "AppManager answered {Code} for {Path}; refetching the public key and retrying once.",
                vEx.Code,
                aPath);

            var vFreshKey = await GetPublicKeyAsync(true, aCancellationToken).ConfigureAwait(false);
            var vRetryBody = aBodyFactory(aPassword => EncryptPassword(aPassword, vFreshKey));
            return await SendAsync(HttpMethod.Post, aPath, vRetryBody, aAccessToken, aCancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the server's public key, fetching it at most once per rotation.
    /// </summary>
    /// <param name="aForceRefresh"><c>true</c> to discard the cached key first, after a decryption failure.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The PEM-encoded RSA public key.</returns>
    /// <exception cref="AppManagerException">The key endpoint answered without a key.</exception>
    private async Task<string> GetPublicKeyAsync(bool aForceRefresh, CancellationToken aCancellationToken)
    {
        var vCached = Volatile.Read(ref objCachedPublicKey);
        if (!aForceRefresh && vCached is not null)
        {
            return vCached;
        }

        await objPublicKeyLock.WaitAsync(aCancellationToken).ConfigureAwait(false);

        try
        {
            var vExisting = Volatile.Read(ref objCachedPublicKey);
            if (!aForceRefresh && vExisting is not null)
            {
                return vExisting;
            }

            var vData = await SendAsync(HttpMethod.Get, PublicKeyPath, null, null, aCancellationToken)
                .ConfigureAwait(false);

            var vKey = ReadString(vData, "publicKey")
                       ?? throw new AppManagerException(
                           "PUBLIC_KEY_UNAVAILABLE",
                           "AppManager returned no public key; passwords cannot be encrypted.",
                           0);

            Volatile.Write(ref objCachedPublicKey, vKey);
            objLogger.LogInformation("Cached the AppManager public key ({Length} characters).", vKey.Length);
            return vKey;
        }
        finally
        {
            objPublicKeyLock.Release();
        }
    }

    /// <summary>
    /// Sends one AppManager request and unwraps its response envelope.
    /// </summary>
    /// <param name="aMethod">The HTTP method.</param>
    /// <param name="aPath">The AppManager path.</param>
    /// <param name="aBody">The request body, which always gains <c>applicationId</c>; <c>null</c> for a GET.</param>
    /// <param name="aAccessToken">Bearer token when the endpoint is authenticated; otherwise <c>null</c>.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The <c>data</c> element of the envelope, which may be <c>Undefined</c> when there is none.</returns>
    /// <exception cref="AppManagerException">The call failed, or the envelope reported <c>success: false</c>.</exception>
    private async Task<JsonElement> SendAsync(
        HttpMethod aMethod,
        string aPath,
        IDictionary<string, object?>? aBody,
        string? aAccessToken,
        CancellationToken aCancellationToken)
    {
        using var vRequest = BuildRequest(aMethod, aPath, aBody, aAccessToken);

        HttpResponseMessage vResponse;
        try
        {
            vResponse = await objHttpClient.SendAsync(vRequest, aCancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException vEx)
        {
            throw new AppManagerException(
                "NETWORK_ERROR",
                $"AppManager could not be reached for {aPath}.",
                0,
                vEx);
        }

        using (vResponse)
        {
            return await ReadEnvelopeAsync(vResponse, aPath, aCancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds one request, applying the header rule and the always-present application id.
    /// </summary>
    /// <param name="aMethod">The HTTP method.</param>
    /// <param name="aPath">The AppManager path.</param>
    /// <param name="aBody">The request body, or <c>null</c>.</param>
    /// <param name="aAccessToken">Bearer token, or <c>null</c>.</param>
    /// <returns>The prepared request message.</returns>
    private HttpRequestMessage BuildRequest(
        HttpMethod aMethod,
        string aPath,
        IDictionary<string, object?>? aBody,
        string? aAccessToken)
    {
        var vRequest = new HttpRequestMessage(aMethod, aPath);
        vRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Whole pair or nothing: a half or bogus pair is answered INVALID_API_KEY on every call.
        if (objOptions.HasAppManagerApiCredentials)
        {
            vRequest.Headers.TryAddWithoutValidation("X-Api-Key", objOptions.AppManagerApiKey);
            vRequest.Headers.TryAddWithoutValidation("X-Api-Secret", objOptions.AppManagerApiSecret);
        }

        if (!string.IsNullOrEmpty(aAccessToken))
        {
            vRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aAccessToken);
        }

        if (aBody is null)
        {
            return vRequest;
        }

        // The application is resolved from this field whenever the API-key pair is absent.
        aBody["applicationId"] = objOptions.AppManagerAppId;
        vRequest.Content = new StringContent(JsonSerializer.Serialize(aBody), Encoding.UTF8, "application/json");
        return vRequest;
    }

    /// <summary>
    /// Reads the standard <c>{ success, data, error, message, statusCode }</c> envelope.
    /// </summary>
    /// <param name="aResponse">The response to read.</param>
    /// <param name="aPath">The path, for diagnostics.</param>
    /// <param name="aCancellationToken">Cancels the read.</param>
    /// <returns>The <c>data</c> element on success.</returns>
    /// <exception cref="AppManagerException">The envelope reported a failure, or there was no JSON at all.</exception>
    private async Task<JsonElement> ReadEnvelopeAsync(
        HttpResponseMessage aResponse,
        string aPath,
        CancellationToken aCancellationToken)
    {
        var vStatus = (int)aResponse.StatusCode;
        var vJson = await aResponse.Content.ReadAsStringAsync(aCancellationToken).ConfigureAwait(false);

        using var vDocument = TryParse(vJson);

        if (vDocument is null)
        {
            objLogger.LogWarning("AppManager answered {Status} with no JSON body for {Path}.", vStatus, aPath);
            throw new AppManagerException(
                $"HTTP{vStatus}",
                $"AppManager answered {vStatus} with no readable body.",
                vStatus);
        }

        var vRoot = vDocument.RootElement;
        var vIsSuccess = vRoot.TryGetProperty("success", out var vSuccess) && vSuccess.ValueKind == JsonValueKind.True;

        if (!vIsSuccess || !aResponse.IsSuccessStatusCode)
        {
            var vCode = ReadString(vRoot, "error") ?? $"HTTP{vStatus}";
            objLogger.LogWarning("AppManager rejected {Path} with {Code} ({Status}).", aPath, vCode, vStatus);
            throw new AppManagerException(
                vCode,
                ReadString(vRoot, "message") ?? "AppManager rejected the request.",
                vStatus);
        }

        return vRoot.TryGetProperty("data", out var vData) ? vData.Clone() : default;
    }

    /// <summary>
    /// Parses a response body, treating a non-JSON body as absent rather than fatal.
    /// </summary>
    /// <param name="aJson">The raw response text.</param>
    /// <returns>The parsed document, or <c>null</c> when the body was empty or not JSON.</returns>
    private static JsonDocument? TryParse(string aJson)
    {
        if (string.IsNullOrWhiteSpace(aJson))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(aJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a login or registration payload onto the shared contract.
    /// </summary>
    /// <param name="aData">The envelope's <c>data</c> element.</param>
    /// <returns>The tokens and display claims.</returns>
    /// <remarks>
    /// The role is the constant <see cref="ManagerRoleCode"/>, never the server's <c>applicationRole</c>:
    /// every TfLens user is a Manager and no other role is ever requested or persisted (BRD-95).
    /// </remarks>
    private static AuthResponseData ReadAuthResponse(JsonElement aData) => new(
        ReadInt(aData, "userId"),
        ReadString(aData, "email") ?? string.Empty,
        ReadString(aData, "firstName"),
        ReadString(aData, "lastName"),
        ManagerRoleCode,
        ReadString(aData, "accessToken") ?? string.Empty,
        ReadString(aData, "refreshToken") ?? string.Empty,
        ReadString(aData, "tokenExpiresAt") ?? ReadString(aData, "expiresAt") ?? string.Empty);

    /// <summary>
    /// Maps a refresh payload onto the shared contract.
    /// </summary>
    /// <param name="aData">The envelope's <c>data</c> element.</param>
    /// <returns>The rotated tokens, with the identity claims recovered from the new access token.</returns>
    /// <remarks>
    /// <c>POST /AuthSvc/refresh</c> returns only <c>accessToken</c>, <c>refreshToken</c> and
    /// <c>expiresAt</c> — verified live on 2026-08-26. The identity fields the contract requires are
    /// therefore read back out of the freshly issued JWT, which carries <c>userId</c>, <c>email</c>,
    /// <c>firstName</c> and <c>lastName</c>, rather than being invented or left blank.
    /// </remarks>
    private static AuthResponseData ReadRefreshResponse(JsonElement aData)
    {
        var vAccessToken = ReadString(aData, "accessToken") ?? string.Empty;
        using var vClaims = ReadJwtPayload(vAccessToken);
        var vPayload = vClaims?.RootElement ?? default;

        return new AuthResponseData(
            ReadInt(vPayload, "userId"),
            ReadString(vPayload, "email") ?? string.Empty,
            ReadString(vPayload, "firstName"),
            ReadString(vPayload, "lastName"),
            ManagerRoleCode,
            vAccessToken,
            ReadString(aData, "refreshToken") ?? string.Empty,
            ReadString(aData, "expiresAt") ?? ReadString(aData, "tokenExpiresAt") ?? string.Empty);
    }

    /// <summary>
    /// Decodes a JWT's payload segment without validating it.
    /// </summary>
    /// <param name="aToken">The access token; never logged.</param>
    /// <returns>The payload document, or <c>null</c> when the token is not a readable JWT.</returns>
    /// <remarks>
    /// The signature is AppManager's to verify — the token arrived over TLS from the endpoint that
    /// minted it seconds earlier, and this reads it only to recover display claims.
    /// </remarks>
    private static JsonDocument? ReadJwtPayload(string aToken)
    {
        var vParts = aToken.Split('.');
        if (vParts.Length < 2)
        {
            return null;
        }

        try
        {
            var vPadded = vParts[1].Replace('-', '+').Replace('_', '/');
            vPadded += new string('=', (4 - (vPadded.Length % 4)) % 4);
            return JsonDocument.Parse(Convert.FromBase64String(vPadded));
        }
        catch (Exception vEx) when (vEx is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a string property, tolerating an absent or null field.
    /// </summary>
    /// <param name="aElement">The element to read from.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The value, or <c>null</c> when it is absent, null or empty.</returns>
    private static string? ReadString(JsonElement aElement, string aName)
    {
        if (aElement.ValueKind != JsonValueKind.Object || !aElement.TryGetProperty(aName, out var vValue))
        {
            return null;
        }

        var vText = vValue.ValueKind == JsonValueKind.String ? vValue.GetString() : vValue.ToString();
        return string.IsNullOrEmpty(vText) ? null : vText;
    }

    /// <summary>
    /// Reads an integer property that AppManager may send as a number or as a string.
    /// </summary>
    /// <param name="aElement">The element to read from.</param>
    /// <param name="aName">The property name.</param>
    /// <returns>The value, or zero when it is absent or unreadable.</returns>
    private static int ReadInt(JsonElement aElement, string aName)
    {
        if (aElement.ValueKind != JsonValueKind.Object || !aElement.TryGetProperty(aName, out var vValue))
        {
            return 0;
        }

        return vValue.ValueKind switch
        {
            JsonValueKind.Number => vValue.GetInt32(),
            JsonValueKind.String => int.TryParse(vValue.GetString(), out var vParsed) ? vParsed : 0,
            _ => 0
        };
    }
}
