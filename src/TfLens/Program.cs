using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration.Memory;
using Serilog;
using TfLens.Components;
using TfLens.Configuration;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Services.Auth;
using TfLens.Services.Commands;
using TfLens.Services.Export;
using TfLens.Services.Import;
using TfLens.Services.Metrics;
using TfLens.Services.Playbook;
using TfLens.Services.Repos;
using TfLens.Services.Storage;
using TfLens.Services.Sync;
using TfLens.Services.Ui;
using TrBlazeUI.Components.Toast;
using TrBlazeUI.Primitives.Extensions;

// Serilog is wired before anything else can fail, so a startup exception still reaches a file.
//
// The reset-token redaction is attached here, at the logger itself, rather than anywhere in the
// request pipeline: the line that leaked a live password-reset link came from ASP.NET Core's own
// hosting diagnostics, which runs before any TfLens middleware and logs the whole request URL,
// query string included (BRD-92).
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.With(new ResetTokenRedaction())
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

    // REMOVED 2026-08-29 (owner report, MISS-TfLens-20260829-23): a Development-only default that
    // seeded `TfLens:DbConnection` from a connection string held as a `const` in TfLensOptions,
    // with its password inline.
    //
    // Two things were wrong with it, and the second is the expensive one.
    //
    // 1. It put a credential in committed source. It was argued to be safe because the password is a
    //    throwaway already published in `.env.example`. That reasoning is how credentials normally get
    //    into repositories: the exception is always locally true and never stays local.
    // 2. It silently PINNED local development to one specific database — the compose container on
    //    port 5433 — so a developer who never configured anything got a working app pointed at a
    //    server they never chose. That is exactly what happened: an agent brought up a second
    //    PostgreSQL container beside the machine's real local dev server (`WinPostgre`, port 5550,
    //    which also hosts `AppMngrDb`), stopped that server, and nothing ever surfaced the switch,
    //    because the fallback made the wrong database look like a correct default.
    //
    // There is now NO database default in any environment. `TfLens:DbConnection` comes from user
    // secrets in development and from the `TfLensDbConnection` environment variable in deployment, and
    // a missing value fails fast at startup (BRD-9) with a message naming the two ways to supply it.
    // A default nobody chose is worse than an error somebody reads.

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
            // Nothing maps /signout; sign-out is POST /auth/logout (UserMenu posts it). This value only
            // affects framework-generated logout links, so it points at a route that exists.
            aCookie.LogoutPath = "/login";
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
    vBuilder.Services.AddTfLensImport(vBuilder.Configuration);
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

        // Applying the schema is the first thing that actually opens a connection, so it — not the
        // ping below — is where an unreachable database surfaces. Left unhandled it surfaces as a raw
        // Npgsql socket stack trace, which tells a developer nothing about what to start or where to
        // look; the guidance below is the whole point of failing at startup rather than later.
        try
        {
            await vStore.EnsureSchemaAsync();

            if (!await vStore.PingAsync())
            {
                throw new InvalidOperationException(TfLensOptions.UnreachableDatabaseMessage(null));
            }
        }
        catch (Exception vDbEx) when (vDbEx is not InvalidOperationException)
        {
            throw new InvalidOperationException(TfLensOptions.UnreachableDatabaseMessage(vDbEx), vDbEx);
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

    // The Import-metric-files mode of the Add-source dialog. Both routes require authentication and
    // take no user id, so the only archive either can write into is the caller's own (REQ-NFR-014).
    vApp.MapImportEndpoints();

    // REQ-NFR-015, 2026-09-01 — this replaces `UseStaticFiles()`, and the replacement is the fix for a
    // defect the owner hit twice.
    //
    // `UseStaticFiles` answers a stylesheet with `ETag` + `Last-Modified` and NO `Cache-Control` at all.
    // A browser handed no directive falls back to heuristic caching and reuses its copy without asking,
    // so after a rebuild the owner's browser kept serving itself the PREVIOUS `TfLens.styles.css`: on
    // 2026-08-28 `/login` collapsed to an unstyled column, and on 2026-08-30 `/harness` rendered as three
    // full-width stacked tables with the raw `Metric | Value` headers the scoped CSS hides. Both times the
    // server was correct, every asset answered 200, and `asset-integrity.spec.ts` — which fetches in a
    // fresh context — passed while the screen in front of the owner was broken. A guard that cannot see
    // the client's cache cannot certify the client's render.
    //
    // `MapStaticAssets` closes it at the source rather than by testing harder: `@Assets[...]` resolves to
    // a CONTENT-FINGERPRINTED url, so a rebuilt file is a different url and the old entry can never be
    // matched; and the unfingerprinted path (the colocated `*.razor.js` modules import theirs by name)
    // is answered `Cache-Control: no-cache`, which forces revalidation instead of silent reuse. It also
    // pre-compresses, which `UseStaticFiles` did not.
    // `.AllowAnonymous()` is not optional here and the smoke found out why. `UseStaticFiles` was
    // MIDDLEWARE, running before authorization, so a stylesheet was always served anonymously.
    // `MapStaticAssets` registers ENDPOINTS, and endpoints inherit BRD-2's fallback policy — so the very
    // first run of this change answered `AuthForms.<hash>.razor.js` with a 302 to /login for an anonymous
    // visitor, and the sign-in form's own JS module failed to import. Nothing under wwwroot or in a
    // colocated asset is user data or a secret (REQ-FN-040 keeps secrets out of files entirely), so
    // anonymous is what these endpoints are for; BRD-2's protection belongs to the routes, which keep it.
    vApp.MapStaticAssets().AllowAnonymous();

    vApp.MapRazorComponents<App>().AddInteractiveServerRenderMode();

    await vApp.RunAsync();
    return 0;
}
catch (IOException vBindEx) when (vBindEx.InnerException is AddressInUseException)
{
    // "address already in use" arrives as a 40-line Kestrel/socket stack trace that never names the
    // port, the app already holding it, or how to free it. The usual cause is mundane — a previous
    // debug session, a `dotnet run` in another terminal, or a container publishing the same port —
    // and all a developer needs is which command to run.
    Log.Fatal(
        """
        TfLens cannot start — the port it listens on is already in use.

        {Detail}

        Something else is already bound to that address. Almost always one of:

          1. An earlier run of TfLens is still alive — a previous F5 session that did not shut down, or
             a `dotnet run` in another terminal. Stop it and start again.

          2. A container is publishing the same port. `docker ps` will show it; the compose stack
             publishes the app on 8080, so it does not normally collide with the 5014 dev port.

          3. Something unrelated owns the port:
               Windows: netstat -ano | findstr :5014     then  taskkill /PID <pid> /F
               Linux/macOS: lsof -i :5014                then  kill -9 <pid>

        To use a different port instead, change applicationUrl in
        src/TfLens/Properties/launchSettings.json, or run with:
          dotnet run --project src/TfLens --urls http://localhost:5099
        """,
        vBindEx.Message);

    return 1;
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
