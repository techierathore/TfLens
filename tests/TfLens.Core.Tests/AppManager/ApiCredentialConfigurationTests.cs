using FluentAssertions;
using Microsoft.Extensions.Options;
using TfLens.Core.AppManager;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// REQ-FN-003's precondition — how TfLens behaves when the AppManager API-key pair is present in the
/// environment but empty.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape a real deployment actually arrives in. A <c>.env</c> file copied from
/// <c>.env.example</c> declares <c>TfLensAppManagerApiKey=</c> and <c>TfLensAppManagerApiSecret=</c>
/// with nothing after the equals sign, so both settings bind to the empty string rather than to
/// <c>null</c>. Two things must follow from that and neither is automatic: startup must treat it as
/// "unconfigured" rather than refusing to boot, and the client must send no headers at all rather than
/// two empty ones — which the live server answers <c>401 INVALID_API_KEY</c>, breaking every call
/// including the ones that would otherwise work without a key.
/// </para>
/// <para>
/// <c>POST /AuthSvc/forgot-password</c> and <c>POST /AuthSvc/reset-password</c> are the two endpoints
/// that require the pair outright, so this is the boundary that decides whether the reset round trip is
/// merely unavailable or actively broken.
/// </para>
/// </remarks>
public sealed class ApiCredentialConfigurationTests
{
    /// <summary>An empty pair is "not configured", not "configured badly".</summary>
    /// <param name="aKey">The key as configuration binds it.</param>
    /// <param name="aSecret">The secret as configuration binds it.</param>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("", null)]
    [InlineData(null, "")]
    public void AnEmptyPairStartsAndCountsAsUnconfigured(string? aKey, string? aSecret)
    {
        var vOptions = NewOptions(aKey, aSecret);

        vOptions.Invoking(aOptions => aOptions.Validate()).Should().NotThrow(
            "an unset pair is a supported deployment — the reset endpoints are simply unavailable");

        vOptions.HasAppManagerApiCredentials.Should().BeFalse();
    }

    /// <summary>Half a pair is refused at startup, whichever half is missing and however it is empty.</summary>
    /// <param name="aKey">The key as configuration binds it.</param>
    /// <param name="aSecret">The secret as configuration binds it.</param>
    /// <remarks>
    /// A half pair authenticates nothing and turns every call into a 401, which is precisely the silent
    /// misconfiguration BRD-9 exists to catch at startup rather than at the first user's sign-in.
    /// </remarks>
    [Theory]
    [InlineData("ak-live-test", null)]
    [InlineData("ak-live-test", "")]
    [InlineData("ak-live-test", "   ")]
    [InlineData(null, "secret-test")]
    [InlineData("", "secret-test")]
    [InlineData("   ", "secret-test")]
    public void HalfAPairIsRefusedAtStartup(string? aKey, string? aSecret)
    {
        var vOptions = NewOptions(aKey, aSecret);

        vOptions.Invoking(aOptions => aOptions.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*TfLensAppManagerApiKey*");
    }

    /// <summary>An empty pair puts no header on the wire, rather than two empty ones.</summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// An empty <c>X-Api-Key</c> is not the same as an absent one: the live server answers the former
    /// <c>401 INVALID_API_KEY</c> and the latter <c>400 APPLICATION_ID_REQUIRED</c>, and only the
    /// second leaves the rest of the surface working.
    /// </remarks>
    [Fact]
    public async Task AnEmptyPairSendsNoApiKeyHeaderAtAll()
    {
        var vHandler = new StubHttpMessageHandler()
            .Script("/AuthSvc/forgot-password", """{"success":true,"data":null}""");

        var vOptions = NewOptions(string.Empty, string.Empty);
        var vHttpClient = new HttpClient(vHandler) { BaseAddress = new Uri(vOptions.AppManagerBaseUrl) };
        var vClient = new AppManagerClient(
            vHttpClient,
            Options.Create(vOptions),
            new CapturingLogger<AppManagerClient>());

        await vClient.ForgotPasswordAsync("tflensdemo@techierathore.com");

        var vRequest = vHandler.RequestFor("/AuthSvc/forgot-password");
        vRequest.Headers.Should().NotContainKey("X-Api-Key");
        vRequest.Headers.Should().NotContainKey("X-Api-Secret");
    }

    /// <summary>
    /// A configured pair reaches <c>/AuthSvc/*</c>, which needs the application resolved.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AConfiguredPairIsSentOnAuthSvcCalls()
    {
        var vHandler = new StubHttpMessageHandler()
            .Script("/AuthSvc/forgot-password", """{"success":true,"data":null}""");
        var vClient = NewClient(vHandler);

        await vClient.ForgotPasswordAsync("tflensdemo@techierathore.com");

        var vRequest = vHandler.RequestFor("/AuthSvc/forgot-password");
        vRequest.Headers.Should().ContainKey("X-Api-Key");
        vRequest.Headers.Should().ContainKey("X-Api-Secret");
    }

    /// <summary>
    /// The pair is sent on <c>/UserSvc/*</c> too, because without it the application scope goes unresolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This assertion is the reverse of the one it replaces, and the reversal is deliberate. Measured on
    /// 2026-08-27, <c>GET /UserSvc/profile</c> answered <c>200</c> with no pair and
    /// <c>403 NO_APP_ACCESS</c> with one, so the client excluded <c>/UserSvc/*</c> to keep the Profile
    /// page alive. That <c>403</c> was an AppManager-side defect — reported as AM-002 — and the owner
    /// fixed it on 2026-08-28.
    /// </para>
    /// <para>
    /// Re-measured live that day: with the pair the endpoint answers <c>200</c> and
    /// <c>applicationRole: "Manager"</c>; without it, <c>200</c> with <c>applicationRole</c> as an
    /// <b>empty string</b>, because nothing tells AppManager which application to scope the role to. The
    /// exclusion had stopped protecting the page and started starving it, so it is gone.
    /// </para>
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheConfiguredPairIsSentOnUserSvcCallsAsWell()
    {
        var vHandler = new StubHttpMessageHandler()
            .Script("/UserSvc/profile", """{"success":true,"data":{"userId":2}}""");
        var vClient = NewClient(vHandler);

        await vClient.GetProfileAsync("bearer-token-value");

        var vRequest = vHandler.RequestFor("/UserSvc/profile");
        vRequest.Headers.Should().ContainKey("X-Api-Key");
        vRequest.Headers.Should().ContainKey("X-Api-Secret");
    }

    /// <summary>
    /// Builds a client whose API-key pair is configured, so only the per-path rule is under test.
    /// </summary>
    /// <param name="aHandler">The scripted transport.</param>
    /// <returns>The client.</returns>
    private static AppManagerClient NewClient(StubHttpMessageHandler aHandler)
    {
        var vOptions = NewOptions("ak_live_test_key", "sk_live_test_secret");
        var vHttpClient = new HttpClient(aHandler) { BaseAddress = new Uri(vOptions.AppManagerBaseUrl) };
        return new AppManagerClient(
            vHttpClient, Options.Create(vOptions), new CapturingLogger<AppManagerClient>());
    }

    /// <summary>
    /// Builds options with a database configured, so only the API-key pair is under test.
    /// </summary>
    /// <param name="aKey">The key as configuration binds it.</param>
    /// <param name="aSecret">The secret as configuration binds it.</param>
    /// <returns>The options.</returns>
    private static TfLensOptions NewOptions(string? aKey, string? aSecret) => new()
    {
        AppManagerBaseUrl = "https://appmanager.invalid",
        DbConnection = "Host=localhost;Database=tflens",
        AppManagerApiKey = aKey,
        AppManagerApiSecret = aSecret
    };
}
