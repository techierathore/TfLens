using Microsoft.JSInterop;
using TfLens.Core.Contracts;

namespace TfLens.Services.Ui;

/// <summary>
/// The two per-user shell preferences — the dark-first theme and the Framework switch.
/// </summary>
/// <remarks>
/// Both live in cookies rather than browser storage because <c>App.razor</c> has to know the theme before
/// the first byte is written (ADR-014) and the report pages have to know the framework before they query
/// (ADR-016). This type is the only writer: it mirrors the value into the cookie through
/// <c>window.tflens</c> and then raises <see cref="Changed"/> so the pages re-query rather than filter
/// what they already rendered.
/// </remarks>
public sealed class ShellPreferences
{
    private readonly IJSRuntime objJsRuntime;

    /// <summary>
    /// Creates the preference holder, seeding it from the request's cookies when there is a request.
    /// </summary>
    /// <param name="aJsRuntime">Used to write the preference cookies and flip the theme class.</param>
    /// <param name="aHttpContextAccessor">Supplies the cookies of the request that opened the circuit.</param>
    public ShellPreferences(IJSRuntime aJsRuntime, IHttpContextAccessor aHttpContextAccessor)
    {
        objJsRuntime = aJsRuntime;

        var vCookies = aHttpContextAccessor.HttpContext?.Request.Cookies;

        // ADR-014: no cookie means dark, so only an explicit "light" turns the toggle off.
        IsDark = vCookies?[ThemeState.CookieName] != ThemeState.Light;

        var vFramework = vCookies?[FrameworkState.CookieName];

        Framework = FrameworkNames.All.Contains(vFramework, StringComparer.OrdinalIgnoreCase)
            ? vFramework!.ToLowerInvariant()
            : FrameworkNames.TechieFlow;
    }

    /// <summary>Raised when either preference changes, so the shell and the current page re-render.</summary>
    public event Action? Changed;

    /// <summary>True when the dark palette is active — the state of the header theme toggle.</summary>
    public bool IsDark { get; private set; }

    /// <summary>The framework the report pages are querying (REQ-UI-010).</summary>
    public string Framework { get; private set; }

    /// <summary>
    /// Applies the theme choice — flips <c>&lt;html class="dark"&gt;</c> and persists the cookie.
    /// </summary>
    /// <param name="aIsDark"><c>true</c> for the dark palette.</param>
    /// <returns>A task that completes once the browser has been told.</returns>
    public async Task SetThemeAsync(bool aIsDark)
    {
        IsDark = aIsDark;
        await objJsRuntime.InvokeVoidAsync("tflens.setTheme", aIsDark).ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>
    /// Applies the Framework-switch choice and persists the cookie.
    /// </summary>
    /// <param name="aFramework">One of <see cref="FrameworkNames.TechieFlow"/> or <see cref="FrameworkNames.Playbook"/>.</param>
    /// <returns>A task that completes once the browser has been told.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The framework is not one of the two provenance axes.</exception>
    public async Task SetFrameworkAsync(string aFramework)
    {
        if (!FrameworkNames.All.Contains(aFramework, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(aFramework), aFramework, "Unknown framework.");
        }

        Framework = aFramework.ToLowerInvariant();

        await objJsRuntime
            .InvokeVoidAsync("tflens.setPreference", FrameworkState.CookieName, Framework)
            .ConfigureAwait(false);

        Changed?.Invoke();
    }

    /// <summary>
    /// Recovers the persisted Framework choice from the browser once the circuit can talk to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The constructor seeds <see cref="Framework"/> from <c>IHttpContextAccessor</c>, which works for the
    /// static-rendered host page but is <b>null inside an interactive Blazor Server circuit</b> — the
    /// circuit outlives the request that created it, so the server genuinely cannot see the request's
    /// cookies. Without this method the switch always resolved to <c>techieflow</c>: selecting Playbook
    /// wrote the cookie, the page re-queried correctly, and then the next circuit read no cookie at all
    /// and silently fell back, so the choice never survived a navigation (REQ-UI-010).
    /// </para>
    /// <para>
    /// The browser is therefore the authority here, and this is called from the shell's first render.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes once the switch matches the persisted choice.</returns>
    public async Task SyncFrameworkFromBrowserAsync()
    {
        string? vStored;

        try
        {
            vStored = await objJsRuntime
                .InvokeAsync<string?>("tflens.getPreference", FrameworkState.CookieName)
                .ConfigureAwait(false);
        }
        catch (JSException)
        {
            // Prerendering, or the circuit went away mid-call. The seeded default stands.
            return;
        }

        if (!FrameworkNames.All.Contains(vStored, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var vFramework = vStored!.ToLowerInvariant();
        if (vFramework == Framework)
        {
            return;
        }

        Framework = vFramework;
        Changed?.Invoke();
    }

    /// <summary>
    /// Reconciles the toggle with what the browser actually rendered.
    /// </summary>
    /// <remarks>
    /// The theme class is resolved server-side in <c>App.razor</c>; this only corrects the toggle's position
    /// when the circuit could not see the request's cookies, so the switch never contradicts the page.
    /// </remarks>
    /// <returns>A task that completes once the toggle matches the document.</returns>
    public async Task SyncThemeFromBrowserAsync()
    {
        var vIsDark = await objJsRuntime
            .InvokeAsync<bool>("document.documentElement.classList.contains", ThemeState.Dark)
            .ConfigureAwait(false);

        if (vIsDark == IsDark)
        {
            return;
        }

        IsDark = vIsDark;
        Changed?.Invoke();
    }
}
