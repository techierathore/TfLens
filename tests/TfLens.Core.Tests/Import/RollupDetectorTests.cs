using System.Text;
using TfLens.Core.Import;

namespace TfLens.Core.Tests.Import;

/// <summary>
/// REQ-FN-086 (BRD-140) — a precomputed rollup, a <c>tflens.json</c> or an exported snapshot is
/// refused with a message naming what to upload instead, and the refusal has no override.
/// </summary>
/// <remarks>
/// The three shapes below are the real ones: the first is the top of
/// <c>bash .tfcore/telemetry/tf-metrics.sh --rollup --json</c> as run against this repository on
/// 2026-08-28, the second is the top of a <c>tflens.json</c> the exporter wrote under
/// <c>data/reports/</c>, and the third is what <c>SnapshotJson.Build</c> composes.
/// </remarks>
public sealed class RollupDetectorTests
{
    /// <summary><c>tf-metrics.sh --rollup --json</c> output, trimmed to its shape.</summary>
    private const string ReferenceRollup = """
        {
          "per_repo": [
            { "repo": ".", "app": "TfLens", "project_type": "app", "gates": 225, "runs": 11 }
          ],
          "tainted_reqs": [],
          "live": { "docs": { "records": 225, "first_pass_rate": "94%" } },
          "backfilled": {},
          "pooled": { "runs_total": 11 }
        }
        """;

    /// <summary>An exported <c>tflens.json</c>, trimmed to its shape.</summary>
    private const string ExportedSnapshot = """
        {
          "per_repo": [
            { "repo": "techierathore/TechieBlog", "gates": 214, "source_sha": "30e6616" }
          ],
          "tainted_reqs": ["REQ-UI-010"],
          "live": {},
          "backfilled": {},
          "pooled": {},
          "extras": {},
          "parity": { "status": "clean" }
        }
        """;

    /// <summary>All three named files are refused on their name alone.</summary>
    [Theory]
    [InlineData("tflens.json")]
    [InlineData("snapshot.md")]
    [InlineData("rollup.json")]
    [InlineData("data/reports/2/2026-08-27/techieflow/tflens.json")]
    public void TheExportsOwnFileNamesAreRefused(string aEntryName) =>
        Assert.True(RollupDetector.IsRefusedName(aEntryName));

    /// <summary>The reference's rollup is refused by shape, whatever it is called.</summary>
    [Fact]
    public void TheReferenceRollupIsRefusedByShape() =>
        Assert.True(RollupDetector.IsRollupPayload(Encoding.UTF8.GetBytes(ReferenceRollup)));

    /// <summary>An exported snapshot is refused by shape, whatever it is called.</summary>
    [Fact]
    public void TheExportedSnapshotIsRefusedByShape() =>
        Assert.True(RollupDetector.IsRollupPayload(Encoding.UTF8.GetBytes(ExportedSnapshot)));

    /// <summary>
    /// Renaming a rollup to a stream file name does not smuggle it in.
    /// </summary>
    /// <remarks>
    /// The name check alone would be trivially evadable, which is why the shape check exists.
    /// </remarks>
    [Fact]
    public void ARenamedRollupIsStillRefused()
    {
        var vRefusal = RollupDetector.Detect("runs.jsonl", Encoding.UTF8.GetBytes(ReferenceRollup));

        Assert.NotNull(vRefusal);
        Assert.Equal(ImportRefusalReason.PrecomputedRollup, vRefusal.Reason);
    }

    /// <summary>Real telemetry is never mistaken for a rollup.</summary>
    [Fact]
    public void RawStreamLinesAreNotARollup()
    {
        Assert.False(RollupDetector.IsRollupPayload(Encoding.UTF8.GetBytes(ImportTestSupport.GateLines)));
        Assert.False(RollupDetector.IsRollupPayload(Encoding.UTF8.GetBytes(ImportTestSupport.RunLinesWithOneInvalid)));
        Assert.Null(RollupDetector.Detect("gates.jsonl", Encoding.UTF8.GetBytes(ImportTestSupport.GateLines)));
    }

    /// <summary>A single-line stream file is one JSON object and must still not read as a rollup.</summary>
    [Fact]
    public void ASingleRecordFileIsNotARollup()
    {
        const string vOneRecord =
            """{"v":1,"ts":"2026-08-01T10:00:00Z","kind":"gate","app":"TfLens","req_id":"REQ-FN-086","verdict":"pass"}""";

        Assert.False(RollupDetector.IsRollupPayload(Encoding.UTF8.GetBytes(vOneRecord)));
    }

    /// <summary>The refusal message names the directory to zip and both frameworks' paths.</summary>
    [Fact]
    public void TheMessageNamesWhatToUploadInstead()
    {
        Assert.Contains("docs/metrics/", RollupDetector.Message, StringComparison.Ordinal);
        Assert.Contains("verification/telemetry/", RollupDetector.Message, StringComparison.Ordinal);
        Assert.Contains("runs.jsonl", RollupDetector.Message, StringComparison.Ordinal);
        Assert.Equal(ImportRefusalReason.PrecomputedRollup, RollupDetector.Refusal.Reason);
    }

    /// <summary>
    /// The refusal is structural: it takes no flag, no option and no caller-supplied override.
    /// </summary>
    /// <remarks>
    /// REQ-FN-086 — "the refusal is structural and has no override". A parameter that could turn it
    /// off would be the override, so the detector's whole public surface is checked for one.
    /// </remarks>
    [Fact]
    public void TheRefusalHasNoOverride()
    {
        var vParameters = typeof(RollupDetector)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(aM => aM.DeclaringType == typeof(RollupDetector))
            .SelectMany(aM => aM.GetParameters())
            .ToArray();

        Assert.DoesNotContain(vParameters, aP => aP.ParameterType == typeof(bool));
        Assert.DoesNotContain(
            vParameters,
            aP => aP.Name!.Contains("allow", StringComparison.OrdinalIgnoreCase)
                  || aP.Name.Contains("force", StringComparison.OrdinalIgnoreCase)
                  || aP.Name.Contains("override", StringComparison.OrdinalIgnoreCase));
    }
}
