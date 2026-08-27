namespace TfLens.Services.Ui;

/// <summary>
/// The dark-first theme preference, persisted per user in a cookie.
/// </summary>
/// <remarks>
/// ADR-014 — dark is the default, so the absence of a cookie means dark rather than light. The value is
/// read in <c>App.razor</c> before the first byte, which is why it lives in a cookie rather than in
/// browser storage a server-rendered page cannot see.
/// </remarks>
public static class ThemeState
{
    /// <summary>
    /// Cookie the preference is stored in.
    /// </summary>
    /// <remarks>
    /// The separator is a hyphen, not a colon, and that is load-bearing. A cookie <i>name</i> is an
    /// RFC 6265 token, in which <c>:</c> is a separator character, so ASP.NET Core's parser silently
    /// drops any request cookie containing one — verified directly: sending
    /// <c>tflens:theme=light; tflens-theme=light; plain=1</c> to a minimal API yields only
    /// <c>tflens-theme=light | plain=1</c>. The browser stores and sends the colon form quite happily,
    /// so the failure is invisible from the client side. Under the original <c>tflens:theme</c> name a
    /// light preference could never reach the server, and <c>App.razor</c>'s first-byte theme
    /// resolution therefore rendered dark on every fresh load no matter what the user had chosen —
    /// BRD-85 / ADR-014 broken with no error anywhere. The same trap applied to the framework switch
    /// and the sidebar state.
    /// </remarks>
    public const string CookieName = "tflens-theme";

    /// <summary>The class applied to <c>&lt;html&gt;</c> for the dark palette.</summary>
    public const string Dark = "dark";

    /// <summary>The stored value meaning the user chose the light palette.</summary>
    public const string Light = "light";
}

/// <summary>
/// The framework the report pages are showing, persisted per user.
/// </summary>
/// <remarks>
/// ADR-016 — framework is a provenance axis, so the switch re-queries every figure rather than
/// filtering client-side. The selection is a cookie so it survives a reload and a reconnect.
/// </remarks>
public static class FrameworkState
{
    /// <summary>Cookie the selection is stored in.</summary>
    public const string CookieName = "tflens-framework";
}

/// <summary>
/// The sidebar's collapsed/expanded preference, persisted per user.
/// </summary>
/// <remarks>
/// Named <c>SidebarPreference</c> rather than <c>SidebarState</c> because TrBlazeUI ships its own
/// <c>TrBlazeUI.Components.Sidebar.SidebarState</c>, and both namespaces are imported globally in
/// <c>_Imports.razor</c> — the shorter name would be an ambiguous reference in every component that
/// touched it.
/// </remarks>
public static class SidebarPreference
{
    /// <summary>Cookie key handed to TrBlazeUI's <c>SidebarProvider</c>.</summary>
    public const string CookieName = "tflens-sidebar";
}
