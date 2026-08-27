using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.Metrics;

namespace TfLens.Core.Tests.Export;

/// <summary>
/// Wires a real exporter over the real engine and the real extras, for the export tests.
/// </summary>
/// <remarks>
/// The engine fixtures (<c>Fixtures/Engine</c>) are the ones <c>Fixtures/Engine/reference.json</c> was
/// produced from by running the oracle, so an export built on them can be compared against that
/// document key-for-key. Nothing between the fixture bytes and <c>tflens.json</c> is stubbed except
/// storage: the engine, the extras and the exporter are all the production types.
/// </remarks>
public static class ExportFixture
{
    /// <summary>The user the engine fixtures are attributed to.</summary>
    public const int UserId = 7;

    /// <summary>The provenance axis the engine fixtures sit on.</summary>
    public const string Framework = FrameworkNames.TechieFlow;

    /// <summary>The date the export tests write under.</summary>
    public static readonly DateOnly Date = new(2026, 8, 26);

    /// <summary>Absolute path of the engine fixture root beside the test binary.</summary>
    /// <returns>The folder holding <c>alpha</c>, <c>beta</c>, <c>gamma</c> and <c>reference.json</c>.</returns>
    public static string EngineRoot() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Engine");

    /// <summary>Absolute path of the oracle's rollup over the engine fixtures.</summary>
    /// <returns>The <c>reference.json</c> path.</returns>
    public static string ReferenceJson() => Path.Combine(EngineRoot(), "reference.json");

    /// <summary>
    /// Builds a store over the three engine fixture repositories.
    /// </summary>
    /// <returns>The store.</returns>
    public static FixtureTelemetryStore Store() =>
        new FixtureTelemetryStore()
            .Load(UserId, "acme/alpha", Framework, Path.Combine(EngineRoot(), "alpha"))
            .Load(UserId, "acme/beta", Framework, Path.Combine(EngineRoot(), "beta"))
            .Load(UserId, "acme/gamma", Framework, Path.Combine(EngineRoot(), "gamma"));

    /// <summary>
    /// Builds the exporter under test.
    /// </summary>
    /// <param name="aDataRoot">A throwaway data root; reports, the rate card and the parity record live under it.</param>
    /// <returns>The exporter.</returns>
    public static SnapshotExporter Exporter(string aDataRoot)
    {
        var vStore = Store();
        var vOptions = Options.Create(new TfLensOptions { DataRoot = aDataRoot });

        return new SnapshotExporter(
            new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance),
            new ExtraMetrics(vStore, vOptions),
            vStore,
            vOptions,
            NullLogger<SnapshotExporter>.Instance);
    }

    /// <summary>
    /// Creates a throwaway data root.
    /// </summary>
    /// <returns>The folder path; the caller deletes it.</returns>
    public static string TemporaryDataRoot() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tflens-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Walks up from the test binary to the repository root — the folder holding <c>tools/</c>.
    /// </summary>
    /// <returns>The repository root.</returns>
    /// <exception cref="DirectoryNotFoundException">No ancestor holds <c>tools/parity-compare.py</c>.</exception>
    public static string RepositoryRoot()
    {
        var vFolder = new DirectoryInfo(AppContext.BaseDirectory);

        while (vFolder is not null)
        {
            if (File.Exists(Path.Combine(vFolder.FullName, "tools", "parity-compare.py")))
            {
                return vFolder.FullName;
            }

            vFolder = vFolder.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate tools/parity-compare.py above the test binary.");
    }
}
