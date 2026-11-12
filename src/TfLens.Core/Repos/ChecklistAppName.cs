namespace TfLens.Core.Repos;

/// <summary>
/// Names an app from a repository's checklist file, exactly as <c>app_name()</c> in
/// <c>tf-metrics.sh</c> does (BRD-220, REQ-FN-152, ADR-036).
/// </summary>
/// <remarks>
/// <para>
/// The script globs <c>docs/*-Checklist.md</c> — the files directly inside <c>docs/</c> whose name ends
/// <c>-Checklist.md</c> — and names the app from the one hit when there is exactly one; otherwise it
/// falls back to the repository folder's name. The glob is case-sensitive and not recursive, and so is
/// this. A repository with phase checklists (<c>App-P2-Checklist.md</c>) beside its own therefore has
/// several hits and no checklist name, as it has for the script.
/// </para>
/// <para>Pure: it reads names it is given and touches no network and no disk.</para>
/// </remarks>
public static class ChecklistAppName
{
    /// <summary>The file-name ending the script globs for.</summary>
    public const string Suffix = "-Checklist.md";

    /// <summary>The folder the script globs in.</summary>
    public const string Folder = "docs";

    /// <summary>
    /// The app name from the file names directly inside <c>docs/</c>.
    /// </summary>
    /// <param name="aFileNames">The bare file names in the folder.</param>
    /// <returns>The name before <c>-Checklist.md</c> when exactly one file matches, else <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aFileNames"/> is <c>null</c>.</exception>
    public static string? FromFileNames(IEnumerable<string> aFileNames)
    {
        ArgumentNullException.ThrowIfNull(aFileNames);

        var vHits = aFileNames
            .Where(aName => aName.Length > Suffix.Length && aName.EndsWith(Suffix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return vHits.Count == 1 ? vHits[0][..^Suffix.Length] : null;
    }

    /// <summary>
    /// Reads the app name from the entry paths of an imported bundle, whose root is the repository's root.
    /// </summary>
    /// <remarks>
    /// Only entries sitting directly in <c>docs/</c> count, as in the glob: <c>docs/App-Checklist.md</c>
    /// does, <c>docs/archive/App-Checklist.md</c> does not. A bundle with no checklist entry at all says
    /// nothing about the name, which is told apart from one with several (a name of <c>null</c>).
    /// </remarks>
    /// <param name="aEntryPaths">The bundle's entry paths, with <c>/</c> or <c>\</c> separators.</param>
    /// <param name="aAppName">The app name, or <c>null</c> when several checklists match.</param>
    /// <returns><c>true</c> when the bundle carries at least one checklist entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aEntryPaths"/> is <c>null</c>.</exception>
    public static bool TryFromBundleEntries(IEnumerable<string> aEntryPaths, out string? aAppName)
    {
        ArgumentNullException.ThrowIfNull(aEntryPaths);

        var vInDocs = aEntryPaths
            .Select(aPath => aPath.Replace('\\', '/').TrimStart('/'))
            .Where(aPath => aPath.StartsWith(Folder + "/", StringComparison.Ordinal)
                            && aPath.IndexOf('/', Folder.Length + 1) < 0)
            .Select(aPath => aPath[(Folder.Length + 1)..])
            .ToList();

        aAppName = FromFileNames(vInDocs);
        return vInDocs.Any(aName => aName.Length > Suffix.Length && aName.EndsWith(Suffix, StringComparison.Ordinal));
    }
}
