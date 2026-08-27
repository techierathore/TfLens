using System.Text.RegularExpressions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// Locates the repository on disk and enumerates the files a guardrail is allowed to judge.
/// </summary>
/// <remarks>
/// The guardrails in this project are static checks over the working tree rather than over a running
/// process, because that is the only way to prove a negative — "no code path logs a token" cannot be
/// demonstrated by exercising the code paths that exist today.
/// </remarks>
public static class RepoTree
{
    /// <summary>Directories no guardrail ever looks inside.</summary>
    /// <remarks>
    /// Build output, the framework's deployed copy and the gitignored local state. Everything here is
    /// either machine-generated or never committed, so a finding inside it would be noise.
    /// </remarks>
    private static readonly string[] ExcludedDirectories =
    [
        ".git", ".tfcore", ".claude", ".opencode", ".codex", ".agents", ".techierag", ".trblazeui",
        ".verify", "bin", "obj", "node_modules", "data", "logs", "playwright-report",
        "test-results", "OldDocs"
    ];

    private static readonly Lazy<DirectoryInfo> objRoot = new(FindRoot);

    /// <summary>The repository root — the directory holding <c>TfLens.slnx</c>.</summary>
    public static DirectoryInfo Root => objRoot.Value;

    /// <summary>
    /// Walks up from the test binaries until the solution file appears.
    /// </summary>
    /// <returns>The repository root.</returns>
    /// <exception cref="InvalidOperationException">No ancestor directory holds <c>TfLens.slnx</c>.</exception>
    private static DirectoryInfo FindRoot()
    {
        var vCurrent = new DirectoryInfo(AppContext.BaseDirectory);

        while (vCurrent is not null)
        {
            if (File.Exists(Path.Combine(vCurrent.FullName, "TfLens.slnx")))
            {
                return vCurrent;
            }

            vCurrent = vCurrent.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find TfLens.slnx above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Enumerates every committed file matching a glob, skipping build output and framework copies.
    /// </summary>
    /// <param name="aPattern">A file-name glob, for example <c>*.cs</c>.</param>
    /// <param name="aRelativeRoot">A repository-relative subdirectory, or <c>null</c> for the whole tree.</param>
    /// <returns>Absolute paths, in a stable order so a failure message is reproducible.</returns>
    public static IReadOnlyList<string> Files(string aPattern, string? aRelativeRoot = null)
    {
        var vStart = aRelativeRoot is null ? Root.FullName : Path.Combine(Root.FullName, aRelativeRoot);

        if (!Directory.Exists(vStart))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(vStart, aPattern, SearchOption.AllDirectories)
            .Where(aPath => !IsExcluded(aPath))
            .OrderBy(aPath => aPath, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Tells whether a path sits under one of the excluded directories.</summary>
    /// <param name="aPath">An absolute path.</param>
    /// <returns><c>true</c> when the file must be ignored.</returns>
    private static bool IsExcluded(string aPath)
    {
        var vRelative = Path.GetRelativePath(Root.FullName, aPath);
        var vSegments = vRelative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return vSegments.Any(aSegment =>
            ExcludedDirectories.Contains(aSegment, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Renders an absolute path relative to the repository root, for a failure message.</summary>
    /// <param name="aPath">An absolute path.</param>
    /// <returns>The repository-relative path with forward slashes.</returns>
    public static string Relative(string aPath) =>
        Path.GetRelativePath(Root.FullName, aPath).Replace('\\', '/');

    /// <summary>
    /// Removes every string and character literal from a line of C# or Razor.
    /// </summary>
    /// <param name="aLine">One source line.</param>
    /// <returns>The line with literal contents blanked out.</returns>
    /// <remarks>
    /// This is what keeps the secret scan honest. <c>Log.Warning("INVALID_CURRENT_PASSWORD")</c> is a
    /// message, not a leak; <c>Log.Warning("token {Token}", vAccessToken)</c> is a leak. Stripping the
    /// literals first means only *identifiers* — actual values — are matched.
    /// </remarks>
    public static string StripLiterals(string aLine)
    {
        // Interpolation holes are the single most likely leak vector — `$"… {vAccessToken} …"` — and
        // they live *inside* a literal, so their contents are lifted out before the literals go.
        var vHoles = Regex.Matches(aLine, @"\{([^{}""]+)\}")
            .Select(aMatch => aMatch.Groups[1].Value)
            .ToArray();

        var vWithoutVerbatim = Regex.Replace(aLine, "@\"(?:[^\"]|\"\")*\"", "\"\"");
        var vWithoutStrings = Regex.Replace(vWithoutVerbatim, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
        var vWithoutChars = Regex.Replace(vWithoutStrings, "'(?:\\\\.|[^'\\\\])'", "''");

        return vHoles.Length == 0 ? vWithoutChars : vWithoutChars + " /*holes*/ " + string.Join(' ', vHoles);
    }
}
