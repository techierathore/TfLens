using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TfLens.Integration.Tests;

/// <summary>
/// REQ-NFR-002 (BRD-83) checked against the container the application actually builds, not against
/// the source that configures it.
/// </summary>
/// <remarks>
/// The static checks in the guardrails project prove the right lines were written. These prove the
/// lines took effect — a later <c>Configure&lt;CookieAuthenticationOptions&gt;</c> anywhere in the
/// registration chain could quietly override any of them, and only the resolved options show that.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SecurityRuntimeTests
{
    private readonly PostgresFixture objDb;

    /// <summary>Creates the test class.</summary>
    /// <param name="aDb">The shared fixture, which also sets the connection string the host reads.</param>
    public SecurityRuntimeTests(PostgresFixture aDb)
    {
        objDb = aDb;
    }

    /// <summary>The resolved auth cookie carries HttpOnly, SameSite=Lax and a Secure policy.</summary>
    [Fact]
    [Trait("Category", "Blocked")]
    public void TheResolvedAuthCookieCarriesTheRequiredFlags()
    {
        var vServices = RequireHost();

        var vOptions = vServices
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.True(vOptions.Cookie.HttpOnly, "REQ-NFR-002 — the auth cookie is not HttpOnly.");
        Assert.Equal(SameSiteMode.Lax, vOptions.Cookie.SameSite);
        Assert.NotEqual(CookieSecurePolicy.None, vOptions.Cookie.SecurePolicy);
        Assert.Equal(TimeSpan.FromHours(12), vOptions.ExpireTimeSpan);
        Assert.True(vOptions.SlidingExpiration, "BRD-93 — the session must slide.");
        Assert.Equal("/login", vOptions.LoginPath.Value);
    }

    /// <summary>
    /// <c>/healthz</c> answers anonymously, reports the two permitted facts and nothing more.
    /// </summary>
    /// <remarks>REQ-FN-041's whole acceptance in one assertion set.</remarks>
    [Fact]
    [Trait("Category", "Blocked")]
    public async Task HealthzIsAnonymousAndDisclosesOnlyTheTwoPermittedFacts()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is unreachable: {objDb.UnavailableReason}");

        await using var vHost = new TfLensTestHost();
        var vServices = vHost.TryGetServices(out var vWhyNot);
        Assert.True(vServices is not null, $"The host could not be built. Reason: {vWhyNot}");

        using var vClient = vHost.CreateClient();
        using var vResponse = await vClient.GetAsync("/healthz");

        Assert.Equal(200, (int)vResponse.StatusCode);

        var vBody = await vResponse.Content.ReadAsStringAsync();

        using var vJson = System.Text.Json.JsonDocument.Parse(vBody);
        var vKeys = vJson.RootElement.EnumerateObject().Select(aProperty => aProperty.Name).OrderBy(aName => aName).ToArray();

        Assert.Equal(new[] { "database", "lastSuccessfulSyncAgeSeconds", "status" }, vKeys);

        // Nothing about the deployment escapes through the probe.
        foreach (var vForbidden in new[] { "Version", "Host=", "Password", "techierathore", "appmgr" })
        {
            Assert.DoesNotContain(vForbidden, vBody, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("X-Frame-Options", vResponse.Headers.Select(aHeader => aHeader.Key));
        Assert.Contains("X-Content-Type-Options", vResponse.Headers.Select(aHeader => aHeader.Key));
    }

    /// <summary>Builds the host or fails with the reason it could not be built.</summary>
    /// <returns>The root service provider.</returns>
    private IServiceProvider RequireHost()
    {
        Assert.True(objDb.IsAvailable, $"PostgreSQL is unreachable: {objDb.UnavailableReason}");

        var vHost = new TfLensTestHost();
        var vServices = vHost.TryGetServices(out var vWhyNot);

        Assert.True(vServices is not null, $"The host could not be built. Reason: {vWhyNot}");

        return vServices!;
    }
}
