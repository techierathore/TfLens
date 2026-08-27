using System.Text.Json.Nodes;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Export;

/// <summary>
/// Renders <c>tflens.json</c> — the machine-readable half of a snapshot, in the reference's key layout.
/// </summary>
/// <remarks>
/// <para>
/// REQ-FN-058 / ADR-008: the top level is <c>per_repo</c>, <c>tainted_reqs</c>, <c>live</c>,
/// <c>backfilled</c>, <c>pooled</c> — exactly the keys <c>tf-metrics.sh --rollup --json</c> emits, with
/// exactly its spellings and value shapes — plus <c>extras</c> and <c>parity</c>, which are the only two
/// additional top-level keys and are the documented allow-list in <c>tools/parity-compare.py</c>. Keys
/// here are the <b>SCHEMA.md snake_case</b> spelling, never the PascalCase column spelling, so the
/// parity compare walks the two documents key-for-key with no mapping layer (Coding Standards, DB
/// naming note).
/// </para>
/// <para>
/// REQ-FN-059: nothing on this document merges provenances. <c>live</c> and <c>backfilled</c> are
/// separate maps keyed by <c>project_type</c>, exactly as the engine produces them; there is no total
/// row, no "all types" key and no cross-framework key, because <see cref="AnalysisResult"/> cannot
/// express one. Every repricing value's key ends <c>_usd_estimate</c> and sits beside an explicit
/// <c>estimate: true</c> marker and the <see cref="RateCard.EstimateLabel"/> wording, so a consumer
/// cannot read a rate-card figure as a measurement (SCHEMA.md §4). There is no cross-harness dollar
/// key anywhere in the document (BRD-54).
/// </para>
/// </remarks>
internal static class SnapshotJson
{
    /// <summary>Decimal places the reference rounds the throughput median to.</summary>
    private const int ThroughputDigits = 2;

    /// <summary>Decimal places the reference rounds tokens per Verified REQ to.</summary>
    private const int TokensPerVerifiedDigits = 1;

    /// <summary>Decimal places the reference rounds commit cadence to.</summary>
    private const int CadenceDigits = 2;

    /// <summary>
    /// Builds the whole document.
    /// </summary>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The JSON object, with the reference's keys first and the two TfLens additions last.</returns>
    public static JsonObject Build(SnapshotInputs aInputs) => new()
    {
        ["per_repo"] = PerRepo(aInputs),
        ["tainted_reqs"] = Strings(aInputs.Analysis.TaintedReqs),
        ["live"] = Segments(aInputs.Analysis.Live),
        ["backfilled"] = Segments(aInputs.Analysis.Backfilled),
        ["pooled"] = Pooled(aInputs.Analysis.Pooled),
        ["extras"] = Extras(aInputs),
        ["parity"] = Parity(aInputs)
    };

    /// <summary>
    /// One entry per repository the figures were computed from.
    /// </summary>
    /// <remarks>
    /// The reference's keys come first and unchanged. <c>commit_hook</c> is always <c>null</c> and that
    /// is the honest value, not a stub: the reference reads a clone's <c>.git/hooks</c> directory, and
    /// TfLens reads the GitHub REST API, which cannot see a hook that lives outside the repository. The
    /// reference itself emits <c>null</c> for "not a work tree — say nothing rather than warn", and
    /// that is precisely TfLens's position. <c>framework</c>, <c>events</c> and <c>source_sha</c> are
    /// TfLens additions: the first two because framework is a provenance axis here (ADR-016) and the
    /// third because REQ-FN-062 requires the dataset to be pinnable from the export.
    /// </remarks>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The <c>per_repo</c> array.</returns>
    private static JsonArray PerRepo(SnapshotInputs aInputs)
    {
        var vShas = aInputs.DatasetShas.ToDictionary(aP => aP.Key, aP => aP.Value, StringComparer.Ordinal);
        var vArray = new JsonArray();

        foreach (var vRepo in aInputs.Analysis.PerRepo)
        {
            vArray.Add(new JsonObject
            {
                ["repo"] = vRepo.Repo,
                ["app"] = vRepo.App,
                ["project_type"] = vRepo.ProjectType,
                ["gates"] = vRepo.Gates,
                ["gates_backfilled"] = vRepo.GatesBackfilled,
                ["runs"] = vRepo.Runs,
                ["sessions"] = vRepo.Sessions,
                ["commits"] = vRepo.Commits,
                ["commit_hook"] = null,
                ["framework"] = vRepo.Framework,
                ["events"] = vRepo.Events,
                ["source_sha"] = vShas.GetValueOrDefault(vRepo.Repo)
            });
        }

        return vArray;
    }

    /// <summary>
    /// Renders one provenance bucket, keyed by <c>project_type</c> and never merged with the other.
    /// </summary>
    /// <param name="aSegments">The engine's <c>Live</c> or <c>Backfilled</c> map.</param>
    /// <returns>The bucket, project types in ordinal order as the reference sorts them.</returns>
    private static JsonObject Segments(IReadOnlyDictionary<string, SegmentFigures> aSegments)
    {
        var vObject = new JsonObject();

        foreach (var vKey in aSegments.Keys.OrderBy(aK => aK, StringComparer.Ordinal))
        {
            vObject[vKey] = Segment(aSegments[vKey]);
        }

        return vObject;
    }

    /// <summary>
    /// Renders one (provenance, project type) segment in the reference's key order.
    /// </summary>
    /// <remarks>
    /// The rate keys carry the reference's <i>strings</i>, not numbers, because the reference prints
    /// either <c>67%</c>, <c>—</c> or <c>insufficient data (n=…)</c> in the same slot.
    /// <see cref="Figure.Display"/> produces exactly those three forms, so a figure the reference
    /// refuses to state is refused here in the identical words and the compare matches on it
    /// (REQ-FN-061).
    /// </remarks>
    /// <param name="aSegment">The segment's figures.</param>
    /// <returns>The segment object.</returns>
    private static JsonObject Segment(SegmentFigures aSegment) => new()
    {
        ["records"] = aSegment.Records,
        ["reqs_scored"] = aSegment.ReqsScored,
        ["reqs_excluded_backfill_taint"] = aSegment.ReqsExcludedBackfillTaint,
        ["first_pass_n"] = aSegment.FirstPassN,
        ["first_pass_rate"] = aSegment.FirstPassRate.Display(),
        ["gate_distribution"] = GateDistribution(aSegment.GateDistribution),
        ["gate_distribution_n"] = aSegment.GateDistributionN,
        ["gate_distribution_note"] = aSegment.GateDistributionNote,
        ["late_gate_coverage"] = LateGateCoverage(aSegment.LateGateCoverage),
        ["escape_rate"] = aSegment.EscapeRate.Display()
    };

    /// <summary>
    /// The failure counts per gate, omitting gates that caught nothing.
    /// </summary>
    /// <remarks>
    /// The reference omits a zero-count gate from the map entirely rather than emitting a zero, so the
    /// same filter is applied here: an absent key and a zero would otherwise be a spurious diff.
    /// </remarks>
    /// <param name="aCounts">The engine's distribution, already in the reference's gate order.</param>
    /// <returns>The distribution object.</returns>
    private static JsonObject GateDistribution(IReadOnlyList<GateCount> aCounts)
    {
        var vObject = new JsonObject();

        foreach (var vCount in aCounts.Where(aC => aC.Count > 0))
        {
            vObject[vCount.Gate] = vCount.Count;
        }

        return vObject;
    }

    /// <summary>
    /// Coverage of the late-added gates — <c>ran</c> beside <c>caught</c>, never a share.
    /// </summary>
    /// <param name="aCoverage">The engine's late-gate rows.</param>
    /// <returns>The coverage object, one key per late gate.</returns>
    private static JsonObject LateGateCoverage(IReadOnlyList<LateGateCoverage> aCoverage)
    {
        var vObject = new JsonObject();

        foreach (var vGate in aCoverage)
        {
            vObject[vGate.Gate] = new JsonObject
            {
                ["ran"] = vGate.Ran,
                ["caught"] = vGate.Caught,
                ["since"] = vGate.Since
            };
        }

        return vObject;
    }

    /// <summary>
    /// The metrics the reference exempts from the provenance separations.
    /// </summary>
    /// <remarks>
    /// <c>cost_usd</c> is emitted and is always <c>null</c> — the key exists so the layouts match
    /// key-for-key, and there is no code path that could give it a value (REQ-FN-051, SCHEMA.md §4).
    /// </remarks>
    /// <param name="aPooled">The engine's pooled block.</param>
    /// <returns>The <c>pooled</c> object.</returns>
    private static JsonObject Pooled(PooledMetrics aPooled) => new()
    {
        ["runs_total"] = aPooled.RunsTotal,
        ["runs_by_cmd"] = Counts(aPooled.RunsByCmd),
        ["rework_ratio"] = aPooled.ReworkRatio.Display(),
        ["throughput_median_reqs_per_hour"] = Number(aPooled.ThroughputMedianReqsPerHour, ThroughputDigits),
        ["batch_size_median"] = Number(aPooled.BatchSizeMedian, null),
        ["sessions"] = aPooled.Sessions,
        ["tokens_total"] = aPooled.TokensTotal,
        ["tokens_per_verified_req"] = Number(aPooled.TokensPerVerifiedReq, TokensPerVerifiedDigits),
        ["cost_usd"] = null,
        ["commits"] = aPooled.Commits,
        ["commit_duplicates_collapsed"] = aPooled.CommitDuplicatesCollapsed,
        ["active_days"] = aPooled.ActiveDays,
        ["commits_per_active_day"] = Number(aPooled.CommitsPerActiveDay, CadenceDigits)
    };

    /// <summary>
    /// The metrics that have no parity oracle, kept out of the reference's keys entirely.
    /// </summary>
    /// <remarks>
    /// REQ-FN-064: these are checked by hand against raw JSONL once and the check is recorded in
    /// <c>DECISIONS.md</c>. They live under one clearly named key so the parity compare can allow-list
    /// them explicitly instead of skipping unknown keys silently.
    /// </remarks>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The <c>extras</c> object.</returns>
    private static JsonObject Extras(SnapshotInputs aInputs) => new()
    {
        ["note"] =
            "Metrics tf-metrics.sh does not compute. They have no parity oracle and are spot-checked by "
            + "hand against raw JSONL, recorded in DECISIONS.md (REQ-FN-064).",
        ["harness"] = Harness(aInputs.Harness),
        ["routing"] = Routing(aInputs.Routing),
        ["repricing"] = Repricing(aInputs)
    };

    /// <summary>
    /// The per-harness comparison.
    /// </summary>
    /// <remarks>
    /// Three columns always, in the ADR-017 order, including for a harness with no records.
    /// <c>not_detected_records</c> is the footnote for <c>harness: null</c> records — they are counted
    /// here and are not in any column, and there is no fourth column. <c>opencode_cost_usd</c> is the
    /// only measured money in the document and is marked <c>measured: true</c> to distinguish it from
    /// everything under <c>repricing</c>; there is deliberately no key totalling dollars across
    /// harnesses (BRD-54).
    /// </remarks>
    /// <param name="aHarness">The harness comparison.</param>
    /// <returns>The <c>harness</c> object.</returns>
    private static JsonObject Harness(HarnessComparison aHarness)
    {
        var vColumns = new JsonArray();

        foreach (var vColumn in aHarness.Columns)
        {
            vColumns.Add(new JsonObject
            {
                ["harness"] = vColumn.Harness,
                ["runs"] = vColumn.Runs,
                ["runs_by_cmd"] = Counts(vColumn.RunsByCmd),
                ["gate_records"] = vColumn.GateRecords,
                ["verdict_mix"] = Counts(vColumn.VerdictMix),
                ["sessions"] = vColumn.Sessions,
                ["tokens_in"] = vColumn.TokensIn,
                ["tokens_out"] = vColumn.TokensOut,
                ["tokens_cache_read"] = vColumn.TokensCacheRead,
                ["tokens_cache_write"] = vColumn.TokensCacheWrite,
                ["tokens_per_verified_req"] = vColumn.TokensPerVerifiedReq.Display()
            });
        }

        return new JsonObject
        {
            ["columns"] = vColumns,
            ["not_detected_records"] = aHarness.NotDetectedRecords,
            ["not_detected_note"] =
                $"{aHarness.NotDetectedRecords} records with harness not detected — excluded from the "
                + "columns above, never merged into a named harness (SCHEMA.md §1, ADR-017).",
            ["opencode_cost_usd"] = new JsonObject
            {
                ["value"] = aHarness.OpenCodeCostUsd,
                ["measured"] = true,
                ["note"] =
                    "The only measured dollars in TfLens. Claude Code and Codex report cost_usd as null "
                    + "by design and are never estimated into it; no total across harnesses exists."
            }
        };
    }

    /// <summary>
    /// Routing drift and tokens by observed model.
    /// </summary>
    /// <param name="aRouting">The routing analysis.</param>
    /// <returns>The <c>routing</c> object.</returns>
    private static JsonObject Routing(RoutingAnalysis aRouting)
    {
        var vDrift = new JsonArray();
        foreach (var vRow in aRouting.Drift)
        {
            vDrift.Add(new JsonObject
            {
                ["ts"] = vRow.Ts,
                ["cmd"] = vRow.Cmd,
                ["tier"] = vRow.Tier,
                ["tier_model"] = vRow.TierModel,
                ["model"] = vRow.Model,
                ["models"] = vRow.Models,
                ["routed"] = vRow.Routed
            });
        }

        var vTokens = new JsonArray();
        foreach (var vModel in aRouting.TokensByModel)
        {
            vTokens.Add(new JsonObject
            {
                ["model"] = vModel.Model,
                ["tokens_in"] = vModel.TokensIn,
                ["tokens_out"] = vModel.TokensOut,
                ["tokens_cache_read"] = vModel.TokensCacheRead,
                ["tokens_cache_write"] = vModel.TokensCacheWrite,
                ["tokens_total"] = vModel.Total
            });
        }

        return new JsonObject
        {
            ["runs_with_routing_fields"] = aRouting.RunsWithRoutingFields,
            ["unrouted_runs"] = aRouting.UnroutedRuns,
            ["distinct_models"] = aRouting.DistinctModels,
            ["drift"] = vDrift,
            ["tokens_by_model"] = vTokens
        };
    }

    /// <summary>
    /// The counterfactual repricing — every value an estimate, and labelled as one.
    /// </summary>
    /// <remarks>
    /// BRD-59 and SCHEMA.md §4. Three things make the estimate unmissable to a machine reader: the
    /// <c>estimate</c> flag, the <c>estimate_label</c> string, and the <c>_usd_estimate</c> key suffix
    /// on every money value. <c>runs_excluded_no_token_scope</c> states how many runs were left out for
    /// want of a token window (BRD-60), and <c>missing_price_models</c> names the observed models the
    /// rate card does not price so the UI can warn about them by name rather than pricing them at zero.
    /// </remarks>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The <c>repricing</c> object.</returns>
    private static JsonObject Repricing(SnapshotInputs aInputs) => new()
    {
        ["estimate"] = true,
        ["estimate_label"] = RateCard.EstimateLabel,
        ["basis"] = "observed token counts multiplied by the operator's rate card; not measured spend",
        ["rate_card_path"] = aInputs.RateCardPath,
        ["rate_card_units"] = RateCard.Units,
        ["actual_mix_usd_estimate"] = aInputs.Routing.ActualMixUsd,
        ["all_at_max_usd_estimate"] = aInputs.Routing.AllAtMaxUsd,
        ["delta_usd_estimate"] = aInputs.Routing.DeltaUsd,
        ["most_expensive_model"] = aInputs.Routing.MostExpensiveModel,
        ["runs_excluded_no_token_scope"] = aInputs.Routing.RunsExcludedNoTokenScope,
        ["missing_price_models"] = Strings(aInputs.Routing.MissingPriceModels)
    };

    /// <summary>
    /// The parity stamp — what makes the rest of the document quotable, or not.
    /// </summary>
    /// <remarks>
    /// REQ-FN-060 puts the parser version here, and REQ-FN-062 the dataset SHAs. The record itself is
    /// read from <c>data/parity-last.json</c>, which only a passing compare writes, so nothing in this
    /// document can claim quotability on its own authority.
    /// </remarks>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The <c>parity</c> object.</returns>
    private static JsonObject Parity(SnapshotInputs aInputs) => new()
    {
        ["status"] = aInputs.ParityStatus,
        ["parser_version"] = aInputs.Analysis.ParserVersion,
        ["parser_version_validated"] = aInputs.Parity?.ParserVersion,
        ["last_pass_date"] = aInputs.Parity?.Date,
        ["script_path"] = aInputs.Parity?.ScriptPath,
        ["script_hash"] = aInputs.Parity?.ScriptHash,
        ["compare_command"] = aInputs.Parity?.CompareCommand,
        ["compare_output"] = aInputs.Parity?.CompareOutput,
        ["dataset_shas"] = Pairs(aInputs.DatasetShas),
        ["framework"] = aInputs.Framework,
        ["user_id"] = aInputs.UserId,
        ["generated_ts"] = aInputs.GeneratedTs,
        ["source"] = "data/parity-last.json — written only by a passing tools/parity-compare.py run"
    };

    /// <summary>Renders a string list as a JSON array.</summary>
    /// <param name="aValues">The strings.</param>
    /// <returns>The array.</returns>
    private static JsonArray Strings(IEnumerable<string> aValues)
    {
        var vArray = new JsonArray();
        foreach (var vValue in aValues)
        {
            vArray.Add(vValue);
        }

        return vArray;
    }

    /// <summary>Renders label-count pairs as a JSON object, preserving the caller's order.</summary>
    /// <param name="aCounts">The pairs.</param>
    /// <returns>The object.</returns>
    private static JsonObject Counts(IEnumerable<KeyValuePair<string, int>> aCounts)
    {
        var vObject = new JsonObject();
        foreach (var vCount in aCounts)
        {
            vObject[vCount.Key] = vCount.Value;
        }

        return vObject;
    }

    /// <summary>Renders string pairs as a JSON object.</summary>
    /// <param name="aPairs">The pairs.</param>
    /// <returns>The object.</returns>
    private static JsonObject Pairs(IEnumerable<KeyValuePair<string, string>> aPairs)
    {
        var vObject = new JsonObject();
        foreach (var vPair in aPairs)
        {
            vObject[vPair.Key] = vPair.Value;
        }

        return vObject;
    }

    /// <summary>
    /// Renders a numeric figure the way the reference does — a number, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The reference emits <c>null</c> both when there are too few supporting records and when the
    /// metric does not apply, so both non-value cases of <see cref="Figure"/> land on <c>null</c> here.
    /// The <c>insufficient data (n=…)</c> wording is carried in the string-valued keys and in the
    /// markdown, where the reference also carries it.
    /// </remarks>
    /// <param name="aFigure">The figure.</param>
    /// <param name="aDigits">Decimal places to round to, or <c>null</c> to emit the value unrounded.</param>
    /// <returns>The number, or a JSON null.</returns>
    private static JsonNode? Number(Figure aFigure, int? aDigits) =>
        aFigure.TryGetValue(out var vValue)
            ? JsonValue.Create(aDigits is { } vDigits ? Math.Round(vValue, vDigits, MidpointRounding.ToEven) : vValue)
            : null;
}
