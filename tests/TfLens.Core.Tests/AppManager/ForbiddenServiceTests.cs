using FluentAssertions;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// Proves REQ-FN-008 structurally: TfLens is Manager-only for Application 1, and no code path anywhere
/// in the product calls LicenseSvc, FeatureSvc, PaymentSvc or IssueSvc.
/// </summary>
/// <remarks>
/// The check is on the source rather than on behaviour on purpose. "No call exists" is a statement
/// about the whole codebase, and a runtime test can only ever say "the paths I exercised made no such
/// call". Matching the leading slash keeps it precise — it finds a request path, not the prose in an
/// XML comment that says these services are never called.
/// </remarks>
public sealed class ForbiddenServiceTests
{
    private static readonly string[] ForbiddenPaths =
    [
        "/LicenseSvc",
        "/FeatureSvc",
        "/PaymentSvc",
        "/IssueSvc"
    ];

    /// <summary>No source file under src/ contains a path into a forbidden AppManager service.</summary>
    [Fact]
    public void NoForbiddenServicePathExistsInTheCodebase()
    {
        var vOffenders = new List<string>();

        foreach (var vFile in SourceFiles())
        {
            var vText = File.ReadAllText(vFile);
            vOffenders.AddRange(
                ForbiddenPaths
                    .Where(aPath => vText.Contains(aPath, StringComparison.Ordinal))
                    .Select(aPath => $"{aPath} in {vFile}"));
        }

        vOffenders.Should().BeEmpty();
    }

    /// <summary>The only application role code the client ever sends is Manager.</summary>
    [Fact]
    public void ManagerIsTheOnlyRoleCodeRequested()
    {
        var vSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "TfLens.Core",
            "AppManager",
            "AppManagerClient.cs"));

        vSource.Should().Contain("applicationRoleCode\"] = ManagerRoleCode");
        vSource.Should().Contain("public const string ManagerRoleCode = \"Manager\"");
    }

    /// <summary>
    /// Enumerates every C# file the product ships, skipping build output.
    /// </summary>
    /// <returns>Absolute paths of the source files to scan.</returns>
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(aFile => !aFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !aFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Locates the repository root, which is what this test actually scans.
    /// </summary>
    /// <returns>The absolute repository root.</returns>
    /// <exception cref="InvalidOperationException">The repository could not be located from anywhere the test can see.</exception>
    /// <remarks>
    /// Walking up from the test assembly is the normal path. The <c>TfLensRepoRoot</c> escape hatch
    /// exists so the scan still works when the build output is deliberately placed outside the tree —
    /// a source-scanning test that silently finds nothing to scan would be worse than useless.
    /// </remarks>
    private static string RepositoryRoot() =>
        FindUpwards(Environment.GetEnvironmentVariable("TfLensRepoRoot"))
        ?? FindUpwards(AppContext.BaseDirectory)
        ?? FindUpwards(Directory.GetCurrentDirectory())
        ?? throw new InvalidOperationException("The repository root could not be located from the test assembly.");

    /// <summary>
    /// Walks up from a directory looking for the solution file.
    /// </summary>
    /// <param name="aStart">Where to start, or <c>null</c> to skip.</param>
    /// <returns>The repository root, or <c>null</c> when this branch does not reach it.</returns>
    private static string? FindUpwards(string? aStart)
    {
        if (string.IsNullOrWhiteSpace(aStart) || !Directory.Exists(aStart))
        {
            return null;
        }

        var vDirectory = new DirectoryInfo(aStart);

        while (vDirectory is not null)
        {
            if (File.Exists(Path.Combine(vDirectory.FullName, "TfLens.slnx")))
            {
                return vDirectory.FullName;
            }

            vDirectory = vDirectory.Parent;
        }

        return null;
    }
}
