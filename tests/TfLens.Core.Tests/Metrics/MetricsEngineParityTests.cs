using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-054 — the fixture-driven parity gate against <c>tf-metrics.sh</c>.
/// </summary>
/// <remarks>
/// <c>Fixtures/Engine/reference.json</c> is produced by running the oracle itself
/// (<c>Fixtures/Engine/make-reference.sh</c> → <c>tf-metrics.sh --rollup … --json</c>) over the same
/// fixture streams this test feeds the engine. Nothing in it is hand-written. Every key the two
/// implementations share is compared; the keys only one of them has are named explicitly by
/// <see cref="ReferenceKeysWithNoTfLensCounterpartAreNamed"/> and
/// <see cref="TfLensKeysBeyondTheReferenceAreNamed"/>, so a divergence can never hide as a skip.
/// </remarks>
public sealed class MetricsEngineParityTests
{
    private const int FixtureUserId = 7;
    private const string FixtureFramework = "techieflow";

    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Engine");

    /// <summary>
    /// The engine's output over the fixture streams equals the oracle's <c>reference.json</c> for every
    /// key the two share — per-repo counts, the tainted set, every live and backfilled segment, and the
    /// whole pooled block.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task EngineMatchesReferenceJsonKeyForKey()
    {
        var vAnalysis = await AnalyseFixturesAsync();
        var vReference = LoadReference();
        var vDifferences = new List<string>();

        ComparePerRepo(vReference.GetProperty("per_repo"), vAnalysis.PerRepo, vDifferences);
        CompareTainted(vReference.GetProperty("tainted_reqs"), vAnalysis.TaintedReqs, vDifferences);
        CompareBucket(vReference.GetProperty("live"), vAnalysis.Live, "live", vDifferences);
        CompareBucket(vReference.GetProperty("backfilled"), vAnalysis.Backfilled, "backfilled", vDifferences);
        ComparePooled(vReference.GetProperty("pooled"), vAnalysis.Pooled, vDifferences);

        Assert.True(
            vDifferences.Count == 0,
            "TfLens diverged from tf-metrics.sh:" + Environment.NewLine + string.Join(Environment.NewLine, vDifferences));
    }

    /// <summary>
    /// The keys the oracle emits that TfLens has no counterpart for are named here rather than skipped
    /// silently, so adding one to the reference cannot go unnoticed.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ReferenceKeysWithNoTfLensCounterpartAreNamed()
    {
        var vReference = LoadReference();
        var vAnalysis = await AnalyseFixturesAsync();

        // per_repo.commit_hook — the reference reads the running clone's .git/hooks directory. TfLens
        // reads repositories over the GitHub API and never sees a clone, so the fact does not exist.
        Assert.All(
            vReference.GetProperty("per_repo").EnumerateArray(),
            aRepo => Assert.True(aRepo.TryGetProperty("commit_hook", out _)));
        Assert.DoesNotContain(
            nameof(PerRepoFacts) + ".CommitHook",
            typeof(PerRepoFacts).GetProperties().Select(aProperty => nameof(PerRepoFacts) + "." + aProperty.Name));

        // per_repo.repo — the reference names a repository by the directory it was rolled up from;
        // TfLens names it owner/name. Compared as a suffix in EngineMatchesReferenceJsonKeyForKey.
        Assert.Equal(
            vReference.GetProperty("per_repo").EnumerateArray().Select(aRepo => aRepo.GetProperty("repo").GetString()),
            vAnalysis.PerRepo.Select(aRepo => aRepo.Repo.Split('/')[^1]));

        // pooled.cost_usd — present in both, and null in both, forever (REQ-FN-051).
        Assert.Equal(JsonValueKind.Null, vReference.GetProperty("pooled").GetProperty("cost_usd").ValueKind);
        Assert.Null(vAnalysis.Pooled.CostUsd);
    }

    /// <summary>
    /// The values TfLens computes that the oracle's JSON does not carry are named here, with the reason
    /// each one is additive rather than a divergence.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TfLensKeysBeyondTheReferenceAreNamed()
    {
        var vAnalysis = await AnalyseFixturesAsync();
        var vReference = LoadReference();

        // UserId / Framework / ParserVersion — the axes and stamp a multi-user, multi-framework service
        // needs and a single-machine script does not (ADR-013, ADR-016, BRD-68).
        Assert.Equal(FixtureUserId, vAnalysis.UserId);
        Assert.Equal(FixtureFramework, vAnalysis.Framework);
        Assert.False(string.IsNullOrWhiteSpace(vAnalysis.ParserVersion));

        // PerRepoFacts.Framework and .Events — the framework axis, and the Playbook stream the
        // reference has no concept of.
        Assert.All(vAnalysis.PerRepo, aRepo => Assert.Equal(FixtureFramework, aRepo.Framework));
        Assert.All(vAnalysis.PerRepo, aRepo => Assert.Equal(0, aRepo.Events));

        // LateGateCoverage.CatchRate — the reference computes this in print_report rather than in the
        // JSON, so it is compared against the printed formula, not against a JSON key.
        var vPerf = vAnalysis.Live["app"].LateGateCoverage.Single(aGate => aGate.Gate == "perf");
        var vReferencePerf = vReference.GetProperty("live").GetProperty("app")
            .GetProperty("late_gate_coverage").GetProperty("perf");
        Assert.Equal(vReferencePerf.GetProperty("ran").GetInt32(), vPerf.Ran);
        Assert.Equal(vReferencePerf.GetProperty("caught").GetInt32(), vPerf.Caught);
        Assert.Equal(
            MetricsConstants.Pct(vReferencePerf.GetProperty("caught").GetInt32(), vReferencePerf.GetProperty("ran").GetInt32()),
            vPerf.CatchRate.Display());

        // GateCount.Share — the reference prints pct(n, total) in the report rather than storing it.
        var vBuild = vAnalysis.Live["app"].GateDistribution.Single(aGate => aGate.Gate == "build");
        Assert.Equal(MetricsConstants.Pct(vBuild.Count, vAnalysis.Live["app"].GateDistributionN), vBuild.Share);
    }

    /// <summary>
    /// Runs the engine over the checked-in fixture streams.
    /// </summary>
    /// <returns>The analysis the parity comparison is made against.</returns>
    private static async Task<AnalysisResult> AnalyseFixturesAsync()
    {
        var vStore = new FixtureTelemetryStore()
            .Load(FixtureUserId, "acme/alpha", FixtureFramework, Path.Combine(FixtureRoot, "alpha"))
            .Load(FixtureUserId, "acme/beta", FixtureFramework, Path.Combine(FixtureRoot, "beta"))
            .Load(FixtureUserId, "acme/gamma", FixtureFramework, Path.Combine(FixtureRoot, "gamma"));

        var vEngine = new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance);
        return await vEngine.AnalyseAsync(FixtureUserId, FixtureFramework);
    }

    /// <summary>
    /// Reads the oracle's output.
    /// </summary>
    /// <returns>The root element of <c>reference.json</c>.</returns>
    private static JsonElement LoadReference()
    {
        var vPath = Path.Combine(FixtureRoot, "reference.json");
        Assert.True(File.Exists(vPath), $"reference.json is missing at {vPath} — run make-reference.sh.");
        return JsonDocument.Parse(File.ReadAllText(vPath)).RootElement.Clone();
    }

    /// <summary>Compares the per-repository fact lines.</summary>
    /// <param name="aReference">The reference's <c>per_repo</c> array.</param>
    /// <param name="aActual">The engine's per-repository facts.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void ComparePerRepo(
        JsonElement aReference,
        IReadOnlyList<PerRepoFacts> aActual,
        List<string> aDifferences)
    {
        var vExpected = aReference.EnumerateArray().ToList();
        if (vExpected.Count != aActual.Count)
        {
            aDifferences.Add($"per_repo: {vExpected.Count} repositories in the reference, {aActual.Count} in TfLens");
            return;
        }

        for (var vIndex = 0; vIndex < vExpected.Count; vIndex++)
        {
            var vRow = vExpected[vIndex];
            var vFacts = aActual[vIndex];
            var vPath = $"per_repo[{vIndex}]";

            if (!vFacts.Repo.EndsWith("/" + vRow.GetProperty("repo").GetString(), StringComparison.Ordinal))
            {
                aDifferences.Add($"{vPath}.repo: {vRow.GetProperty("repo").GetString()} vs {vFacts.Repo}");
            }

            CompareString(vRow, "app", vFacts.App, vPath, aDifferences);
            CompareString(vRow, "project_type", vFacts.ProjectType, vPath, aDifferences);
            CompareInt(vRow, "gates", vFacts.Gates, vPath, aDifferences);
            CompareInt(vRow, "gates_backfilled", vFacts.GatesBackfilled, vPath, aDifferences);
            CompareInt(vRow, "runs", vFacts.Runs, vPath, aDifferences);
            CompareInt(vRow, "sessions", vFacts.Sessions, vPath, aDifferences);
            CompareInt(vRow, "commits", vFacts.Commits, vPath, aDifferences);
        }
    }

    /// <summary>Compares the tainted-REQ list, order included.</summary>
    /// <param name="aReference">The reference's <c>tainted_reqs</c> array.</param>
    /// <param name="aActual">The engine's list.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void CompareTainted(JsonElement aReference, IReadOnlyList<string> aActual, List<string> aDifferences)
    {
        var vExpected = aReference.EnumerateArray().Select(aItem => aItem.GetString()).ToList();
        if (!vExpected.SequenceEqual(aActual))
        {
            aDifferences.Add(
                $"tainted_reqs: [{string.Join(", ", vExpected)}] vs [{string.Join(", ", aActual)}]");
        }
    }

    /// <summary>Compares one provenance bucket, segment by segment.</summary>
    /// <param name="aReference">The reference's <c>live</c> or <c>backfilled</c> object.</param>
    /// <param name="aActual">The engine's matching dictionary.</param>
    /// <param name="aLabel">The bucket name, for the difference message.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void CompareBucket(
        JsonElement aReference,
        IReadOnlyDictionary<string, SegmentFigures> aActual,
        string aLabel,
        List<string> aDifferences)
    {
        var vExpectedKeys = aReference.EnumerateObject().Select(aProperty => aProperty.Name).ToList();
        if (!vExpectedKeys.SequenceEqual(aActual.Keys))
        {
            aDifferences.Add(
                $"{aLabel}: segments [{string.Join(", ", vExpectedKeys)}] vs [{string.Join(", ", aActual.Keys)}]");
            return;
        }

        foreach (var vSegment in aReference.EnumerateObject())
        {
            CompareSegment(vSegment.Value, aActual[vSegment.Name], $"{aLabel}.{vSegment.Name}", aDifferences);
        }
    }

    /// <summary>Compares one (provenance, project type) figure block, field for field.</summary>
    /// <param name="aReference">The reference's segment object.</param>
    /// <param name="aActual">The engine's figures.</param>
    /// <param name="aPath">The key path, for the difference message.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void CompareSegment(
        JsonElement aReference,
        SegmentFigures aActual,
        string aPath,
        List<string> aDifferences)
    {
        CompareInt(aReference, "records", aActual.Records, aPath, aDifferences);
        CompareInt(aReference, "reqs_scored", aActual.ReqsScored, aPath, aDifferences);
        CompareInt(aReference, "reqs_excluded_backfill_taint", aActual.ReqsExcludedBackfillTaint, aPath, aDifferences);
        CompareInt(aReference, "first_pass_n", aActual.FirstPassN, aPath, aDifferences);
        CompareFigure(aReference.GetProperty("first_pass_rate"), aActual.FirstPassRate, $"{aPath}.first_pass_rate", aDifferences);
        CompareInt(aReference, "gate_distribution_n", aActual.GateDistributionN, aPath, aDifferences);
        CompareString(aReference, "gate_distribution_note", aActual.GateDistributionNote, aPath, aDifferences);
        CompareFigure(aReference.GetProperty("escape_rate"), aActual.EscapeRate, $"{aPath}.escape_rate", aDifferences);

        var vExpectedGates = aReference.GetProperty("gate_distribution").EnumerateObject()
            .Select(aGate => $"{aGate.Name}={aGate.Value.GetInt32()}").ToList();
        var vActualGates = aActual.GateDistribution.Select(aGate => $"{aGate.Gate}={aGate.Count}").ToList();
        if (!vExpectedGates.SequenceEqual(vActualGates))
        {
            aDifferences.Add(
                $"{aPath}.gate_distribution: [{string.Join(", ", vExpectedGates)}] vs [{string.Join(", ", vActualGates)}]");
        }

        var vExpectedCoverage = aReference.GetProperty("late_gate_coverage").EnumerateObject()
            .Select(aGate =>
                $"{aGate.Name}:ran={aGate.Value.GetProperty("ran").GetInt32()}," +
                $"caught={aGate.Value.GetProperty("caught").GetInt32()}," +
                $"since={aGate.Value.GetProperty("since").GetString()}")
            .ToList();
        var vActualCoverage = aActual.LateGateCoverage
            .Select(aGate => $"{aGate.Gate}:ran={aGate.Ran},caught={aGate.Caught},since={aGate.Since}")
            .ToList();
        if (!vExpectedCoverage.SequenceEqual(vActualCoverage))
        {
            aDifferences.Add(
                $"{aPath}.late_gate_coverage: [{string.Join(", ", vExpectedCoverage)}] vs [{string.Join(", ", vActualCoverage)}]");
        }
    }

    /// <summary>Compares the pooled block.</summary>
    /// <param name="aReference">The reference's <c>pooled</c> object.</param>
    /// <param name="aActual">The engine's pooled metrics.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void ComparePooled(JsonElement aReference, PooledMetrics aActual, List<string> aDifferences)
    {
        const string vPath = "pooled";

        CompareInt(aReference, "runs_total", aActual.RunsTotal, vPath, aDifferences);
        CompareInt(aReference, "sessions", aActual.Sessions, vPath, aDifferences);
        CompareInt(aReference, "commits", aActual.Commits, vPath, aDifferences);
        CompareInt(aReference, "commit_duplicates_collapsed", aActual.CommitDuplicatesCollapsed, vPath, aDifferences);
        CompareInt(aReference, "active_days", aActual.ActiveDays, vPath, aDifferences);

        var vTokens = aReference.GetProperty("tokens_total").GetInt64();
        if (vTokens != aActual.TokensTotal)
        {
            aDifferences.Add($"{vPath}.tokens_total: {vTokens} vs {aActual.TokensTotal}");
        }

        var vExpectedCmds = aReference.GetProperty("runs_by_cmd").EnumerateObject()
            .Select(aCmd => $"{aCmd.Name}={aCmd.Value.GetInt32()}").ToList();
        var vActualCmds = aActual.RunsByCmd.Select(aCmd => $"{aCmd.Key}={aCmd.Value}").ToList();
        if (!vExpectedCmds.SequenceEqual(vActualCmds))
        {
            aDifferences.Add(
                $"{vPath}.runs_by_cmd: [{string.Join(", ", vExpectedCmds)}] vs [{string.Join(", ", vActualCmds)}]");
        }

        CompareFigure(aReference.GetProperty("rework_ratio"), aActual.ReworkRatio, $"{vPath}.rework_ratio", aDifferences);
        CompareFigure(
            aReference.GetProperty("throughput_median_reqs_per_hour"),
            aActual.ThroughputMedianReqsPerHour,
            $"{vPath}.throughput_median_reqs_per_hour",
            aDifferences);
        CompareFigure(aReference.GetProperty("batch_size_median"), aActual.BatchSizeMedian, $"{vPath}.batch_size_median", aDifferences);
        CompareFigure(
            aReference.GetProperty("tokens_per_verified_req"),
            aActual.TokensPerVerifiedReq,
            $"{vPath}.tokens_per_verified_req",
            aDifferences);
        CompareFigure(
            aReference.GetProperty("commits_per_active_day"),
            aActual.CommitsPerActiveDay,
            $"{vPath}.commits_per_active_day",
            aDifferences);

        if (aReference.GetProperty("cost_usd").ValueKind != JsonValueKind.Null || aActual.CostUsd is not null)
        {
            aDifferences.Add($"{vPath}.cost_usd must be null on both sides (REQ-FN-051)");
        }
    }

    /// <summary>Compares a reference value against a <see cref="Figure"/> in every one of its forms.</summary>
    /// <param name="aReference">The reference value — a number, a rendered string, or null.</param>
    /// <param name="aActual">The figure TfLens produced.</param>
    /// <param name="aPath">The key path, for the difference message.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void CompareFigure(
        JsonElement aReference,
        Figure aActual,
        string aPath,
        List<string> aDifferences)
    {
        switch (aReference.ValueKind)
        {
            case JsonValueKind.Number:
                if (!aActual.TryGetValue(out var vValue) || Math.Abs(vValue - aReference.GetDouble()) > 1e-9)
                {
                    aDifferences.Add($"{aPath}: {aReference.GetDouble().ToString(CultureInfo.InvariantCulture)} vs {aActual.Display()}");
                }

                break;

            case JsonValueKind.Null:
                // The reference prints null where it refuses a figure; TfLens says which refusal it is.
                if (aActual.HasValue)
                {
                    aDifferences.Add($"{aPath}: the reference refuses a number, TfLens produced {aActual.Display()}");
                }

                break;

            case JsonValueKind.String:
                var vExpected = NormaliseRefusal(aReference.GetString()!);
                if (vExpected != aActual.Display())
                {
                    aDifferences.Add($"{aPath}: \"{vExpected}\" vs \"{aActual.Display()}\"");
                }

                break;

            default:
                aDifferences.Add($"{aPath}: unexpected reference value kind {aReference.ValueKind}");
                break;
        }
    }

    /// <summary>
    /// Strips the reference's per-metric wording from an <c>insufficient data</c> string.
    /// </summary>
    /// <param name="aReference">The reference's rendered value.</param>
    /// <returns>The refusal in the canonical form <see cref="Figure.Display"/> produces, or the value unchanged.</returns>
    /// <remarks>
    /// The oracle writes <c>insufficient data (n=2 build-phase runs)</c> for the rework ratio and
    /// <c>insufficient data (n=2)</c> everywhere else. The refusal and its <c>n</c> are what must match;
    /// the trailing noun is the reference's prose, and TfLens carries the unit on the label instead.
    /// </remarks>
    private static string NormaliseRefusal(string aReference)
    {
        var vMatch = Regex.Match(aReference, @"^insufficient data \(n=(\d+)");
        return vMatch.Success ? $"insufficient data (n={vMatch.Groups[1].Value})" : aReference;
    }

    /// <summary>Compares one integer property.</summary>
    /// <param name="aReference">The object holding it.</param>
    /// <param name="aName">The property name.</param>
    /// <param name="aActual">The engine's value.</param>
    /// <param name="aPath">The key path, for the difference message.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void CompareInt(
        JsonElement aReference,
        string aName,
        int aActual,
        string aPath,
        List<string> aDifferences)
    {
        var vExpected = aReference.GetProperty(aName).GetInt32();
        if (vExpected != aActual)
        {
            aDifferences.Add($"{aPath}.{aName}: {vExpected} vs {aActual}");
        }
    }

    /// <summary>Compares one nullable string property.</summary>
    /// <param name="aReference">The object holding it.</param>
    /// <param name="aName">The property name.</param>
    /// <param name="aActual">The engine's value.</param>
    /// <param name="aPath">The key path, for the difference message.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void CompareString(
        JsonElement aReference,
        string aName,
        string? aActual,
        string aPath,
        List<string> aDifferences)
    {
        var vProperty = aReference.GetProperty(aName);
        var vExpected = vProperty.ValueKind == JsonValueKind.Null ? null : vProperty.GetString();
        if (vExpected != aActual)
        {
            aDifferences.Add($"{aPath}.{aName}: {vExpected ?? "null"} vs {aActual ?? "null"}");
        }
    }
}
