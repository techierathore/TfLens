using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.Export;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// Malformed gate records are listed, never scored (SCHEMA.md §3, REQ-FN-145).
/// </summary>
/// <remarks>
/// The reference (<c>tf-metrics.sh analyse</c>) removes every gate record with no <c>req_id</c> or no
/// <c>verdict</c> before anything is scored, lists each as <c>{app, ts, gate, result}</c> under
/// <c>gates_malformed</c>, publishes <c>gates_malformed_n</c>, and gives each <c>per_repo</c> entry a
/// <c>gates_malformed</c> count while its <c>gates</c> count excludes them.
/// </remarks>
public sealed class GateMalformedTests : IDisposable
{
    private const string Repo = "acme/alpha";
    private readonly string objDataRoot = ExportFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>
    /// A record with no req_id, one with no verdict and one with an empty verdict are malformed; a
    /// complete record is kept.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-145 — a gate with no req_id or no verdict is malformed")]
    public void MissingReqIdOrVerdictIsMalformed()
    {
        var vResult = GateMalformed.Apply(
        [
            GateFixtures.Gate(),
            GateFixtures.Gate(aReqId: null),
            GateFixtures.Gate(aVerdict: null),
            GateFixtures.Gate(aVerdict: string.Empty)
        ]);

        vResult.Kept.Should().ContainSingle();
        vResult.Malformed.Should().HaveCount(3);
    }

    /// <summary>
    /// Each listed line carries the record's own app, ts, gate and its undocumented <c>result</c> read
    /// from Overflow; with no app it falls back to the repository name.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-145 — a malformed gate is listed as app, ts, gate and result")]
    public void MalformedLineMatchesTheReference()
    {
        var vSuite = GateFixtures.Gate(aReqId: null, aVerdict: null, aGate: "verify-suite", aTs: "2026-10-01T00:00:00Z")
            with { Overflow = """{"result":"pass"}""" };
        var vNoApp = GateFixtures.Gate(aReqId: null, aApp: null);

        var vResult = GateMalformed.Apply([vSuite, vNoApp]);

        vResult.Malformed[0].Should().Be(new MalformedGate(Repo, "AlphaApp", "2026-10-01T00:00:00Z", "verify-suite", "pass"));
        vResult.Malformed[1].App.Should().Be("alpha");
        vResult.Malformed[1].Result.Should().BeNull();
    }

    /// <summary>
    /// The engine scores no malformed record: per-repo gates excludes it, its own count is published,
    /// and the first-pass denominator is the complete records alone.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-145 — the engine scores no malformed gate")]
    public async Task EngineScoresNoMalformedGate()
    {
        var vAnalysis = await new MetricsEngine(Store(), NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(ExportFixture.UserId, ExportFixture.Framework);

        var vRepo = vAnalysis.PerRepo.Single();
        vRepo.Gates.Should().Be(3);
        vRepo.GatesMalformed.Should().Be(1);
        vAnalysis.GatesMalformed.Should().ContainSingle();
        vAnalysis.Live.Values.Sum(aSegment => aSegment.FirstPassN).Should().Be(3);
    }

    /// <summary>
    /// <c>tflens.json</c> carries <c>gates_malformed_n</c>, the <c>gates_malformed</c> list and the
    /// per-repo <c>gates_malformed</c>, at the reference's paths.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-145 — the export lists malformed gates at the reference's paths")]
    public async Task ExportListsMalformedGates()
    {
        var vResult = await ExportFixture.Exporter(objDataRoot, Store())
            .ExportAsync(ExportFixture.UserId, ExportFixture.Framework, ExportFixture.Date);
        using var vDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vResult.JsonPath));
        var vRoot = vDocument.RootElement;

        vRoot.GetProperty("gates_malformed_n").GetInt32().Should().Be(1);
        var vLine = vRoot.GetProperty("gates_malformed").EnumerateArray().Single();
        vLine.GetProperty("app").GetString().Should().Be("AlphaApp");
        vLine.GetProperty("gate").GetString().Should().Be("verify-suite");
        vLine.GetProperty("result").GetString().Should().Be("pass");

        var vRepo = vRoot.GetProperty("per_repo").EnumerateArray().Single();
        vRepo.GetProperty("gates").GetInt32().Should().Be(3);
        vRepo.GetProperty("gates_malformed").GetInt32().Should().Be(1);
    }

    /// <summary>Three complete verdicts plus one suite-level note with no req_id or verdict.</summary>
    /// <returns>The store.</returns>
    private static FixtureTelemetryStore Store() =>
        new FixtureTelemetryStore().Seed(
            ExportFixture.UserId,
            Repo,
            ExportFixture.Framework,
            [
                GateFixtures.Gate(aReqId: "REQ-FN-001"),
                GateFixtures.Gate(aReqId: "REQ-FN-002"),
                GateFixtures.Gate(aReqId: "REQ-FN-003"),
                GateFixtures.Gate(aReqId: null, aVerdict: null, aGate: "verify-suite") with { Overflow = """{"result":"pass"}""" }
            ]);
}
