using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using TfLens.Components;
using TfLens.Configuration;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Services.Auth;
using TfLens.Services.Commands;
using TfLens.Services.Export;
using TfLens.Services.Metrics;
using TfLens.Services.Playbook;
using TfLens.Services.Repos;
using TfLens.Services.Storage;
using TfLens.Services.Sync;
using TfLens.Services.Ui;
using TrBlazeUI.Components.Toast;
using TrBlazeUI.Primitives.Extensions;

// Serilog is wired before anything else can fail, so a startup exception still reaches a file.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/tflens-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();

AppDomain.CurrentDomain.UnhandledException += (aSender, aArgs) =>
    Log.Fatal(aArgs.ExceptionObject as Exception, "Unhandled exception at the AppDomain boundary");

TaskScheduler.UnobservedTaskException += (aSender, aArgs) =>
{
    Log.Error(aArgs.Exception, "Unobserved task exception");
    aArgs.SetObserved();
};

try
{
    var vBuilder = WebApplication.CreateBuilder(args);

    // PascalCase env vars (TfLensDbConnection) map onto TfLens:* config paths — Coding Standards
    // §Environment Variables. Application code never reads the environment directly.
    vBuilder.Configuration.AddPascalCaseEnvironmentVariables();
    vBuilder.Host.UseSerilog();

    vBuilder.Services.Configure<TfLensOptions>(vBuilder.Configuration.GetSection(TfLensOptions.SectionName));

    var vOptions = new TfLensOptions();
    vBuilder.Configuration.GetSection(TfLensOptions.SectionName).Bind(vOptions);

    // BRD-9: refuse to start on a missing secret rather than failing at the first user's sign-in.
    vOptions.Validate();

    vBuilder.Services.AddRazorComponents().AddInteractiveServerComponents();
    vBuilder.Services.AddCascadingAuthenticationState();
    vBuilder.Services.AddHttpContextAccessor();
    vBuilder.Services.AddMemoryCache();

    // The AuthSession token columns are encrypted with Data Protection before they are written, and
    // the auth cookie is protected with the same key ring. The default key location is inside the
    // container filesystem, so a restart would issue a fresh ring and orphan every stored token —
    // the keys therefore live on the data volume alongside the raw archive (BRD-87).
    vBuilder.Services.AddDataProtection()
        .SetApplicationName("TfLens")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(vOptions.DataRoot, "keys")));

    vBuilder.Services.AddAntiforgery();

    vBuilder.Services.AddTrBlazeUIPrimitives();
    vBuilder.Services.AddScoped<ToastService>();

    vBuilder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(aCookie =>
        {
            aCookie.Cookie.Name = "TfLensAuth";
            aCookie.Cookie.HttpOnly = true;
            aCookie.Cookie.SameSite = SameSiteMode.Lax;

            // BRD-83 wants the Secure flag. Outside development that is unconditional: TLS is
            // terminated by the proxy and the forwarded scheme is honoured, so `Always` is correct
            // even though the container itself speaks plain HTTP. In development the app is reached
            // over http://localhost, where `Always` would simply stop the cookie being stored.
            aCookie.Cookie.SecurePolicy = vBuilder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

            aCookie.LoginPath = "/login";
            aCookie.LogoutPath = "/signout";
            aCookie.AccessDeniedPath = "/login";

            // BRD-93 fixes the session at a sliding 12 hours.
            aCookie.ExpireTimeSpan = TimeSpan.FromHours(12);
            aCookie.SlidingExpiration = true;
        });

    vBuilder.Services.AddAuthorization();

    // Each area owns its own registration file so parallel work never collides in Program.cs.
    vBuilder.Services.AddTfLensStorage(vBuilder.Configuration);
    vBuilder.Services.AddTfLensAuth(vBuilder.Configuration);
    vBuilder.Services.AddTfLensRepos(vBuilder.Configuration);
    vBuilder.Services.AddTfLensSync(vBuilder.Configuration);
    vBuilder.Services.AddTfLensMetrics(vBuilder.Configuration);
    vBuilder.Services.AddTfLensExport(vBuilder.Configuration);
    vBuilder.Services.AddTfLensPlaybook(vBuilder.Configuration);
    vBuilder.Services.AddTfLensUiState(vBuilder.Configuration);

    var vApp = vBuilder.Build();

    // BRD-83: the container terminates TLS behind a reverse proxy, so the real scheme and client IP
    // arrive in forwarded headers.
    var vForwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };

    // The defaults trust only loopback, so in a container the proxy's headers would be silently
    // dropped and every request would look like plain HTTP from another container's IP — which turns
    // `CookieSecurePolicy.Always` into a cookie that is never issued. TfLens is only ever reachable
    // through its proxy, so the proxy is trusted explicitly.
    vForwardedHeaders.KnownIPNetworks.Clear();
    vForwardedHeaders.KnownProxies.Clear();

    vApp.UseForwardedHeaders(vForwardedHeaders);
    vApp.UseTfLensSecurityHeaders();

    // The schema script is idempotent and the store is disposable — applying it at startup is the
    // whole migration story (ADR-015).
    await using (var vScope = vApp.Services.CreateAsyncScope())
    {
        var vStore = vScope.ServiceProvider.GetRequiredService<ITelemetryStore>();
        await vStore.EnsureSchemaAsync();

        if (!await vStore.PingAsync())
        {
            throw new InvalidOperationException(
                "TfLens cannot start — the database named by TfLensDbConnection is unreachable.");
        }
    }

    // `dotnet TfLens.dll sync|rebuild|export` runs a verb and exits, sharing the engine the pages use
    // so a parity run exercises exactly the production code path (ADR-005).
    if (args.Length > 0 && CommandRunner.IsVerb(args[0]))
    {
        var vExitCode = await CommandRunner.RunAsync(vApp.Services, args);
        Log.CloseAndFlush();
        return vExitCode;
    }

    if (!vApp.Environment.IsDevelopment())
    {
        vApp.UseExceptionHandler("/Error", createScopeForErrors: true);
        vApp.UseHsts();
    }

    vApp.UseStaticFiles();
    vApp.UseAuthentication();
    vApp.UseAuthorization();

    // UseAntiforgery must run after authentication and authorization so a token is validated against
    // the identity that actually owns it (BRD-83).
    vApp.UseAntiforgery();

    // BRD-78: anonymous liveness, reporting DB reachability and the age of the last successful sync.
    // No metrics, no version and no configuration are exposed here.
    vApp.MapHealthEndpoint();

    vApp.MapAuthEndpoints();

    // Snapshot downloads. The endpoint takes no user id: it derives the reports root from the auth
    // cookie, so one user cannot fetch another's snapshot by editing a query string (ADR-013).
    vApp.MapExportEndpoints();

    vApp.MapRazorComponents<App>().AddInteractiveServerRenderMode();

    await vApp.RunAsync();
    return 0;
}
catch (Exception vEx)
{
    Log.Fatal(vEx, "TfLens terminated unexpectedly during startup");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Marker type so integration tests can reference the head's entry assembly.</summary>
public partial class Program;
