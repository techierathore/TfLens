namespace TfLens.Services.Ui;

/// <summary>
/// One entry in the shell's sidebar navigation.
/// </summary>
/// <param name="Href">Route the item navigates to.</param>
/// <param name="Label">Text shown beside the icon, and the collapsed-rail tooltip.</param>
/// <param name="Icon">Lucide icon name, kebab-case.</param>
/// <param name="Section">Breadcrumb section the route belongs to.</param>
/// <param name="IsExact">True when the route only matches exactly — the Coverage landing route <c>/</c>.</param>
/// <param name="HasFrameworkSwitch">True when the header Framework switch renders on this route (REQ-UI-010).</param>
public sealed record ShellNavItem(
    string Href,
    string Label,
    string Icon,
    string Section,
    bool IsExact,
    bool HasFrameworkSwitch);

/// <summary>
/// The fixed navigation of the app shell (REQ-UI-006).
/// </summary>
/// <remarks>
/// The order, the labels and the Lucide icon names are acceptance criteria, not styling — they live in
/// one list so the sidebar, the breadcrumb and the Framework-switch visibility rule cannot drift apart.
/// There is deliberately no <c>/playbook</c> item: the framework is chosen in the header (BRD-108).
/// </remarks>
public static class ShellNavigation
{
    /// <summary>Breadcrumb section for the repo-management route.</summary>
    public const string WorkspaceSection = "Workspace";

    /// <summary>Breadcrumb section for the five report routes.</summary>
    public const string ReportsSection = "Reports";

    /// <summary>Breadcrumb section for the profile route, which has no sidebar item.</summary>
    public const string AccountSection = "Account";

    /// <summary>
    /// The six navigation items, in the fixed working order the checklist asserts.
    /// </summary>
    public static readonly IReadOnlyList<ShellNavItem> Items =
    [
        new ShellNavItem("/repos", "Repos", "git-branch", WorkspaceSection, false, false),
        new ShellNavItem("/", "Coverage / health", "activity", ReportsSection, true, true),
        new ShellNavItem("/three-questions", "Three questions", "circle-question-mark", ReportsSection, false, true),
        new ShellNavItem("/harness", "Harness comparison", "git-compare", ReportsSection, false, true),
        new ShellNavItem("/routing", "Routing & economics", "route", ReportsSection, false, true),
        new ShellNavItem("/export", "Snapshot export", "download", ReportsSection, false, true)
    ];

    /// <summary>Routes that carry a breadcrumb but no sidebar item.</summary>
    private static readonly IReadOnlyDictionary<string, (string Section, string Page)> ExtraCrumbs =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["profile"] = (AccountSection, "Profile")
        };

    /// <summary>
    /// Normalises a URI into the bare route segment the shell reasons about.
    /// </summary>
    /// <param name="aRelativePath">A base-relative path, possibly carrying a query string or a trailing slash.</param>
    /// <returns>The route segment without leading or trailing slashes and without the query.</returns>
    public static string Normalise(string aRelativePath)
    {
        var vPath = aRelativePath ?? string.Empty;
        var vQueryAt = vPath.IndexOf('?', StringComparison.Ordinal);

        if (vQueryAt >= 0)
        {
            vPath = vPath[..vQueryAt];
        }

        return vPath.Trim('/');
    }

    /// <summary>
    /// Finds the navigation item a route belongs to.
    /// </summary>
    /// <param name="aRelativePath">A base-relative path.</param>
    /// <returns>The matching item, or <c>null</c> for a route with no sidebar entry.</returns>
    public static ShellNavItem? Match(string aRelativePath)
    {
        var vPath = Normalise(aRelativePath);

        if (vPath.Length == 0)
        {
            return Items.First(aItem => aItem.IsExact);
        }

        return Items.FirstOrDefault(aItem =>
            !aItem.IsExact &&
            vPath.StartsWith(aItem.Href.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the header breadcrumb for a route.
    /// </summary>
    /// <param name="aRelativePath">A base-relative path.</param>
    /// <returns>The section and page names shown as "section › page".</returns>
    public static (string Section, string Page) Breadcrumb(string aRelativePath)
    {
        var vItem = Match(aRelativePath);

        if (vItem is not null)
        {
            return (vItem.Section, vItem.Label);
        }

        var vPath = Normalise(aRelativePath);

        if (ExtraCrumbs.TryGetValue(vPath, out var vCrumb))
        {
            return vCrumb;
        }

        return (WorkspaceSection, vPath.Length == 0 ? "Home" : vPath);
    }

    /// <summary>
    /// Decides whether the header Framework switch renders on a route (REQ-UI-010).
    /// </summary>
    /// <param name="aRelativePath">A base-relative path.</param>
    /// <returns><c>true</c> on the five report routes and nowhere else.</returns>
    public static bool ShowsFrameworkSwitch(string aRelativePath) =>
        Match(aRelativePath)?.HasFrameworkSwitch == true;
}
