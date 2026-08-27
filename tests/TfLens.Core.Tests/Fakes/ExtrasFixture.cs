using Microsoft.Extensions.Options;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Fakes;

/// <summary>
/// Wires the extras fixture repository up to a real <see cref="ExtraMetrics"/> over a real parser.
/// </summary>
/// <remarks>
/// One place to build the graph, so every extras test is computing from the same bytes the
/// <c>tflens-fixtures/parity-repo</c> streams hold and from the same rate card. Nothing about the
/// figures is stubbed: only the storage round-trip and the clock are.
/// </remarks>
public static class ExtrasFixture
{
    /// <summary>The user the fixture records are attributed to — the UsageGuide demo account.</summary>
    public const int UserId = 2;

    /// <summary>The repository the fixture records carry, named so it reads as <c>owner/name</c>.</summary>
    public const string Repo = "tflens-fixtures/parity-repo";

    /// <summary>The provenance axis the fixture sits on.</summary>
    public const string Framework = FrameworkNames.TechieFlow;

    /// <summary>The SHA the fixture pretends to have been fetched at.</summary>
    public const string SourceSha = "9f3c1ab5d2e47610c8b4a09fe5d7213c6b8e40a1";

    /// <summary>Absolute path of the fixture's <c>docs/metrics</c> folder beside the test binary.</summary>
    /// <returns>The folder holding the four stream files.</returns>
    public static string MetricsFolder() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "tflens-fixtures", "parity-repo", "docs", "metrics");

    /// <summary>Absolute path of the oracle's rollup over the fixture.</summary>
    /// <returns>The <c>reference.json</c> path.</returns>
    public static string ReferenceJson() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "tflens-fixtures", "reference.json");

    /// <summary>
    /// Builds a store serving the fixture streams through the production parser.
    /// </summary>
    /// <returns>The store.</returns>
    public static ParsedFixtureStore Store() =>
        new(UserId, Repo, Framework, MetricsFolder(), SourceSha);

    /// <summary>
    /// Builds options rooted at a throwaway data folder, so a test's rate card and parity record are
    /// its own.
    /// </summary>
    /// <param name="aDataRoot">The temporary data root.</param>
    /// <returns>The bound options.</returns>
    public static IOptions<TfLensOptions> Options(string aDataRoot) =>
        Microsoft.Extensions.Options.Options.Create(new TfLensOptions { DataRoot = aDataRoot });

    /// <summary>
    /// Builds the extras service over the fixture.
    /// </summary>
    /// <param name="aDataRoot">A temporary data root; the default rate card is written into it.</param>
    /// <returns>The service under test.</returns>
    public static ExtraMetrics Extras(string aDataRoot) => new(Store(), Options(aDataRoot));

    /// <summary>
    /// Creates a throwaway data root under the test binary's own artifacts folder.
    /// </summary>
    /// <returns>The folder path; the caller deletes it.</returns>
    public static string TemporaryDataRoot() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tflens-tests", Guid.NewGuid().ToString("N"))).FullName;
}
