using System.Globalization;
using System.Text;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Export;

/// <summary>
/// Renders <c>snapshot.md</c> — the human half of a snapshot, sectioned exactly like the report pages.
/// </summary>
/// <remarks>
/// REQ-FN-056 fixes the sectioning: Coverage, Three questions, Harness comparison, Routing &amp;
/// economics, Snapshot / parity — the five pages, in nav order. REQ-FN-059 fixes the content rule:
/// no heading, row or sentence here combines live with backfilled, one <c>project_type</c> with
/// another, or one framework with another, because the shape it renders (<see cref="AnalysisResult"/>)
/// cannot express such a value; and every repricing number is printed with
/// <see cref="RateCard.EstimateLabel"/> attached to it, not merely somewhere on the page.
/// </remarks>
internal static class SnapshotMarkdown
{
    /// <summary>
    /// Renders the whole document.
    /// </summary>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    /// <returns>The markdown text.</returns>
    public static string Build(SnapshotInputs aInputs)
    {
        var vText = new StringBuilder();

        Header(vText, aInputs);
        Coverage(vText, aInputs);
        ThreeQuestions(vText, aInputs);
        Harness(vText, aInputs);
        Routing(vText, aInputs);
        Playbook(vText, aInputs);
        Parity(vText, aInputs);

        return vText.ToString();
    }

    /// <summary>
    /// The Playbook-native report set, written only into a <c>playbook</c> snapshot (REQ-FN-070).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The section is absent from a TechieFlow snapshot rather than present-and-empty, because a
    /// document that says nothing about the Playbook axis is honest and a document with an empty
    /// Playbook table reads as "there was nothing there".
    /// </para>
    /// <para>
    /// Every row is keyed by a <b>process</b> gate. No heading, row or sentence in this section combines
    /// it with the TechieFlow assertion-gate tables above (SCHEMA.md §11, REQ-FN-066). A phase whose
    /// events carried no spend prints <c>—</c>, and the three questions print whatever
    /// <see cref="Figure.Display"/> gives them, so <c>insufficient data (n=…)</c> reaches the page in
    /// those words rather than as a number.
    /// </para>
    /// </remarks>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Playbook(StringBuilder aText, SnapshotInputs aInputs)
    {
        if (aInputs.Playbook is not { } vPlaybook)
        {
            return;
        }

        aText.AppendLine("## Playbook (phase_gate axis)").AppendLine();
        aText.AppendLine(
            "> Playbook process-gates (`phase_gate`) and TechieFlow assertion-gates (`gate`) are "
            + "different axes and never share a table, column or chart (SCHEMA.md §11). Nothing below is "
            + "pooled with anything above it.")
            .AppendLine();

        aText.Append("**Schema status:** ").Append(vPlaybook.SchemaStatus.ToString()).AppendLine("  ");
        aText.Append("**Events:** ").Append(vPlaybook.EventsTotal.ToString(CultureInfo.InvariantCulture))
            .Append(" across ").Append(vPlaybook.PerRepo.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine(" repository/ies  ");
        aText.Append("**Observed fields:** ")
            .AppendLine(vPlaybook.ObservedFields.Count == 0
                ? "—"
                : string.Join(", ", vPlaybook.ObservedFields.Select(aF => "`" + aF + "`")))
            .AppendLine();

        PlaybookPhases(aText, vPlaybook);
        PlaybookSplit(aText, vPlaybook);
        PlaybookModels(aText, vPlaybook);

        foreach (var vNote in vPlaybook.ProvisionalNotes)
        {
            aText.Append("- ⚠ ").AppendLine(vNote);
        }

        aText.AppendLine();
    }

    /// <summary>
    /// The per-process-gate totals and the three questions asked of each of them.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aPlaybook">The Playbook report set.</param>
    private static void PlaybookPhases(StringBuilder aText, PlaybookAnalysis aPlaybook)
    {
        aText.AppendLine("### Phase totals and the three questions").AppendLine();

        if (aPlaybook.PhaseTotals.Count == 0)
        {
            aText.AppendLine("_No events._").AppendLine();
            return;
        }

        var vQuestions = aPlaybook.PhaseQuestions
            .ToDictionary(aQ => aQ.PhaseGate.Name, StringComparer.Ordinal);

        aText.AppendLine(
            "| phase_gate | Events | Sessions | Tokens | Cost | First-pass rate | Catch share | Escape rate |");
        aText.AppendLine("|---|---:|---:|---:|---:|---|---|---|");

        foreach (var vTotals in aPlaybook.PhaseTotals)
        {
            var vHasRow = vQuestions.TryGetValue(vTotals.PhaseGate.Name, out var vRow);

            aText.Append("| `").Append(vTotals.PhaseGate.Name)
                .Append("` | ").Append(vTotals.Events.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(vTotals.Sessions.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(vTotals.Tokens.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(Measured(vTotals.CostUsd))
                .Append(" | ").Append(vHasRow ? vRow!.FirstPassRate.Display() : "—")
                .Append(" | ").Append(vHasRow ? vRow!.CatchShare.Display() : "—")
                .Append(" | ").Append(vHasRow ? vRow!.EscapeRate.Display() : "—")
                .AppendLine(" |");
        }

        aText.AppendLine();

        var vReason = aPlaybook.PhaseQuestions.Select(aQ => aQ.UnavailableReason).FirstOrDefault(aR => aR is not null);
        if (vReason is not null)
        {
            aText.Append("The three questions are not applicable: ").AppendLine(vReason).AppendLine();
        }
    }

    /// <summary>
    /// The main-vs-sub-agent split resolved through the <c>parentID</c> chain.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aPlaybook">The Playbook report set.</param>
    private static void PlaybookSplit(StringBuilder aText, PlaybookAnalysis aPlaybook)
    {
        var vSplit = aPlaybook.AgentSplit;

        aText.AppendLine("### Main vs sub-agent (parentID chain)").AppendLine();
        aText.AppendLine("| Side | Sessions | Tokens | Cost |");
        aText.AppendLine("|---|---:|---:|---:|");
        aText.Append("| Main | ").Append(vSplit.MainSessions.ToString(CultureInfo.InvariantCulture))
            .Append(" | ").Append(vSplit.MainTokens.ToString(CultureInfo.InvariantCulture))
            .Append(" | ").Append(Measured(vSplit.MainCostUsd)).AppendLine(" |");
        aText.Append("| Sub-agent | ").Append(vSplit.SubagentSessions.ToString(CultureInfo.InvariantCulture))
            .Append(" | ").Append(vSplit.SubagentTokens.ToString(CultureInfo.InvariantCulture))
            .Append(" | ").Append(Measured(vSplit.SubagentCostUsd)).AppendLine(" |");
        aText.AppendLine();

        aText.Append("Sub-agent share of tokens: ").Append(vSplit.SubagentTokenShare.Display())
            .Append(". Sessions whose parent chain never reached a known root: ")
            .Append(vSplit.UnresolvedParentSessions.ToString(CultureInfo.InvariantCulture))
            .AppendLine(" — counted as sub-agent rather than promoted to main.")
            .AppendLine();
    }

    /// <summary>
    /// Tokens by observed model — the Playbook half of the routing view (BRD-75).
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aPlaybook">The Playbook report set.</param>
    private static void PlaybookModels(StringBuilder aText, PlaybookAnalysis aPlaybook)
    {
        aText.AppendLine("### Tokens by model").AppendLine();

        if (aPlaybook.TokensByModel.Count == 0)
        {
            aText.AppendLine("_No event carried a model field._").AppendLine();
            return;
        }

        aText.AppendLine("| Model | In | Out | Cache read | Cache write | Total |");
        aText.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var vModel in aPlaybook.TokensByModel)
        {
            aText.Append("| `").Append(vModel.Model)
                .Append("` | ").Append(vModel.TokensIn.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(vModel.TokensOut.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(vModel.TokensCacheRead.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(vModel.TokensCacheWrite.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(vModel.Total.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        aText.AppendLine();
    }

    /// <summary>
    /// Renders a <b>measured</b> money value, or an em dash when the events carried none.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Money(decimal?)"/>, which labels its number an estimate: Playbook
    /// <c>cost</c> is reported by the harness itself, so it must not carry the "(est.)" suffix — and an
    /// absent one must never become <c>$0.00</c> (SCHEMA.md §4).
    /// </remarks>
    /// <param name="aValue">The measured amount, or <c>null</c>.</param>
    /// <returns>The rendered cell.</returns>
    private static string Measured(decimal? aValue) =>
        aValue is { } vValue ? "$" + vValue.ToString("F2", CultureInfo.InvariantCulture) : "—";

    /// <summary>
    /// The title block, carrying the parser version and the quotable stamp.
    /// </summary>
    /// <remarks>REQ-FN-060 — the version is in the markdown header as well as in the JSON.</remarks>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Header(StringBuilder aText, SnapshotInputs aInputs)
    {
        aText.Append("# TfLens snapshot — ").Append(aInputs.Framework).Append(" — ")
            .Append(aInputs.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).AppendLine()
            .AppendLine();

        aText.Append("**Parity status:** ").Append(aInputs.ParityStatus).AppendLine("  ");
        aText.Append("**Parser version:** `").Append(aInputs.Analysis.ParserVersion).AppendLine("`  ");
        aText.Append("**Framework:** ").Append(aInputs.Framework)
            .AppendLine(" — figures never pool across frameworks (ADR-016).  ");
        aText.Append("**Generated:** ").Append(aInputs.GeneratedTs).AppendLine("  ");
        aText.Append("**User id:** ").Append(aInputs.UserId.ToString(CultureInfo.InvariantCulture))
            .AppendLine().AppendLine();

        aText.AppendLine(
            "> Live and backfilled records never pool, and `project_type` segments never pool, for "
            + "first-pass rate, gate catch distribution or escape rate (SCHEMA.md §6). There is no total "
            + "row in this document because the result type cannot express one.")
            .AppendLine();
    }

    /// <summary>
    /// Section 1 — Coverage / health: what the figures were computed from, and at which SHA.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Coverage(StringBuilder aText, SnapshotInputs aInputs)
    {
        var vShas = aInputs.DatasetShas.ToDictionary(aP => aP.Key, aP => aP.Value, StringComparer.Ordinal);

        aText.AppendLine("## Coverage / health").AppendLine();
        aText.AppendLine("| Repo | App | Project type | Framework | Gates | of which backfilled | Runs | Sessions | Commits | Events | Dataset SHA |");
        aText.AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|---:|---|");

        foreach (var vRepo in aInputs.Analysis.PerRepo)
        {
            aText.Append("| ").Append(vRepo.Repo)
                .Append(" | ").Append(Dash(vRepo.App))
                .Append(" | ").Append(Dash(vRepo.ProjectType))
                .Append(" | ").Append(vRepo.Framework)
                .Append(" | ").Append(vRepo.Gates)
                .Append(" | ").Append(vRepo.GatesBackfilled)
                .Append(" | ").Append(vRepo.Runs)
                .Append(" | ").Append(vRepo.Sessions)
                .Append(" | ").Append(vRepo.Commits)
                .Append(" | ").Append(vRepo.Events)
                .Append(" | `").Append(Dash(vShas.GetValueOrDefault(vRepo.Repo))).AppendLine("` |");
        }

        aText.AppendLine();
        aText.AppendLine(
            "The dataset SHAs above are the commits the streams were read at (REQ-FN-062). Check them "
            + "out to re-run `tf-metrics.sh` against exactly this data.")
            .AppendLine();
    }

    /// <summary>
    /// Section 2 — Three questions, one table per provenance and project type. Never a total.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void ThreeQuestions(StringBuilder aText, SnapshotInputs aInputs)
    {
        aText.AppendLine("## Three questions").AppendLine();

        SegmentTables(aText, "Live", aInputs.Analysis.Live);
        SegmentTables(aText, "Backfilled", aInputs.Analysis.Backfilled);

        aText.AppendLine("### Tainted REQs").AppendLine();
        aText.AppendLine(
            "REQs with at least one backfilled record. They are excluded from the **live** first-pass "
            + "rate because their live `attempt` numbering restarts at 1 (SCHEMA.md §3.1); the list is "
            + "shown rather than silently applied.")
            .AppendLine();

        aText.AppendLine(aInputs.Analysis.TaintedReqs.Count == 0
            ? "_None._"
            : string.Join(", ", aInputs.Analysis.TaintedReqs.Select(aR => "`" + aR + "`")))
            .AppendLine();
    }

    /// <summary>
    /// Renders one provenance bucket's tables, one per project type, each labelled with both.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aLabel">The provenance label — <c>Live</c> or <c>Backfilled</c>.</param>
    /// <param name="aSegments">The bucket's segments.</param>
    private static void SegmentTables(
        StringBuilder aText,
        string aLabel,
        IReadOnlyDictionary<string, SegmentFigures> aSegments)
    {
        if (aSegments.Count == 0)
        {
            aText.Append("### ").Append(aLabel).AppendLine().AppendLine("_No records._").AppendLine();
            return;
        }

        foreach (var vKey in aSegments.Keys.OrderBy(aK => aK, StringComparer.Ordinal))
        {
            var vSegment = aSegments[vKey];

            aText.Append("### ").Append(aLabel).Append(" · `").Append(vKey).AppendLine("`").AppendLine();
            aText.AppendLine("| Figure | Value |");
            aText.AppendLine("|---|---|");
            aText.Append("| Gate records | ").Append(vSegment.Records).AppendLine(" |");
            aText.Append("| REQs scored | ").Append(vSegment.ReqsScored).AppendLine(" |");
            aText.Append("| REQs excluded (backfill taint) | ").Append(vSegment.ReqsExcludedBackfillTaint)
                .AppendLine(" |");
            aText.Append("| First-pass REQs | ").Append(vSegment.FirstPassN).AppendLine(" |");
            aText.Append("| First-pass rate | ").Append(vSegment.FirstPassRate.Display()).AppendLine(" |");
            aText.Append("| Escape rate | ").Append(vSegment.EscapeRate.Display()).AppendLine(" |");
            aText.AppendLine();

            GateDistribution(aText, vSegment);
            LateGates(aText, vSegment);
        }
    }

    /// <summary>Renders the gate catch distribution with its honest denominator.</summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aSegment">The segment's figures.</param>
    private static void GateDistribution(StringBuilder aText, SegmentFigures aSegment)
    {
        aText.Append("Gate catch distribution over ").Append(aSegment.GateDistributionN)
            .AppendLine(" failure records.").AppendLine();

        if (aSegment.GateDistributionNote is { } vNote)
        {
            aText.Append("> ").Append(vNote).AppendLine(" — shares are not stated.").AppendLine();
        }

        if (aSegment.GateDistribution.Count == 0)
        {
            aText.AppendLine("_No failures recorded._").AppendLine();
            return;
        }

        aText.AppendLine("| Gate | Failures | Share |");
        aText.AppendLine("|---|---:|---:|");
        foreach (var vGate in aSegment.GateDistribution)
        {
            aText.Append("| ").Append(vGate.Gate).Append(" | ").Append(vGate.Count)
                .Append(" | ").Append(vGate.Share).AppendLine(" |");
        }

        aText.AppendLine();
    }

    /// <summary>
    /// Renders late-gate coverage as <c>ran</c> beside <c>caught</c>, never as a share.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aSegment">The segment's figures.</param>
    private static void LateGates(StringBuilder aText, SegmentFigures aSegment)
    {
        if (aSegment.LateGateCoverage.Count == 0)
        {
            return;
        }

        aText.AppendLine("Late-added gates — `ran` beside `caught`; their share of the raw distribution is "
            + "structurally understated and is never presented as a catch rate (SCHEMA.md §3.5).")
            .AppendLine();
        aText.AppendLine("| Gate | Added | Records that ran it | Caught | Catch rate |");
        aText.AppendLine("|---|---|---:|---:|---:|");

        foreach (var vGate in aSegment.LateGateCoverage)
        {
            aText.Append("| ").Append(vGate.Gate).Append(" | ").Append(vGate.Since)
                .Append(" | ").Append(vGate.Ran).Append(" | ").Append(vGate.Caught)
                .Append(" | ").Append(vGate.CatchRate.Display()).AppendLine(" |");
        }

        aText.AppendLine();
    }

    /// <summary>
    /// Section 3 — Harness comparison. Three columns, a footnote, and no dollar total.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Harness(StringBuilder aText, SnapshotInputs aInputs)
    {
        aText.AppendLine("## Harness comparison").AppendLine();
        aText.AppendLine(
            "> Tokens may be compared across harness; **dollars may not** (SCHEMA.md §2.5). There is no "
            + "cross-harness dollar total in this document.")
            .AppendLine();

        aText.AppendLine("| Figure | " + string.Join(" | ", aInputs.Harness.Columns.Select(aC => "`" + aC.Harness + "`")) + " |");
        aText.AppendLine("|---" + string.Concat(aInputs.Harness.Columns.Select(_ => "|---:")) + "|");

        Row(aText, "Runs", aInputs.Harness.Columns, aC => aC.Runs.ToString(CultureInfo.InvariantCulture));
        Row(aText, "Runs by cmd (top 3)", aInputs.Harness.Columns, aC => Counts(aC.RunsByCmd));
        Row(aText, "Gate records", aInputs.Harness.Columns, aC => aC.GateRecords.ToString(CultureInfo.InvariantCulture));
        Row(aText, "Verdict mix", aInputs.Harness.Columns, aC => Counts(aC.VerdictMix));
        Row(aText, "Sessions", aInputs.Harness.Columns, aC => aC.Sessions.ToString(CultureInfo.InvariantCulture));
        Row(aText, "Tokens in", aInputs.Harness.Columns, aC => aC.TokensIn.ToString(CultureInfo.InvariantCulture));
        Row(aText, "Tokens out", aInputs.Harness.Columns, aC => aC.TokensOut.ToString(CultureInfo.InvariantCulture));
        Row(aText, "Cache read", aInputs.Harness.Columns, aC => aC.TokensCacheRead.ToString(CultureInfo.InvariantCulture));
        Row(aText, "Cache write", aInputs.Harness.Columns, aC => aC.TokensCacheWrite.ToString(CultureInfo.InvariantCulture));
        Row(aText, "Tokens per Verified REQ", aInputs.Harness.Columns, aC => aC.TokensPerVerifiedReq.Display());

        aText.AppendLine();
        aText.Append("**").Append(aInputs.Harness.NotDetectedRecords)
            .AppendLine(" records with harness not detected** — excluded from the columns above and never "
                + "merged into a named harness. A missing label is merely missing (SCHEMA.md §1, ADR-017).")
            .AppendLine();

        aText.AppendLine("### Measured dollars (OpenCode only)").AppendLine();
        aText.Append(aInputs.Harness.OpenCodeCostUsd is { } vCost
                ? "**$" + vCost.ToString("F2", CultureInfo.InvariantCulture)
                    + "** — Σ measured `cost_usd` over OpenCode sessions (SCHEMA.md §4)."
                : "**Not measured.** No OpenCode session in this dataset carries a `cost_usd` measurement.")
            .AppendLine().AppendLine();
        aText.AppendLine(
            "Claude Code and Codex report `cost_usd` as null by design and are **not** estimated into "
            + "this figure. These are the only measured dollars in TfLens; they are never added to the "
            + "rate-card estimates below, and no total across harnesses exists (BRD-53, BRD-54).")
            .AppendLine();
    }

    /// <summary>
    /// Section 4 — Routing &amp; economics, including the repricing estimates and the poolable metrics.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Routing(StringBuilder aText, SnapshotInputs aInputs)
    {
        var vRouting = aInputs.Routing;

        aText.AppendLine("## Routing & economics").AppendLine();
        aText.Append("Runs carrying routing fields: **").Append(vRouting.RunsWithRoutingFields)
            .Append("** · runs not routed through their tier: **").Append(vRouting.UnroutedRuns)
            .Append("** · distinct observed models: **").Append(vRouting.DistinctModels).AppendLine("**")
            .AppendLine();
        aText.AppendLine(
            "> Routing is observed, never enforced. `routed: false` is drift made visible, not an error "
            + "(SCHEMA.md §2.5).")
            .AppendLine();

        Drift(aText, aInputs);
        TokensByModel(aText, aInputs);
        Repricing(aText, aInputs);
        Poolable(aText, aInputs);
    }

    /// <summary>Renders the drift table, unrouted runs first.</summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Drift(StringBuilder aText, SnapshotInputs aInputs)
    {
        aText.AppendLine("### Routing drift").AppendLine();

        if (aInputs.Routing.Drift.Count == 0)
        {
            aText.AppendLine("_No run in this dataset carries routing fields._").AppendLine();
            return;
        }

        aText.AppendLine("| Ts | Cmd | Tier | Tier model | Observed model | Models | Routed |");
        aText.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var vRow in aInputs.Routing.Drift)
        {
            aText.Append("| ").Append(vRow.Ts)
                .Append(" | ").Append(Dash(vRow.Cmd))
                .Append(" | ").Append(Dash(vRow.Tier))
                .Append(" | ").Append(Dash(vRow.TierModel))
                .Append(" | ").Append(Dash(vRow.Model))
                .Append(" | ").Append(Dash(vRow.Models))
                .Append(" | ").Append(vRow.Routed is null ? "—" : vRow.Routed.Value ? "yes" : "**no**")
                .AppendLine(" |");
        }

        aText.AppendLine();
    }

    /// <summary>Renders tokens by observed model.</summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void TokensByModel(StringBuilder aText, SnapshotInputs aInputs)
    {
        aText.AppendLine("### Tokens by observed model").AppendLine();

        if (aInputs.Routing.TokensByModel.Count == 0)
        {
            aText.AppendLine("_No run in this dataset carries an observed model._").AppendLine();
            return;
        }

        aText.AppendLine("| Model | In | Out | Cache read | Cache write | Total |");
        aText.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var vModel in aInputs.Routing.TokensByModel)
        {
            aText.Append("| `").Append(vModel.Model)
                .Append("` | ").Append(vModel.TokensIn)
                .Append(" | ").Append(vModel.TokensOut)
                .Append(" | ").Append(vModel.TokensCacheRead)
                .Append(" | ").Append(vModel.TokensCacheWrite)
                .Append(" | ").Append(vModel.Total).AppendLine(" |");
        }

        aText.AppendLine();
    }

    /// <summary>
    /// Renders the counterfactual repricing, with the estimate wording attached to each number.
    /// </summary>
    /// <remarks>
    /// BRD-59 requires the label everywhere the figure appears — so it is on the heading, in the column
    /// header of the table, and again in the sentence beneath. A reader who copies one row out of this
    /// document carries the word "estimate" with it.
    /// </remarks>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Repricing(StringBuilder aText, SnapshotInputs aInputs)
    {
        var vRouting = aInputs.Routing;

        aText.AppendLine("### Counterfactual repricing — ESTIMATE").AppendLine();
        aText.Append("> **").Append(RateCard.EstimateLabel).AppendLine("**").AppendLine();

        aText.AppendLine("| Figure | Value (" + RateCard.EstimateLabel + ") |");
        aText.AppendLine("|---|---:|");
        aText.Append("| Actual observed model mix | ").Append(Money(vRouting.ActualMixUsd)).AppendLine(" |");
        aText.Append("| All runs at `").Append(Dash(vRouting.MostExpensiveModel)).Append("` | ")
            .Append(Money(vRouting.AllAtMaxUsd)).AppendLine(" |");
        aText.Append("| Difference | ").Append(Money(vRouting.DeltaUsd)).AppendLine(" |");
        aText.AppendLine();

        aText.Append("Rate card: `").Append(aInputs.RateCardPath).Append("` (").Append(RateCard.Units)
            .AppendLine("). Every figure in this table is an **estimate — tokens × rate card, not "
                + "measured spend**. Nobody was billed these amounts (SCHEMA.md §4, ADR-009).")
            .AppendLine();

        aText.Append("**").Append(vRouting.RunsExcludedNoTokenScope)
            .AppendLine(" runs excluded** from the repricing because `tokens_scope` is `none` or the run "
                + "carries no token fields — tokens are never estimated (BRD-60).")
            .AppendLine();

        if (vRouting.MissingPriceModels.Count > 0)
        {
            aText.Append("⚠ **Observed models with no rate-card entry:** ")
                .Append(string.Join(", ", vRouting.MissingPriceModels.Select(aM => "`" + aM + "`")))
                .AppendLine(". Their tokens are excluded from both figures above rather than priced at "
                    + "zero. Add them to the rate card to include them.")
                .AppendLine();
        }
    }

    /// <summary>
    /// Renders the poolable metrics — the ones the reference exempts from both separations.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Poolable(StringBuilder aText, SnapshotInputs aInputs)
    {
        var vPooled = aInputs.Analysis.Pooled;

        aText.AppendLine("### Poolable metrics").AppendLine();
        aText.AppendLine(
            "These count events rather than scoring requirements, so pooling them across provenance and "
            + "project type does not manufacture a misleading rate (SCHEMA.md §6).")
            .AppendLine();

        aText.AppendLine("| Metric | Value |");
        aText.AppendLine("|---|---|");
        aText.Append("| Runs total | ").Append(vPooled.RunsTotal).AppendLine(" |");
        aText.Append("| Runs by cmd | ").Append(Counts(vPooled.RunsByCmd)).AppendLine(" |");
        aText.Append("| Rework ratio (fix ÷ build-phase) | ").Append(vPooled.ReworkRatio.Display()).AppendLine(" |");
        aText.Append("| Throughput median (REQs/hour) | ").Append(vPooled.ThroughputMedianReqsPerHour.Display())
            .AppendLine(" |");
        aText.Append("| Batch size median | ").Append(vPooled.BatchSizeMedian.Display()).AppendLine(" |");
        aText.Append("| Sessions | ").Append(vPooled.Sessions).AppendLine(" |");
        aText.Append("| Tokens total | ").Append(vPooled.TokensTotal).AppendLine(" |");
        aText.Append("| Tokens per Verified REQ | ").Append(vPooled.TokensPerVerifiedReq.Display()).AppendLine(" |");
        aText.AppendLine("| Cost (USD) | — not measured; never estimated (SCHEMA.md §4) |");
        aText.Append("| Commits | ").Append(vPooled.Commits).AppendLine(" |");
        aText.Append("| Commit duplicates collapsed | ").Append(vPooled.CommitDuplicatesCollapsed).AppendLine(" |");
        aText.Append("| Session duplicates collapsed | ").Append(vPooled.SessionDuplicatesCollapsed).AppendLine(" |");
        aText.Append("| Active days | ").Append(vPooled.ActiveDays).AppendLine(" |");
        aText.Append("| Commits per active day | ").Append(vPooled.CommitsPerActiveDay.Display()).AppendLine(" |");
        aText.AppendLine();
    }

    /// <summary>
    /// Section 5 — the parity stamp that decides whether any of the above may be quoted.
    /// </summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aInputs">Everything the snapshot renders from.</param>
    private static void Parity(StringBuilder aText, SnapshotInputs aInputs)
    {
        aText.AppendLine("## Snapshot / parity stamp").AppendLine();
        aText.AppendLine("| Field | Value |");
        aText.AppendLine("|---|---|");
        aText.Append("| Status | **").Append(aInputs.ParityStatus).AppendLine("** |");
        aText.Append("| Parser version (this export) | `").Append(aInputs.Analysis.ParserVersion).AppendLine("` |");
        aText.Append("| Parser version validated by last parity run | `")
            .Append(Dash(aInputs.Parity?.ParserVersion)).AppendLine("` |");
        aText.Append("| Last passing parity run | ").Append(Dash(aInputs.Parity?.Date)).AppendLine(" |");
        aText.Append("| Reference script | `").Append(Dash(aInputs.Parity?.ScriptPath)).AppendLine("` |");
        aText.Append("| Reference script hash | `").Append(Dash(aInputs.Parity?.ScriptHash)).AppendLine("` |");
        aText.Append("| Compare command | `").Append(Dash(aInputs.Parity?.CompareCommand)).AppendLine("` |");
        aText.AppendLine();

        aText.AppendLine("Dataset SHAs the figures were computed from:").AppendLine();
        if (aInputs.DatasetShas.Count == 0)
        {
            aText.AppendLine("_None recorded — the repositories have not been synced._").AppendLine();
        }
        else
        {
            foreach (var vSha in aInputs.DatasetShas)
            {
                aText.Append("- `").Append(vSha.Key).Append("` @ `").Append(vSha.Value).AppendLine("`");
            }

            aText.AppendLine();
        }

        aText.AppendLine(
            aInputs.ParityStatus == ParityStatuses.Quotable
                ? "A parity run against `tf-metrics.sh --rollup --json` passed with an empty diff on this "
                  + "parser version. These figures may be quoted."
                : "**These figures are not quotable.** Re-run the parity procedure (BRD §13) — export, "
                  + "run `tf-metrics.sh --rollup --json` on the same dataset SHAs, and run "
                  + "`tools/parity-compare.py` until the diff is empty.")
            .AppendLine();
    }

    /// <summary>Emits one comparison row across the harness columns.</summary>
    /// <param name="aText">The buffer.</param>
    /// <param name="aLabel">The row label.</param>
    /// <param name="aColumns">The harness columns, in ADR-017 order.</param>
    /// <param name="aCell">Renders one column's cell.</param>
    private static void Row(
        StringBuilder aText,
        string aLabel,
        IReadOnlyList<HarnessColumn> aColumns,
        Func<HarnessColumn, string> aCell)
    {
        aText.Append("| ").Append(aLabel);
        foreach (var vColumn in aColumns)
        {
            aText.Append(" | ").Append(aCell(vColumn));
        }

        aText.AppendLine(" |");
    }

    /// <summary>Renders label-count pairs inline, or an em dash when there are none.</summary>
    /// <param name="aCounts">The pairs.</param>
    /// <returns>The inline text.</returns>
    private static string Counts(IReadOnlyList<KeyValuePair<string, int>> aCounts) =>
        aCounts.Count == 0
            ? "—"
            : string.Join(" · ", aCounts.Select(aC => aC.Key + " " + aC.Value.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Renders an optional string, or an em dash when it is absent.</summary>
    /// <param name="aValue">The value.</param>
    /// <returns>The value, or <c>—</c>.</returns>
    private static string Dash(string? aValue) => string.IsNullOrWhiteSpace(aValue) ? "—" : aValue;

    /// <summary>
    /// Renders an estimated money value, or an em dash when nothing could be priced.
    /// </summary>
    /// <remarks>The word "est." travels with the number so a copied cell keeps its provenance.</remarks>
    /// <param name="aValue">The estimated amount.</param>
    /// <returns>The rendered cell.</returns>
    private static string Money(decimal? aValue) =>
        aValue is { } vValue ? "$" + vValue.ToString("F2", CultureInfo.InvariantCulture) + " (est.)" : "—";
}
