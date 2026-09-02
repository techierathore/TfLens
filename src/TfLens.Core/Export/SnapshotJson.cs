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

    /// <summary>Decimal places the reference rounds tokens per miss fixed to.</summary>
    private const int TokensPerMissDigits = 1;

    /// <summary>Decimal places the reference rounds measured dollars per miss to.</summary>
    private const int MeasuredUsdDigits = 4;

    /// <summary>The reference's bucket for a record that carried no value on a distribution's axis.</summary>
    private const string NotRecordedKey = "?";

    /// <summary>
    /// Builds the whole document.
    /// </summary>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The JSON object, with the reference's keys first and the two TfLens additions last.</returns>
    public static JsonObject Build(SnapshotInputs aInputs)
    {
        var vDocument = new JsonObject
        {
            ["per_repo"] = PerRepo(aInputs),
            ["tainted_reqs"] = Strings(aInputs.Analysis.TaintedReqs),
            ["live"] = Segments(aInputs.Analysis.Live),
            ["backfilled"] = Segments(aInputs.Analysis.Backfilled),
            ["pooled"] = Pooled(aInputs.Analysis.Pooled),
            ["misses"] = Misses(aInputs),
            ["phases"] = Phases(aInputs.Analysis.Phases),
            ["extras"] = Extras(aInputs),
            ["parity"] = Parity(aInputs)
        };

        if (aInputs.Playbook is { } vPlaybook)
        {
            vDocument["playbook"] = Playbook(vPlaybook);
        }

        return vDocument;
    }

    /// <summary>
    /// The Playbook-native report set, written only into a <c>playbook</c> snapshot (REQ-FN-070).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key exists only when the snapshot's framework is <c>playbook</c>, so a TechieFlow document is
    /// byte-for-byte what it was before this block was added and <c>tools/parity-compare.py</c> still
    /// walks it against the reference with no added key to allow-list.
    /// </para>
    /// <para>
    /// Every gate name under <c>phase_gates</c> is a Playbook <b>process</b> gate; nothing on the
    /// TechieFlow assertion-gate axis appears in this block, and the top-level <c>live</c>,
    /// <c>backfilled</c> and <c>pooled</c> objects contain nothing from this one — the two axes are two
    /// disjoint subtrees of the document (SCHEMA.md §11, REQ-FN-066). Rates go through
    /// <see cref="Figure.Display"/>, so <c>insufficient data (n=…)</c> and <c>—</c> survive as
    /// themselves, and a phase whose events carried no spend emits a JSON <c>null</c> rather than a
    /// manufactured zero.
    /// </para>
    /// </remarks>
    /// <param name="aPlaybook">The Playbook report set.</param>
    /// <returns>The <c>playbook</c> object.</returns>
    private static JsonObject Playbook(PlaybookAnalysis aPlaybook)
    {
        var vRepos = new JsonArray();
        foreach (var vRepo in aPlaybook.PerRepo)
        {
            vRepos.Add(new JsonObject
            {
                ["repo"] = vRepo.Repo,
                ["events"] = vRepo.Events,
                ["sessions"] = vRepo.Sessions,
                ["phase_gates"] = vRepo.PhaseGates,
                ["earliest_ts"] = vRepo.EarliestTs,
                ["latest_ts"] = vRepo.LatestTs
            });
        }

        var vQuestions = aPlaybook.PhaseQuestions
            .ToDictionary(aQ => aQ.PhaseGate.Name, StringComparer.Ordinal);

        var vGates = new JsonArray();
        foreach (var vTotals in aPlaybook.PhaseTotals)
        {
            var vGate = new JsonObject
            {
                ["phase_gate"] = vTotals.PhaseGate.Name,
                ["events"] = vTotals.Events,
                ["sessions"] = vTotals.Sessions,
                ["tokens"] = vTotals.Tokens,
                ["cost_usd"] = vTotals.CostUsd
            };

            if (vQuestions.TryGetValue(vTotals.PhaseGate.Name, out var vRow))
            {
                vGate["first_pass_rate"] = vRow.FirstPassRate.Display();
                vGate["catch_share"] = vRow.CatchShare.Display();
                vGate["escape_rate"] = vRow.EscapeRate.Display();
                vGate["supporting_events"] = vRow.SupportingEvents;
                vGate["unavailable_reason"] = vRow.UnavailableReason;
            }

            vGates.Add(vGate);
        }

        var vModels = new JsonArray();
        foreach (var vModel in aPlaybook.TokensByModel)
        {
            vModels.Add(new JsonObject
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
            ["framework"] = aPlaybook.Framework,
            ["schema_status"] = aPlaybook.SchemaStatus.ToString(),
            ["parser_version"] = aPlaybook.ParserVersion,
            ["events_total"] = aPlaybook.EventsTotal,
            ["per_repo"] = vRepos,
            ["phase_gates"] = vGates,
            ["agent_split"] = new JsonObject
            {
                ["main_sessions"] = aPlaybook.AgentSplit.MainSessions,
                ["main_tokens"] = aPlaybook.AgentSplit.MainTokens,
                ["main_cost_usd"] = aPlaybook.AgentSplit.MainCostUsd,
                ["subagent_sessions"] = aPlaybook.AgentSplit.SubagentSessions,
                ["subagent_tokens"] = aPlaybook.AgentSplit.SubagentTokens,
                ["subagent_cost_usd"] = aPlaybook.AgentSplit.SubagentCostUsd,
                ["unresolved_parent_sessions"] = aPlaybook.AgentSplit.UnresolvedParentSessions,
                ["subagent_token_share"] = aPlaybook.AgentSplit.SubagentTokenShare.Display()
            },
            ["tokens_by_model"] = vModels,
            ["observed_fields"] = Strings(aPlaybook.ObservedFields),
            ["provisional_notes"] = Strings(aPlaybook.ProvisionalNotes),
            ["note"] =
                "Playbook process-gates (phase_gate) and TechieFlow assertion-gates (gate) are different "
                + "axes and never share a table, column or chart (SCHEMA.md §11, REQ-FN-066). Nothing in "
                + "this block is pooled with the per_repo, live, backfilled or pooled blocks above."
        };
    }

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
        var vOrigins = aInputs.RepoOrigins.ToDictionary(aO => aO.Repo, StringComparer.Ordinal);
        var vArray = new JsonArray();

        foreach (var vRepo in aInputs.Analysis.PerRepo)
        {
            var vOrigin = vOrigins.GetValueOrDefault(vRepo.Repo);

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
                ["misses"] = vOrigin?.Misses ?? 0,
                ["stale_types"] = Strings(vOrigin?.StaleProjectTypes ?? []),
                ["commit_hook"] = null,
                ["framework"] = vRepo.Framework,
                ["events"] = vRepo.Events,
                ["source_sha"] = vShas.GetValueOrDefault(vRepo.Repo),
                ["source_kind"] = vOrigin?.SourceKind ?? SourceKinds.Default
            });
        }

        return vArray;
    }

    /// <summary>
    /// The miss and rework block, in the reference's key layout (REQ-FN-080, BRD-128, BRD-129).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Twenty-nine keys, spelled and shaped exactly as <c>analyse_misses()</c> emits them, so
    /// <c>tools/parity-compare.py</c> walks them with no mapping layer. The counts come from
    /// <see cref="MissAnalysis"/>, which already totals them; every distribution, share and cost figure
    /// comes from <see cref="SnapshotInputs.MissParity"/>, the engine's own block computed in the one
    /// bucket the reference computes in.
    /// </para>
    /// <para>
    /// <b>The attribution split survives as three distinct keys</b> — <c>cost_sole_n</c>,
    /// <c>cost_shared_n</c> and <c>cost_unattributable_n</c> — and the two token columns beside them are
    /// likewise never added together (BRD-122, BRD-128, REQ-NFR-013 clause 1). There is no blended key
    /// here because <see cref="MissCost"/> has no property that could hold one. Measured dollars keep the
    /// bare <c>cost_usd_*</c> spelling because they are measurements; every rate-card figure lives under
    /// <c>extras.misses_repricing</c> and its key ends <c>_usd_estimate</c>.
    /// </para>
    /// <para>
    /// A category the emitter left blank is reported as <c>?</c>, which is the reference's own bucket for
    /// it. It is <b>not</b> a merge: the engine keeps "not assessed" out of every denominator
    /// (<see cref="MissSegmentFigures.ClassNotRecorded"/> and friends), and the <c>?</c> row is that same
    /// count rendered where the reference renders it.
    /// </para>
    /// </remarks>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The <c>misses</c> object.</returns>
    private static JsonObject Misses(SnapshotInputs aInputs)
    {
        var vTotals = aInputs.Analysis.Misses;
        var vBlock = aInputs.MissParity;
        var vAttribution = vBlock.Attribution;

        // The two cost_usd_* keys come from the sole-bounded row, not from the whole-segment one: the
        // reference bounds measured dollars by cost attribution exactly as it bounds the token columns,
        // and an apportioned record counted as a measured one would overstate cost_usd_records.
        var vMeasured = aInputs.MeasuredRework;

        return new JsonObject
        {
            ["misses_total"] = vTotals.MissesTotal,
            ["miss_fixes_total"] = vTotals.MissFixesTotal,
            ["orphan_fixes"] = vTotals.OrphanFixes,
            ["open_misses"] = vTotals.OpenMisses,
            ["wont_fix"] = vTotals.WontFix,
            ["resolved_misses"] = vTotals.ResolvedMisses,
            ["amendments_applied"] = vTotals.AmendmentsApplied,
            ["orphan_amends"] = vTotals.OrphanAmends,
            ["why_missed_n"] = vBlock.WhyMissedN,
            ["why_missed_eligible"] = vBlock.WhyMissedEligibility.Eligible,
            ["why_missed_predates_field"] = vBlock.WhyMissedEligibility.PredatesField,
            ["why_missed"] = Distribution(vBlock.FailedPracticeDistribution, 0),
            ["escapes_missing_why"] = vTotals.EscapesMissingWhy,
            ["class_distribution"] = Distribution(vBlock.ClassDistribution, vBlock.ClassNotRecorded),
            ["found_by"] = Distribution(vBlock.FoundBy, vBlock.FoundByNotRecorded),
            ["design_miss_share"] = vBlock.DesignMissShare.Display(),
            ["escape_share"] = vBlock.EscapeShare.Display(),
            ["attributed_n"] = vAttribution.AttributedN,
            ["attribution_excluded"] = vAttribution.AttributionExcluded,
            ["by_origin_phase"] = Distribution(vAttribution.ByOriginPhase, NotRecorded(vAttribution, vAttribution.ByOriginPhase)),
            ["by_origin_model"] = Distribution(vAttribution.ByOriginModel, NotRecorded(vAttribution, vAttribution.ByOriginModel)),
            ["by_origin_agent"] = Distribution(vAttribution.ByOriginAgent, NotRecorded(vAttribution, vAttribution.ByOriginAgent)),
            ["cost_sole_n"] = vBlock.Cost.SoleRecords,
            ["cost_shared_n"] = vBlock.Cost.SharedRecords,
            ["cost_unattributable_n"] = vBlock.Cost.TokensPerMissFixed.NoneCount,
            ["cost_recovered_n"] = vBlock.Cost.RecoveredRecords,
            ["tokens_per_miss_measured"] = Number(vBlock.Cost.TokensPerMissFixed.Sole, TokensPerMissDigits),
            ["tokens_per_miss_apportioned"] = Number(vBlock.Cost.TokensPerMissFixed.Apportioned, TokensPerMissDigits),
            ["cost_usd_per_miss_measured"] = vMeasured is null
                ? null
                : Number(vMeasured.MeasuredUsdPerMiss, MeasuredUsdDigits),
            ["cost_usd_records"] = vMeasured?.MeasuredUsdRecords ?? 0
        };
    }

    /// <summary>
    /// The phase-effort block, in the oracle's key layout (REQ-FN-093, BRD-152).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle emits this object under the top-level key <c>phases</c> of
    /// <c>tf-metrics.sh --report --json</c> and <c>--rollup --json</c>, so it is already reachable by the
    /// invocation BRD §13 step 2 already makes and <b>no new oracle run is needed</b>. Its inner
    /// <c>phases</c> map is keyed by <c>cmd</c>; the outer keys are the page-level denominators.
    /// </para>
    /// <para>
    /// Two shapes matter more than the rest and both are the reference's, not TfLens's. <b>Shares are
    /// strings.</b> <c>share_of_duration</c>, <c>share_of_tokens_out</c> and
    /// <c>subagent_share_of_tokens_out</c> come out of the engine as the oracle's own <c>87%</c> /
    /// <c>—</c> text and are written here verbatim, because BRD-152 diffs them as strings and any
    /// reformatting would be a diff against the oracle's own rendering. <b>And <c>null</c> is a real
    /// value.</b> <c>tokens_out_per_run</c>, <c>duration_s.median</c>, <c>spawns_median</c> and
    /// <c>spawns_max</c> come back from the oracle as a genuine <c>null</c> below the
    /// <see cref="MetricsConstants.MinN"/> floor, so they go through <see cref="Number"/>, which emits
    /// <c>null</c> for any figure that is not a value — a <c>0</c> on either side is a mismatch, not a
    /// rounding difference.
    /// </para>
    /// <para>
    /// <c>tokens_measured_n</c> and <c>tokens_unmeasured_n</c> are written as two keys and never as one:
    /// the second is the divisor's complement, not part of it, and a document that carried only the
    /// divisor would let a reader take the token totals for figures over every run (BRD-146). The same
    /// applies to <c>fanout.unobserved_not_tree</c> and <c>fanout.unobserved_predates_field</c>, which
    /// stay two counts because they are two facts (BRD-147, ADR-026).
    /// </para>
    /// </remarks>
    /// <param name="aPhases">The engine's phase-effort block.</param>
    /// <returns>The <c>phases</c> object.</returns>
    private static JsonObject Phases(PhaseEffortAnalysis aPhases)
    {
        var vRows = new JsonObject();

        foreach (var vRow in aPhases.Phases)
        {
            vRows[vRow.Cmd] = Phase(vRow);
        }

        return new JsonObject
        {
            ["runs_live"] = aPhases.RunsLive,
            ["scope_coverage"] = Counts(aPhases.ScopeCoverage),
            ["tokens_out_total"] = aPhases.TokensOutTotal,
            ["duration_s_total"] = aPhases.DurationSecondsTotal,
            ["note"] = PhaseEffortAnalysis.StandingNote,
            ["phases"] = vRows
        };
    }

    /// <summary>
    /// One phase's row.
    /// </summary>
    /// <remarks>
    /// <c>models</c> is built from the run's <c>model_tokens_out</c> split and never from its dominant
    /// <c>model</c> label, so a mixed-model window is never filed whole under its winner (BRD-150).
    /// <c>subagents_declared</c> sits beside the <c>fanout</c> block rather than inside it because the two
    /// are a self-report and a measurement, published together and never reconciled — where they disagree
    /// the measured one is authoritative (BRD-149, SCHEMA.md §2.6).
    /// </remarks>
    /// <param name="aRow">The engine's row for one <c>cmd</c>.</param>
    /// <returns>The phase object.</returns>
    private static JsonObject Phase(PhaseEffortRow aRow) => new()
    {
        ["runs"] = aRow.Runs,
        ["duration_s"] = new JsonObject
        {
            ["total"] = aRow.Duration.TotalSeconds,
            ["median"] = aRow.Duration.MedianSeconds,
            ["max"] = aRow.Duration.MaxSeconds,
            ["n"] = aRow.Duration.TimedN
        },
        ["share_of_duration"] = aRow.ShareOfDuration,
        ["tokens_measured_n"] = aRow.TokensMeasuredN,
        ["tokens_unmeasured_n"] = aRow.TokensUnmeasuredN,
        ["tokens"] = new JsonObject
        {
            ["in"] = aRow.Tokens.In,
            ["out"] = aRow.Tokens.Out,
            ["cache_read"] = aRow.Tokens.CacheRead,
            ["cache_write"] = aRow.Tokens.CacheWrite
        },
        ["tokens_out_median"] = aRow.TokensOutMedian,
        ["tokens_out_per_run"] = Number(aRow.TokensOutPerRun.Tokens, null),
        ["share_of_tokens_out"] = aRow.ShareOfTokensOut,
        ["models"] = Models(aRow.Models),
        ["harnesses"] = Counts(aRow.Harnesses),
        ["modes"] = Counts(aRow.Modes),
        ["build_result"] = Counts(aRow.BuildResults),
        ["reqs_touched_total"] = aRow.ReqsTouchedTotal,
        ["files_written_total"] = aRow.FilesWrittenTotal,
        ["subagents_declared"] = Counts(aRow.SubagentsDeclared),
        ["fanout"] = Fanout(aRow.Fanout),
        ["routing"] = new JsonObject
        {
            ["routed"] = aRow.Routing.Routed,
            ["drifted"] = aRow.Routing.Drifted,
            ["unknown"] = aRow.Routing.Unknown
        },
        ["cost_usd_by_harness"] = HarnessCosts(aRow.CostUsdByHarness)
    };

    /// <summary>
    /// The fan-out block — the denominator first, the numbers after (BRD-147, ADR-026).
    /// </summary>
    /// <remarks>
    /// <c>observed_n</c> is written first because it is what the block has to be read against: the figures
    /// below it describe <c>tokens_scope == "tree"</c> runs carrying <c>subagent_runs</c> and nothing
    /// else. The two exclusions are separate keys and their total is emitted beside them rather than
    /// instead of them, because <i>we did not look</i> can change tomorrow and <i>we could not have
    /// looked</i> never will.
    /// </remarks>
    /// <param name="aFanout">The engine's fan-out observation.</param>
    /// <returns>The <c>fanout</c> object.</returns>
    private static JsonObject Fanout(FanoutObservation aFanout) => new()
    {
        ["observed_n"] = aFanout.ObservedN,
        ["unobserved_n"] = aFanout.UnobservedN,
        ["unobserved_not_tree"] = aFanout.UnobservedNotTree,
        ["unobserved_predates_field"] = aFanout.UnobservedPredatesField,
        ["spawns_total"] = aFanout.SpawnsTotal,
        ["spawns_median"] = aFanout.Spawns,
        ["spawns_max"] = aFanout.SpawnsMax,
        ["runs_with_fanout"] = aFanout.RunsWithFanout,
        ["tokens_out_subagents"] = aFanout.TokensOutSubagents,
        ["subagent_share_of_tokens_out"] = aFanout.SubagentShareOfTokensOut
    };

    /// <summary>Renders the per-model split; the key is the model id the producer wrote.</summary>
    /// <param name="aModels">The engine's per-model rows.</param>
    /// <returns>The <c>models</c> object.</returns>
    private static JsonObject Models(IReadOnlyList<PhaseModelEffort> aModels)
    {
        var vObject = new JsonObject();

        foreach (var vModel in aModels)
        {
            vObject[vModel.Model] = new JsonObject
            {
                ["runs"] = vModel.Runs,
                ["tokens_out"] = vModel.TokensOut
            };
        }

        return vObject;
    }

    /// <summary>
    /// Renders measured dollars per harness — never a total across them (SCHEMA.md §4).
    /// </summary>
    /// <remarks>
    /// There is no cross-harness key here and no rate-card figure anywhere near it: only OpenCode measures
    /// spend, and a phase's dollars priced from a rate card would be an estimate presented as a
    /// measurement.
    /// </remarks>
    /// <param name="aCosts">The engine's per-harness rows.</param>
    /// <returns>The <c>cost_usd_by_harness</c> object.</returns>
    private static JsonObject HarnessCosts(IReadOnlyList<PhaseHarnessCost> aCosts)
    {
        var vObject = new JsonObject();

        foreach (var vCost in aCosts)
        {
            vObject[vCost.Harness] = new JsonObject
            {
                ["usd"] = vCost.Usd,
                ["records"] = vCost.Records
            };
        }

        return vObject;
    }

    /// <summary>
    /// Misses the attribution figures counted but that named no value for one axis.
    /// </summary>
    /// <remarks>
    /// The engine keeps a <c>null</c> out of every distribution so it can never become a bucket that
    /// inflates a share (BRD-119). The reference renders the same records as <c>?</c>, so the count is
    /// recovered here as "attributed records the rows do not account for" rather than being recomputed
    /// from the records — arithmetic on the engine's own output, never a second reading of the stream.
    /// </remarks>
    /// <param name="aAttribution">The attribution block, whose <c>AttributedN</c> is the denominator.</param>
    /// <param name="aRows">One axis's rows.</param>
    /// <returns>How many attributed misses carried no value on that axis.</returns>
    private static int NotRecorded(MissAttributionFigures aAttribution, IReadOnlyList<MissCategoryCount> aRows) =>
        Math.Max(0, aAttribution.AttributedN - aRows.Sum(aRow => aRow.Count));

    /// <summary>
    /// Renders a miss distribution the way the reference does, with the unrecorded records as <c>?</c>.
    /// </summary>
    /// <param name="aRows">The categories the engine observed.</param>
    /// <param name="aNotRecorded">Records carrying no value at all; omitted entirely when zero.</param>
    /// <returns>The distribution object.</returns>
    private static JsonObject Distribution(IReadOnlyList<MissCategoryCount> aRows, int aNotRecorded)
    {
        var vObject = new JsonObject();

        foreach (var vRow in aRows.Where(aRow => aRow.Count > 0))
        {
            vObject[vRow.Key] = vRow.Count;
        }

        if (aNotRecorded > 0)
        {
            vObject[NotRecordedKey] = aNotRecorded;
        }

        return vObject;
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
        ["session_duplicates_collapsed"] = aPooled.SessionDuplicatesCollapsed,
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
        ["repricing"] = Repricing(aInputs),
        ["misses_repricing"] = MissRepricing(aInputs)
    };

    /// <summary>
    /// What the rework tokens would have cost at the operator's rate card — an estimate, per harness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BRD-123 / BRD-128 / REQ-NFR-013 clause 5. Measured dollars exist in exactly one place in the
    /// product — <c>cost_usd</c> on OpenCode records — and they are reported under
    /// <c>misses.cost_usd_per_miss_measured</c>, without a suffix, because they are measurements. Every
    /// figure here is derived from token counts and a price list instead, so <b>every money key ends
    /// <c>_usd_estimate</c></b> and every row carries <see cref="RateCard.EstimateLabel"/> — the wording
    /// <see cref="MissHarnessCost.EstimateLabel"/> says such a figure must travel with.
    /// </para>
    /// <para>
    /// The block sits under <c>extras</c> and not under <c>misses</c> deliberately: the reference computes
    /// no rate-card dollars, so these figures have no parity oracle, and <c>extras</c> is where the export
    /// keeps everything that has none (REQ-FN-064). Nothing here is totalled across harnesses — a sum of
    /// numbers that mean different things per harness is a number nobody was billed (BRD-54).
    /// </para>
    /// </remarks>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The <c>misses_repricing</c> object.</returns>
    private static JsonObject MissRepricing(SnapshotInputs aInputs)
    {
        // A miss-fix record carries token counts but no `model`, so there is no observed mix to price it
        // at. The ceiling is priced instead — the same counterfactual `extras.repricing` already reports —
        // because "no more than this" is a statement the data supports and a point estimate is not.
        var vCeilingModel = aInputs.Routing.MostExpensiveModel;
        var vCeilingRate = aInputs.Prices.Find(vCeilingModel);
        var vRows = new JsonArray();

        foreach (var vRow in aInputs.MissParity.Cost.ByHarness)
        {
            // TokenRecords is read before the sums, not after: a sum over records that all carried null
            // is 0, and "0 tokens spent" and "no counts recorded" are different facts (SCHEMA.md §2.5).
            // Pricing the second as the first would manufacture a $0.00 rework cost out of missing data.
            var vPriceable = vRow.EstimateLabel is not null && vCeilingRate is not null && vRow.TokenRecords > 0;

            vRows.Add(new JsonObject
            {
                ["harness"] = vRow.Harness,
                ["fix_records"] = vRow.Records,
                ["token_records"] = vRow.TokenRecords,
                ["tokens_in"] = vRow.TokensIn,
                ["tokens_out"] = vRow.TokensOut,
                ["tokens_cache_read"] = vRow.TokensCacheRead,
                ["tokens_cache_write"] = vRow.TokensCacheWrite,
                ["measured"] = vRow.EstimateLabel is null,
                ["estimate_label"] = vRow.EstimateLabel,
                ["rework_at_max_usd_estimate"] = vPriceable
                    ? JsonValue.Create(vCeilingRate!.EstimateUsd(
                        vRow.TokensIn, vRow.TokensOut, vRow.TokensCacheRead, vRow.TokensCacheWrite))
                    : null
            });
        }

        return new JsonObject
        {
            ["estimate"] = true,
            ["estimate_label"] = RateCard.EstimateLabel,
            ["basis"] =
                "rework token counts multiplied by the operator's rate card at its most expensive model — "
                + "a ceiling, not measured spend. A miss-fix record carries no model, so no observed-mix "
                + "figure is computable and none is invented. Measured rework dollars are "
                + "misses.cost_usd_per_miss_measured and carry no _usd_estimate suffix because they are "
                + "measurements (SCHEMA.md §4, BRD-123).",
            ["rate_card_path"] = aInputs.RateCardPath,
            ["rate_card_units"] = RateCard.Units,
            ["priced_at_model"] = vCeilingModel,
            ["by_harness"] = vRows
        };
    }

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
                    "The only measured dollars in TfLens — Σ cost_usd over OpenCode session records "
                    + "(SCHEMA.md §4), deduped per session_id so a cumulative snapshot counts once. "
                    + "Claude Code and Codex report cost_usd as null "
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
        ["status_reason"] = aInputs.ParityReason,
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
