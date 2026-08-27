using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.AppManager;

namespace TfLens.Services.Auth;

/// <summary>
/// Registers the AppManager identity and session services with the container.
/// </summary>
/// <remarks>
/// One registration file per area keeps <c>Program.cs</c> stable while the areas are built in
/// parallel: an area adds its own services here and nowhere else. This file is also where the
/// authorization convention lives (REQ-FN-005) — it post-configures the fallback policy rather than
/// touching the <c>AddAuthorization()</c> call in <c>Program.cs</c>.
/// </remarks>
public static class AuthRegistration
{
    /// <summary>How long an AppManager call may take before it is abandoned.</summary>
    private static readonly TimeSpan AppManagerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Adds the AppManager identity and session services.
    /// </summary>
    /// <param name="aServices">The service collection.</param>
    /// <param name="aConfiguration">Application configuration, already carrying the PascalCase environment values.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The AppManager base URL and application id come from configuration only, with the documented
    /// defaults (REQ-FN-010); the API-key pair is never read here, because
    /// <see cref="AppManagerClient"/> decides per request whether it may be sent at all.
    /// </remarks>
    public static IServiceCollection AddTfLensAuth(this IServiceCollection aServices, IConfiguration aConfiguration)
    {
        aServices.AddHttpContextAccessor();

        aServices.AddHttpClient<IAppManagerClient, AppManagerClient>(ConfigureAppManagerClient);

        aServices.AddScoped<IAuthSessionStore, AuthSessionStore>();
        aServices.AddScoped<AuthService>();
        aServices.AddScoped<CurrentUser>();

        AddAuthorizationConvention(aServices);
        aServices.PostConfigure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme,
            aOptions => aOptions.ReturnUrlParameter = "returnUrl");

        return aServices;
    }

    /// <summary>
    /// Points the typed client at the configured AppManager instance.
    /// </summary>
    /// <param name="aProvider">Supplies the bound <see cref="TfLensOptions"/>.</param>
    /// <param name="aClient">The client being configured.</param>
    private static void ConfigureAppManagerClient(IServiceProvider aProvider, HttpClient aClient)
    {
        var vOptions = aProvider.GetRequiredService<IOptions<TfLensOptions>>().Value;

        aClient.BaseAddress = new Uri(vOptions.AppManagerBaseUrl.TrimEnd('/'));
        aClient.Timeout = AppManagerTimeout;
        aClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Makes authentication the default for every endpoint, with a fixed list of exceptions.
    /// </summary>
    /// <param name="aServices">The service collection.</param>
    /// <remarks>
    /// <para>
    /// REQ-FN-005 / BRD-2. A fallback policy applies to every endpoint that carries no authorization
    /// metadata of its own, so a page added later is protected by default and a forgotten
    /// <c>[Authorize]</c> can no longer expose one. The exceptions are the five anonymous routes in
    /// <see cref="AnonymousRoutes"/>, tested by path rather than by attribute so that a Razor component
    /// does not have to remember <c>[AllowAnonymous]</c> to stay reachable.
    /// </para>
    /// <para>
    /// The path has to come from <see cref="IHttpContextAccessor"/>: the authorization middleware hands
    /// the policy the matched <c>Endpoint</c> as its resource, not the request.
    /// </para>
    /// </remarks>
    private static void AddAuthorizationConvention(IServiceCollection aServices)
    {
        aServices.AddOptions<AuthorizationOptions>()
            .PostConfigure<IHttpContextAccessor>((aOptions, aHttpContextAccessor) =>
            {
                aOptions.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAssertion(aContext =>
                        aContext.User.Identity?.IsAuthenticated == true
                        || AnonymousRoutes.IsAnonymous(
                            aHttpContextAccessor.HttpContext?.Request.Path ?? default))
                    .Build();
            });
    }
}
