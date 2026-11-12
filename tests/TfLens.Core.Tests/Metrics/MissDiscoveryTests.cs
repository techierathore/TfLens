using System.Text.Json.Nodes;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Metrics;
using TfLens.Core.Parsing;
using TfLens.Core.Repos;
using TfLens.Core.Tests.Export;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-152 (BRD-220) — the two parity gaps the framework's script opened after phase 3: discovery
/// cost with the per-miss cost list (SCHEMA.md §5.5.12), and the app name of a repository with no
/// records, read from its single <c>docs/*-Checklist.md</c>.
/// </summary>
/// <remarks>
/// The expected values come from running <c>tf-metrics.sh --rollup</c> over the made-up "Sample
/// misses" fixture (<c>Fixtures/Discovery/make-reference.sh</c>); none is hand-written. The fixture holds
/// every case §5.5.12 names: measured windows in both scopes, one with no <c>discovery_s</c>,
/// not-measured windows, a miss written before the field existed, a backfilled miss, and fixes that are
/// sole, shared by one run, unmeasured and absent.
/// </remarks>
public sealed class MissDiscoveryTests
{
    private const int FixtureUserId = 9;
    private const string FixtureFramework = "techieflow";
    private const string MissesRepo = "sample/sample-misses";

    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Discovery");

    /// <summary>
    /// The export's six discovery keys equal the script's, the per-miss list line for line (REQ-FN-152).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-152 — the discovery cost keys and per_miss_cost equal tf-metrics.sh")]
    public async Task DiscoveryKeysEqualTheScript()
    {
        var (vExported, _) = await ExportAsync();
        var vReference = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(FixtureRoot, "reference-rollup.json")))!;

        foreach (var vKey in new[]
                 {
                     "discovery_tokens_per_miss", "discovery_minutes_per_miss", "discovery_measured_n",
                     "discovery_not_measured_n", "discovery_not_recorded_n", "per_miss_cost"
                 })
        {
            Assert.True(
                JsonNode.DeepEquals(Normalise(vReference["misses"]![vKey]), Normalise(vExported["misses"]![vKey])),
                $"misses.{vKey}: script {vReference["misses"]![vKey]?.ToJsonString()} · TfLens {vExported["misses"]![vKey]?.ToJsonString()}");
        }
    }

    /// <summary>
    /// The project's own parity check passes over the whole document for the discovery fixture
    /// (REQ-FN-152).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-152 — parity-compare.py over the discovery fixture exits 0")]
    public async Task WholeParityPassesOverTheDiscoveryFixture()
    {
        var (_, vJsonPath) = await ExportAsync();

        var (vExitCode, vReport) = await RunParityAsync(Path.Combine(FixtureRoot, "reference-rollup.json"), vJsonPath);

        Assert.True(vExitCode == 0, $"parity-compare.py exited {vExitCode}:{Environment.NewLine}{vReport}");
    }

    /// <summary>
    /// The checklist name follows the script's glob: exactly one <c>*-Checklist.md</c> names the app;
    /// none or several name nothing; the match is case-sensitive (REQ-FN-152).
    /// </summary>
    /// <param name="aNames">The file names in <c>docs/</c>, comma-separated.</param>
    /// <param name="aExpected">The app name, or empty for none.</param>
    [Theory(DisplayName = "REQ-FN-152 — the app is named from exactly one docs checklist file")]
    [InlineData("SampleEmpty-Checklist.md,README.md", "SampleEmpty")]
    [InlineData("TfLens-Checklist.md,TfLens-P2-Checklist.md", "")]
    [InlineData("README.md", "")]
    [InlineData("App-checklist.md", "")]
    [InlineData("-Checklist.md", "")]
    public void ChecklistNameFollowsTheGlob(string aNames, string aExpected)
    {
        var vName = ChecklistAppName.FromFileNames(aNames.Split(','));

        Assert.Equal(aExpected == string.Empty ? null : aExpected, vName);
    }

    /// <summary>
    /// A bundle names the app only from an entry directly in <c>docs/</c>, and one with no checklist
    /// entry says nothing, so the stored name stands (REQ-FN-152).
    /// </summary>
    [Fact(DisplayName = "REQ-FN-152 — an imported bundle names the app only from docs/ directly")]
    public void BundleNamesTheAppOnlyFromDocs()
    {
        Assert.True(ChecklistAppName.TryFromBundleEntries(["docs/metrics/runs.jsonl", "docs/SampleApp-Checklist.md"], out var vApp));
        Assert.Equal("SampleApp", vApp);

        Assert.False(ChecklistAppName.TryFromBundleEntries(["docs/metrics/runs.jsonl", "docs/old/App-Checklist.md"], out var vNone));
        Assert.Null(vNone);
    }

    /// <summary>
    /// A repository with no records is named from its checklist, while a repository whose records name
    /// their app keeps that name (REQ-FN-152, BRD-220).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-152 — an empty repository takes its checklist name; records keep theirs")]
    public async Task EmptyRepositoryTakesItsChecklistName()
    {
        var (vExported, _) = await ExportAsync(aExtraEmptyRepo: true);
        var vApps = vExported["per_repo"]!.AsArray()
            .ToDictionary(aRow => aRow!["repo"]!.GetValue<string>(), aRow => aRow!["app"]!.GetValue<string>());

        Assert.Equal("SampleEmpty", vApps["sample/sample-empty"]);
        Assert.Equal("SampleMisses", vApps[MissesRepo]);
    }

    /// <summary>Exports the fixture's snapshot.</summary>
    /// <param name="aExtraEmptyRepo">Also connect an empty repository whose checklist names it.</param>
    /// <returns>The parsed <c>tflens.json</c> and a copy of it under <c>tests/.artifacts</c>.</returns>
    private static async Task<(JsonObject Json, string Path)> ExportAsync(bool aExtraEmptyRepo = false)
    {
        var vText = await File.ReadAllTextAsync(Path.Combine(FixtureRoot, "sample__sample-misses", "misses.jsonl"));
        var vParsed = new StreamParser().Parse(FixtureUserId, MissesRepo, "fixture", StreamKind.Misses, vText);

        var vStore = new FixtureTelemetryStore()
            .SeedMisses(FixtureUserId, MissesRepo, FixtureFramework, vParsed.Misses, vParsed.MissFixes)
            .WithChecklistApp(FixtureUserId, MissesRepo, "SampleMisses");

        if (aExtraEmptyRepo)
        {
            vStore.SeedEstimates(FixtureUserId, "sample/sample-empty", FixtureFramework, [])
                .WithChecklistApp(FixtureUserId, "sample/sample-empty", "SampleEmpty");
        }

        var vDataRoot = ExportFixture.TemporaryDataRoot();
        try
        {
            var vResult = await ExportFixture.Exporter(vDataRoot, vStore)
                .ExportAsync(FixtureUserId, FixtureFramework, ExportFixture.Date);
            var vJson = await File.ReadAllTextAsync(vResult.JsonPath);

            var vWork = Path.Combine(ExportFixture.RepositoryRoot(), "tests", ".artifacts", "discovery-parity");
            Directory.CreateDirectory(vWork);
            var vCopy = Path.Combine(vWork, "tflens.json");
            await File.WriteAllTextAsync(vCopy, vJson);

            return (JsonNode.Parse(vJson)!.AsObject(), vCopy);
        }
        finally
        {
            Directory.Delete(vDataRoot, recursive: true);
        }
    }

    /// <summary>Runs <c>tools/parity-compare.py</c> over a reference and an export.</summary>
    /// <param name="aReference">The oracle's rollup.</param>
    /// <param name="aTflens">The exported document.</param>
    /// <returns>The exit code and everything printed.</returns>
    private static async Task<(int ExitCode, string Report)> RunParityAsync(string aReference, string aTflens)
    {
        var vRoot = ExportFixture.RepositoryRoot();
        var vStart = new System.Diagnostics.ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = vRoot
        };
        foreach (var vArgument in new[]
                 {
                     Path.Combine(vRoot, "tools", "parity-compare.py"), aReference, aTflens, "--allow-environment-keys"
                 })
        {
            vStart.ArgumentList.Add(vArgument);
        }

        using var vProcess = System.Diagnostics.Process.Start(vStart)!;
        var vOutput = await vProcess.StandardOutput.ReadToEndAsync();
        var vErrors = await vProcess.StandardError.ReadToEndAsync();
        await vProcess.WaitForExitAsync();

        return (vProcess.ExitCode, vOutput + vErrors);
    }

    /// <summary>Re-parses a node so 4000 and 4000.0 compare equal, as parity-compare.py treats them.</summary>
    /// <param name="aNode">The node.</param>
    /// <returns>The node with every number as a double.</returns>
    private static JsonNode? Normalise(JsonNode? aNode) => aNode switch
    {
        null => null,
        JsonObject vObject => new JsonObject(vObject.Select(aPair =>
            new KeyValuePair<string, JsonNode?>(aPair.Key, Normalise(aPair.Value)))),
        JsonArray vArray => new JsonArray(vArray.Select(Normalise).ToArray()),
        JsonValue vValue when vValue.TryGetValue<double>(out var vNumber) => JsonValue.Create(vNumber),
        _ => aNode.DeepClone()
    };
}
