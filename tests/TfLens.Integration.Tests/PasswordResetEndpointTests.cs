using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Integration.Tests;

/// <summary>
/// REQ-FN-003 / BRD-92 — the two reset endpoints, driven through the real host.
/// </summary>
/// <remarks>
/// <para>
/// The acceptance has three clauses and two of them are about what the browser can observe, so they
/// are proved here against real HTTP responses from the application that ships: the forgot response
/// is identical for a known and an unknown address, and <c>INVALID_RESET_TOKEN</c> and
/// <c>APP_ID_MISMATCH</c> arrive as one indistinguishable outcome.
/// </para>
/// <para>
/// Only the AppManager client is substituted, and only because the codes under test cannot be
/// obtained any other way: <c>APP_ID_MISMATCH</c> requires a reset token minted for a different
/// tenant. Everything between the form post and the response — antiforgery, routing, the authorization
/// convention, the redirect — is the real thing.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PasswordResetEndpointTests
{
    /// <summary>The documented demo account — an address that exists.</summary>
    private const string KnownEmail = "tflensdemo@techierathore.com";

    /// <summary>An address that does not exist, and must be indistinguishable from one that does.</summary>
    private const string UnknownEmail = "nobody.at.all@techierathore.invalid";

    /// <summary>A reset token distinctive enough that any leak into a response is unmistakable.</summary>
    private const string SecretResetToken = "rst-CANARY-9f3ac71e-do-not-log";

    /// <summary>Response headers that legitimately differ between two requests.</summary>
    private static readonly string[] VolatileHeaders = ["Date", "Set-Cookie", "Request-Context"];

    private readonly PostgresFixture objDb;

    /// <summary>Creates the test class.</summary>
    /// <param name="aDb">The shared live-PostgreSQL fixture.</param>
    public PasswordResetEndpointTests(PostgresFixture aDb)
    {
        objDb = aDb;
    }

    /// <summary>
    /// A known and an unknown address produce the same response, header for header and byte for byte.
    /// </summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// Comparing only the status or only the redirect target would miss the ways this actually leaks in
    /// practice: an extra header, a different body length, a query parameter that says <c>sent=0</c>.
    /// Everything but the clock and the rotating antiforgery cookie has to match.
    /// </remarks>
    [Fact]
    public async Task ForgotPasswordAnswersIdenticallyForAKnownAndAnUnknownAddress()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

        var vAppManager = new ScriptedAppManagerClient();
        await using var vFactory = new ScriptedHostFactory(vAppManager);

        var vKnown = await PostForgotAsync(vFactory, KnownEmail);
        var vUnknown = await PostForgotAsync(vFactory, UnknownEmail);

        vUnknown.Status.Should().Be(vKnown.Status);
        vUnknown.Location.Should().Be(vKnown.Location);
        vUnknown.Body.Should().Be(vKnown.Body);
        vUnknown.Headers.Should().BeEquivalentTo(vKnown.Headers);

        vKnown.Location.Should().Be("/forgot-password?sent=1", "there is exactly one outcome");
        vAppManager.ForgottenAddresses.Should().Equal(KnownEmail, UnknownEmail);
    }

    /// <summary>The page the neutral redirect lands on says the same thing for either address.</summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// The redirect being equal is only half of what a user sees. If the landing page then rendered
    /// something derived from the address, the leak would simply have moved one hop.
    /// </remarks>
    [Fact]
    public async Task TheForgotLandingPageSaysTheSameThingWhicheverAddressWasSubmitted()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

        await using var vFactory = new ScriptedHostFactory(new ScriptedAppManagerClient());
        using var vClient = vFactory.CreateClient(NoRedirects());

        var vLanding = await vClient.GetStringAsync("/forgot-password?sent=1");

        vLanding.Should().Contain("If that address exists");
        vLanding.Should().NotContain(KnownEmail).And.NotContain(UnknownEmail);
    }

    /// <summary>
    /// <c>INVALID_RESET_TOKEN</c> and <c>APP_ID_MISMATCH</c> produce one indistinguishable response.
    /// </summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// A wrong-tenant link and a stale link must be the same event as far as the browser is concerned.
    /// Telling them apart would say "this token is real, just not yours", which is a fact about another
    /// application's users.
    /// </remarks>
    [Fact]
    public async Task BothDeadLinkCodesProduceOneIdenticalResponse()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

        var vAppManager = new ScriptedAppManagerClient();
        await using var vFactory = new ScriptedHostFactory(vAppManager);

        vAppManager.ResetFailureCode = AppManagerException.Codes.InvalidResetToken;
        var vStale = await PostResetAsync(vFactory);

        vAppManager.ResetFailureCode = AppManagerException.Codes.AppIdMismatch;
        var vWrongTenant = await PostResetAsync(vFactory);

        vWrongTenant.Status.Should().Be(vStale.Status);
        vWrongTenant.Location.Should().Be(vStale.Location);
        vWrongTenant.Body.Should().Be(vStale.Body);
        vWrongTenant.Headers.Should().BeEquivalentTo(vStale.Headers);

        vStale.Location.Should().Be("/reset-password?error=expired");
        vAppManager.SubmittedTokens.Should().Equal(SecretResetToken, SecretResetToken);
    }

    /// <summary>The single dead-link outcome renders one "invalid or expired" sentence.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheDeadLinkOutcomeRendersOneInvalidOrExpiredSentence()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

        await using var vFactory = new ScriptedHostFactory(new ScriptedAppManagerClient());
        using var vClient = vFactory.CreateClient(NoRedirects());

        var vPage = await vClient.GetStringAsync($"/reset-password?token={SecretResetToken}&error=expired");

        vPage.Should().Contain("invalid or has expired");
        vPage.Should().NotContain("APP_ID_MISMATCH").And.NotContain("INVALID_RESET_TOKEN");
    }

    /// <summary>The reset token never comes back out of the endpoint, on any outcome.</summary>
    /// <param name="aFailureCode">The AppManager code to refuse with, or empty to accept the reset.</param>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// The redirect target is the one place a token would most plausibly be echoed — carrying it back
    /// so the user can retry is the obvious convenience — and it is the worst place for it, since a
    /// redirect target reaches browser history, the referrer header and every access log on the way.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("INVALID_RESET_TOKEN")]
    [InlineData("APP_ID_MISMATCH")]
    [InlineData("VALIDATION_ERROR")]
    [InlineData("INTERNAL_ERROR")]
    public async Task TheResetTokenNeverComesBackInTheResponse(string aFailureCode)
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

        var vAppManager = new ScriptedAppManagerClient
        {
            ResetFailureCode = aFailureCode.Length == 0 ? null : aFailureCode
        };

        await using var vFactory = new ScriptedHostFactory(vAppManager);

        var vResponse = await PostResetAsync(vFactory);

        vResponse.Location.Should().NotContain(SecretResetToken);
        vResponse.Body.Should().NotContain(SecretResetToken);
        vResponse.Headers.Values.Should().OnlyContain(aValue => !aValue.Contains(SecretResetToken));
    }

    /// <summary>The reset page never echoes the token it was handed into the DOM.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheResetPageNeverEchoesTheTokenIntoTheDom()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

        await using var vFactory = new ScriptedHostFactory(new ScriptedAppManagerClient());
        using var vClient = vFactory.CreateClient(NoRedirects());

        var vPage = await vClient.GetStringAsync($"/reset-password?token={SecretResetToken}");

        vPage.Should().NotContain(SecretResetToken, "the token must never reach the page source");
    }

    /// <summary>
    /// Posts one forgot-password form and captures everything observable about the answer.
    /// </summary>
    /// <param name="aFactory">The host to post to.</param>
    /// <param name="aEmail">The address to submit.</param>
    /// <returns>The captured response.</returns>
    private static Task<CapturedResponse> PostForgotAsync(ScriptedHostFactory aFactory, string aEmail) =>
        PostFormAsync(aFactory, "/auth/forgot-password", new Dictionary<string, string> { ["Email"] = aEmail });

    /// <summary>
    /// Posts one reset form carrying the canary token and captures the answer.
    /// </summary>
    /// <param name="aFactory">The host to post to.</param>
    /// <returns>The captured response.</returns>
    private static Task<CapturedResponse> PostResetAsync(ScriptedHostFactory aFactory) =>
        PostFormAsync(aFactory, "/auth/reset-password", new Dictionary<string, string>
        {
            ["Token"] = SecretResetToken,
            ["Password"] = "TfLensReset!23"
        });

    /// <summary>
    /// Posts one antiforgery-protected form to an endpoint and captures the answer.
    /// </summary>
    /// <param name="aFactory">The host to post to.</param>
    /// <param name="aPath">The endpoint to post to.</param>
    /// <param name="aFields">The form fields, less the antiforgery token.</param>
    /// <returns>The captured response.</returns>
    /// <remarks>
    /// The token pair is minted from the host's own <see cref="IAntiforgery"/> rather than scraped out
    /// of a page, because these two pages submit on the Blazor circuit and so render no hidden token
    /// field. The endpoints are still mapped, still anonymous and still the fallback path, so they are
    /// still worth proving — and validating a real token pair is the only honest way to reach the
    /// handler at all, since a missing token short-circuits into <c>error=badrequest</c> before the
    /// AppManager call is ever made.
    /// </remarks>
    private static async Task<CapturedResponse> PostFormAsync(
        ScriptedHostFactory aFactory,
        string aPath,
        Dictionary<string, string> aFields)
    {
        using var vClient = aFactory.CreateClient(NoRedirects());

        var (vRequestToken, vCookie) = IssueAntiforgeryPair(aFactory);
        aFields["__RequestVerificationToken"] = vRequestToken;

        using var vRequest = new HttpRequestMessage(HttpMethod.Post, aPath)
        {
            Content = new FormUrlEncodedContent(aFields)
        };

        vRequest.Headers.Add("Cookie", vCookie);

        return await CapturedResponse.FromAsync(await vClient.SendAsync(vRequest));
    }

    /// <summary>
    /// Mints a matching antiforgery cookie and request token from the running host.
    /// </summary>
    /// <param name="aFactory">The host whose antiforgery service issues the pair.</param>
    /// <returns>The request token and the cookie that validates it.</returns>
    private static (string RequestToken, string Cookie) IssueAntiforgeryPair(ScriptedHostFactory aFactory)
    {
        using var vScope = aFactory.Services.CreateScope();

        var vContext = new DefaultHttpContext { RequestServices = vScope.ServiceProvider };
        var vTokens = vScope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(vContext);
        var vSetCookie = vContext.Response.Headers.SetCookie.ToString();

        return (vTokens.RequestToken ?? string.Empty, vSetCookie.Split(';')[0]);
    }

    /// <summary>Client options that leave a redirect observable instead of following it.</summary>
    /// <returns>The options.</returns>
    private static WebApplicationFactoryClientOptions NoRedirects() => new() { AllowAutoRedirect = false };

    /// <summary>Everything about a response two requests can be compared on.</summary>
    /// <param name="Status">The status code.</param>
    /// <param name="Location">The redirect target, or empty when there was none.</param>
    /// <param name="Body">The response body.</param>
    /// <param name="Headers">The response headers, less the ones that legitimately vary.</param>
    private sealed record CapturedResponse(
        HttpStatusCode Status,
        string Location,
        string Body,
        IReadOnlyDictionary<string, string> Headers)
    {
        /// <summary>Captures one response.</summary>
        /// <param name="aResponse">The response to capture.</param>
        /// <returns>The captured shape.</returns>
        public static async Task<CapturedResponse> FromAsync(HttpResponseMessage aResponse)
        {
            using (aResponse)
            {
                return new CapturedResponse(
                    aResponse.StatusCode,
                    aResponse.Headers.Location?.ToString() ?? string.Empty,
                    await aResponse.Content.ReadAsStringAsync(),
                    aResponse.Headers
                        .Concat(aResponse.Content.Headers)
                        .Where(aHeader => !VolatileHeaders.Contains(aHeader.Key, StringComparer.OrdinalIgnoreCase))
                        .ToDictionary(
                            aHeader => aHeader.Key,
                            aHeader => string.Join(',', aHeader.Value),
                            StringComparer.OrdinalIgnoreCase));
            }
        }
    }

    /// <summary>The real host with only the AppManager client substituted.</summary>
    private sealed class ScriptedHostFactory : WebApplicationFactory<Program>
    {
        private readonly IAppManagerClient objAppManager;

        /// <summary>Creates the factory.</summary>
        /// <param name="aAppManager">The scripted client the host will resolve.</param>
        public ScriptedHostFactory(IAppManagerClient aAppManager)
        {
            objAppManager = aAppManager;
        }

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder aBuilder) =>
            aBuilder.ConfigureTestServices(aServices =>
            {
                aServices.RemoveAll<IAppManagerClient>();
                aServices.AddSingleton(objAppManager);
            });
    }
}
