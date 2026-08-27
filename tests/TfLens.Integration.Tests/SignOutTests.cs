using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace TfLens.Integration.Tests;

/// <summary>
/// REQ-FN-007 / REQ-UI-008 — signing out actually signs the user out.
/// </summary>
/// <remarks>
/// <para>
/// This exists because sign-out shipped completely broken and nobody noticed. The user menu navigated
/// to <c>GET /signout</c>, a route nothing ever mapped — only <c>POST /auth/logout</c> exists. Clicking
/// Sign out sent the user to a dead route, the <c>TfLensAuth</c> cookie survived, and they remained
/// authenticated. It is the only sign-out control in the app, so sign-out did not work at all, and on a
/// shared machine that is a security problem rather than a cosmetic one.
/// </para>
/// <para>
/// The lesson is the one this test encodes: a feature is not built until it has been exercised through
/// the control a user actually clicks. Compiling, rendering and unit-testing all passed.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SignOutTests
{
    private const string DemoEmail = "tflensdemo@techierathore.com";
    private const string DemoPassword = "TfLensDemo!23";

    private readonly PostgresFixture objDb;

    /// <summary>Creates the test class.</summary>
    /// <param name="aDb">The shared live-PostgreSQL fixture.</param>
    public SignOutTests(PostgresFixture aDb)
    {
        objDb = aDb;
    }

    /// <summary>
    /// Signing out clears the cookie and an authenticated page stops being reachable.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task SigningOutClearsTheSessionAndLocksTheUserOut()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

        await using var vHost = new TfLensTestHost();
        var vServices = vHost.TryGetServices(out var vWhyNot);
        Assert.True(vServices is not null, $"The application host could not be built: {vWhyNot}");

        using var vClient = vHost.CreateClient();

        // Sign in for real.
        var vToken = ReadAntiforgeryToken(await vClient.GetStringAsync("/login"));
        Assert.False(string.IsNullOrEmpty(vToken), "/login rendered no antiforgery token.");

        var vLogin = await vClient.PostAsync("/auth/login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = vToken!,
                ["Email"] = DemoEmail,
                ["Password"] = DemoPassword,
                ["ReturnUrl"] = "/repos"
            }));

        var vLanding = vLogin.Headers.Location?.ToString() ?? string.Empty;
        Assert.True(
            vLogin.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found
            && !vLanding.StartsWith("/login", StringComparison.Ordinal),
            $"Sign-in did not succeed: {(int)vLogin.StatusCode} -> '{vLanding}'.");

        (await vClient.GetAsync("/repos")).IsSuccessStatusCode
            .Should().BeTrue("the signed-in user must be able to reach /repos before signing out");

        // Sign out through the endpoint the user menu posts to.
        var vLogoutToken = ReadAntiforgeryToken(await vClient.GetStringAsync("/repos"));
        Assert.False(
            string.IsNullOrEmpty(vLogoutToken),
            "REQ-UI-008 — the shell renders no antiforgery token, so the sign-out form cannot post. " +
            "The hidden form in UserMenu.razor is what makes sign-out possible at all.");

        var vLogout = await vClient.PostAsync("/auth/logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = vLogoutToken! }));

        vLogout.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Redirect, HttpStatusCode.Found],
            "sign-out redirects the browser once the cookie is cleared");

        // The session must now be gone: an authenticated page has to bounce to /login.
        var vAfter = await vClient.GetAsync("/repos");

        vAfter.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.Unauthorized],
            "REQ-FN-007 — after signing out, /repos must no longer be reachable. If this returns 200 " +
            "the cookie survived and the user is still signed in.");

        (vAfter.Headers.Location?.ToString() ?? string.Empty)
            .Should().Contain("/login");
    }

    /// <summary>
    /// Sign-out is not reachable by a plain GET, so a third-party page cannot trigger it.
    /// </summary>
    /// <remarks>
    /// A GET sign-out can be fired by any page embedding <c>&lt;img src="/auth/logout"&gt;</c>. Logging
    /// someone out unbidden is a nuisance rather than a compromise, but the fix costs nothing: the
    /// endpoint is POST-only and carries the antiforgery token.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task SignOutIsNotReachableByGet()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is not reachable: {objDb.UnavailableReason}");

        await using var vHost = new TfLensTestHost();
        Assert.True(vHost.TryGetServices(out var vWhyNot) is not null, $"Host did not start: {vWhyNot}");

        using var vClient = vHost.CreateClient();

        var vResponse = await vClient.GetAsync("/auth/logout");

        vResponse.StatusCode.Should().NotBe(
            HttpStatusCode.OK,
            "a GET must not perform a sign-out");
    }

    /// <summary>Reads the antiforgery token out of rendered markup.</summary>
    /// <param name="aHtml">The page markup.</param>
    /// <returns>The token, or <c>null</c>.</returns>
    private static string? ReadAntiforgeryToken(string aHtml)
    {
        var vMatch = Regex.Match(
            aHtml,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""");

        return vMatch.Success ? vMatch.Groups[1].Value : null;
    }
}
