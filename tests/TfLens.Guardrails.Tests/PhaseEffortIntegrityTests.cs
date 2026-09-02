using System.Reflection;
using System.Text.RegularExpressions;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Metrics;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-023 (BRD-169) — the phase-effort and phase-efficiency invariants, with no configuration
/// switch, no query parameter and no UI toggle that relaxes any of them.
/// </summary>
/// <remarks>
/// <para>
/// Six clauses, one per test, each written for a defect that has already happened at least once:
/// </para>
/// <list type="number">
/// <item><b>No <c>0</c> where the answer is <i>not measured</i>.</b> This is <c>TF-005</c> — the defect
/// TfLens itself reported against the framework. A <c>main</c>-scope run has no <c>subagent_runs</c>
/// because the window never read the subagent transcripts: the run did not report "zero subagents", it
/// reported nothing. <c>?? 0</c> turns "we did not look" into a measurement, and the resulting average
/// is confidently composed largely of runs that could not have seen a subagent. Nothing about the number
/// looks wrong, which is the whole hazard, and the error always runs in the direction that
/// <i>flatters the framework</i>. ADR-026 answers it by making the denominator a scope predicate
/// returned beside the figure rather than a coalesce; this test stops the coalesce coming back.</item>
/// <item><b>Measured and unobserved are never pooled</b>, and every exclusion count stays beside its
/// figure. The two fan-out exclusions are two facts with different futures — <i>we did not look</i>
/// could change tomorrow, <i>we could not have looked</i> never will — so they stay two counts.</item>
/// <item><b>No per-REQ or per-feature effort view.</b> The unit of work is the run, not the ticket. A
/// <c>*build-phase</c> run touching eight REQs has one duration and one token window; dividing it eight
/// ways is arithmetic dressed as measurement. Both producers state this as a standing non-goal and
/// neither emits a per-REQ timing field, so there is nothing to divide <i>from</i>.</item>
/// <item><b>No estimated dollars on an effort tile.</b> Estimates are legitimate — <c>RateCard</c>
/// exists and <c>/routing</c> uses it — but a rate-card figure is an input, not spend, and must never
/// share a series, a total or an aggregate with measured <c>cost_usd</c>.</item>
/// <item><b>No per-subagent cost attribution.</b> The transcripts do not carry it; any such figure would
/// be invented.</item>
/// <item><b>No efficiency-scoreboard framing.</b> <c>*build-phase</c> costing more than <c>*log-miss</c>
/// is a fact about what those phases <i>are</i>, not evidence that one is inefficient. <c>/effort</c> is
/// a budgeting and capacity view; quality lives on <c>/misses</c> and <c>/coverage</c>.</item>
/// </list>
/// <para>
/// A seventh test closes the shape of the requirement itself: none of the six may be reachable through a
/// configuration key, a query parameter or a toggle, because an invariant with an off switch is a
/// default, not an invariant (the same rule BRD-89 fixes for provenance and BRD-130 for misses).
/// </para>
/// <para>
/// <b>Precision over reach.</b> Every pattern below is keyed to a named field, a named denominator or a
/// named surface rather than to a general shape — this is not a ban on <c>?? 0</c>, which is correct and
/// common on counts that really are zero. Where a legitimate exception could exist, it is declared in
/// the guarded source itself as a trailing <c>REQ-NFR-023 measured: why</c> comment, so every waiver is
/// one grep away and costs a sentence of justification. A guardrail that fires on honest code gets
/// deleted, and a deleted guardrail protects nothing.
/// </para>
/// <para>
/// These are static checks over the working tree, as every guardrail in this project is: "no surface
/// renders a zero it did not measure" is a claim about every code path, including the ones no test
/// reaches, and a negative is only provable against the source.
/// </para>
/// </remarks>
public sealed class PhaseEffortIntegrityTests
{
    /// <summary>The documented escape hatch, so every deliberate exception is one grep away.</summary>
    private const string Waiver = "REQ-NFR-023 measured:";

    /// <summary>
    /// The three §2.6 nullable fields and the denominators every phase figure is returned with.
    /// </summary>
    /// <remarks>
    /// Absent on these means <i>not measured</i>, never <i>zero</i>. They are named one by one rather
    /// than matched by shape so the check cannot drift into a general ban on null coalescing.
    /// </remarks>
    private const string NotMeasuredFields =
        "SubagentRuns|TokensOutSubagents|ModelTokensOut|MeasuredN|UnmeasuredN|ObservedN"
        + "|UnobservedNotTree|UnobservedPredatesField|TokensMeasuredN|ActiveCoverage|FanoutObserved\\w*";

    /// <summary>A coalesce or default-read that would turn "not measured" into a measured zero.</summary>
    private static readonly Regex CoercedToZero = new(
        @"\b(?:" + NotMeasuredFields + @")\b\s*(?:\?\?\s*0|\.\s*GetValueOrDefault\s*\()",
        RegexOptions.Compiled);

    /// <summary>The same coercion expressed in SQL.</summary>
    private static readonly Regex CoercedToZeroInSql = new(
        @"(?:COALESCE|IFNULL|NULLIF)\s*\(\s*""?(?:SubagentRuns|subagent_runs|TokensOutSubagents"
        + @"|tokens_out_subagents|ModelTokensOut|model_tokens_out)""?\s*,\s*0",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Arithmetic that adds an unobserved or unmeasured count into a measured one.</summary>
    /// <remarks>
    /// The negative lookahead is what keeps <c>UnmeasuredN</c> from satisfying the "measured" side of
    /// its own pattern; PascalCase does the rest, since <c>vTokensUnmeasured</c> carries a lower-case
    /// <c>m</c> that <c>Measured</c> cannot match.
    /// </remarks>
    private static readonly Regex PooledMeasuredAndUnobserved = new(
        @"\b(?!Un[mo])\w*(?:Measured|Observed)\w*\s*\+\s*\w*(?:Unmeasured|Unobserved)\w*\b"
        + @"|\b\w*(?:Unmeasured|Unobserved)\w*\s*\+\s*(?!Un[mo])\w*(?:Measured|Observed)\w*\b",
        RegexOptions.Compiled);

    /// <summary>A member whose name can only be a per-REQ or per-feature effort figure.</summary>
    private static readonly Regex PerReqEffortMember = new(
        @"\b\w*(?:Effort|Duration|Elapsed|Tokens|Cost|Usd|Spend|Minutes|Seconds|Wall)(?:By|Per)"
        + @"(?:Req|Requirement|Feature)\w*\b"
        + @"|\b(?:By|Per)(?:Req|Requirement|Feature)(?:Id)?(?:Effort|Duration|Elapsed|Tokens|Cost|Usd"
        + @"|Spend|Minutes|Seconds|Wall)\w*\b"
        + @"|\bReq(?:Id)?(?:Effort|Duration|Elapsed|Timing|Timings|Tokens|Cost|Usd|Spend|Minutes"
        + @"|Seconds|Wall|Window)\b",
        RegexOptions.Compiled);

    /// <summary>An aggregation keyed on the REQ id.</summary>
    private static readonly Regex ReqKeyedAggregation = new(
        @"\.(?:GroupBy|ToLookup|ToDictionary|CountBy|AggregateBy)\s*\(\s*[^()\n]{0,160}"
        + @"\b\w*Req(?:Id)?\b",
        RegexOptions.Compiled);

    /// <summary>An effort, duration, token or cost concept.</summary>
    private static readonly Regex EffortConcept = new(
        @"\b\w*(?:Effort|Duration|Elapsed|DurationMs|Tokens|CostUsd|Spend|Minutes|Seconds|Wall"
        + @"|Fanout|Subagent)\w*\b",
        RegexOptions.Compiled);

    /// <summary>A per-REQ or per-feature route segment, or a query parameter of the same shape.</summary>
    private static readonly Regex PerReqRouteOrQuery = new(
        @"@page\s+""[^""]*\{[^}]*(?:[Rr]eq|[Ff]eature)"
        + @"|[?&](?:req|reqId|reqid|req_id|feature|featureId)="
        + @"|\bName\s*=\s*""(?:req|reqId|reqid|req_id|feature|featureId)""",
        RegexOptions.Compiled);

    /// <summary>A dollar figure <i>attributed</i> to a subagent.</summary>
    /// <remarks>
    /// <para>
    /// Deliberately narrower than "a subagent and a dollar on the same line", and the difference is the
    /// whole clause. <c>PlaybookAgentSplit.SubagentCostUsd</c> is a <b>measured</b> total: the Playbook's
    /// turn events carry their own <c>cost</c> field where the harness measures it (OpenCode does), the
    /// sub-agent side of the split is the sum of those, and it is <c>null</c> — never <c>0</c> — when no
    /// event reported one. That is a fact the producer emitted, and refusing it would refuse a measured
    /// number for looking like an invented one.
    /// </para>
    /// <para>
    /// What BRD-169 forbids is the <i>attribution</i>: taking a run's cost, or a rate card, and
    /// distributing it across the subagents it spawned. The TechieFlow transcripts carry subagent token
    /// counts (<c>tokens_out_subagents</c>) and no subagent price, so every such figure would be a
    /// division TfLens invented. The pattern therefore looks for the attribution verbs — attributed,
    /// apportioned, estimated, priced, shared, per-subagent — and for a subagent field meeting the rate
    /// card on the same line, which is the exact shape the invention takes.
    /// </para>
    /// </remarks>
    private static readonly Regex PerSubagentCost = new(
        @"\b\w*(?:Cost|Usd|Spend|Dollars|Price|Priced|Billing)\w*(?:By|Per)Subagents?\w*\b"
        + @"|\b\w*Subagents?\w*(?:CostAttribution|CostAttributed|CostApportioned|CostEstimate"
        + @"|CostShare|CostSplit|UsdEstimate|SpendEstimate|SpendShare|Priced|Pricing|Billing"
        + @"|RateCard)\w*\b"
        + @"|\b(?:Attributed|Apportioned|Estimated)\w*Subagents?\w*(?:Cost|Usd|Spend|Dollars)\w*\b"
        + @"|\b(?:EstimateUsd|RateCard)\w*\b(?=[^\n]*Subagent)"
        + @"|\b\w*Subagents?\w*\b(?=[^\n]*\b(?:EstimateUsd|RateCard)\b)",
        RegexOptions.Compiled);

    /// <summary>An estimate added into a measured dollar figure.</summary>
    private static readonly Regex EstimatePooledWithMeasured = new(
        @"\b\w*Estimate\w*\s*\+\s*\w*(?:Measured|CostUsd)\w*\b"
        + @"|\b\w*(?:Measured|CostUsd)\w*\s*\+\s*\w*Estimate\w*\b",
        RegexOptions.Compiled);

    /// <summary>The rate card reaching a surface it must not price.</summary>
    private static readonly Regex RateCardUse = new(
        @"\bRateCard\b|\bEstimateUsd\b|_usd_estimate", RegexOptions.Compiled);

    /// <summary>A switch, key or parameter that would relax one of the six clauses.</summary>
    private static readonly Regex RelaxationSwitch = new(
        @"\b(?:Allow|Enable|Disable|Skip|Relax|Ignore|Force|Suppress|Override|Bypass)"
        + @"(?:Pooling|Pooled|Pool|Estimates|Estimate|Coalesce|ZeroFill|Unobserved|Unmeasured|FanOut"
        + @"|Fanout|PerReq|PerActor|ActorGrouping|Exclusions|Exclusion|Denominator|Denominators"
        + @"|Blended|Blend|EffortIntegrity)\w*\b"
        + @"|\b(?:Pooling|Estimates|Estimate|Coalesce|ZeroFill|Unobserved|Unmeasured|FanOut|Fanout"
        + @"|PerReq|PerActor|ActorGrouping|Exclusions|Exclusion|Denominator|Blended)"
        + @"(?:Allowed|Enabled|Disabled|Skipped|Relaxed|Ignored|Suppressed|Overridden)\b",
        RegexOptions.Compiled);

    /// <summary>Scoreboard wording that is a finding wherever it is applied.</summary>
    private static readonly Regex ScoreboardWording = new(
        @"\b(?:inefficien\w*|wasteful|efficiency\s+(?:score|scores|rating|ranking|scoreboard))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Comparison wording that is a finding only when it is applied to phases.</summary>
    private static readonly Regex ComparisonWording = new(
        @"\b(?:rank|ranks|ranked|ranking|rankings|leaderboard|scoreboard|worst|best|slowest"
        + @"|cheapest|priciest|optimised|optimized)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The cue that turns scoreboard wording into the standing copy BRD-169 asks for.</summary>
    /// <remarks>
    /// BRD-169 does not merely forbid the framing — it requires the page to <i>say</i> it is not a
    /// quality scoreboard. "Read these as a description of what ran, not as a ranking" is the rule being
    /// obeyed, and a check that failed it would forbid the one sentence the requirement mandates.
    /// </remarks>
    private static readonly Regex NegationCue = new(
        @"\b(?:not|never|nor|neither|no|none|without|rather\s+than|instead\s+of|isn't|aren't|doesn't"
        + @"|don't|cannot|can't|refuses?|forbids?|avoids?|unlike)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The word that scopes comparison wording to the phase axis.</summary>
    private static readonly Regex PhaseWord = new(@"\bphases?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A Razor comment, which is never rendered and so is never page copy.</summary>
    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>A markup tag, stripped so a sentence reads as the reader sees it.</summary>
    private static readonly Regex MarkupTag = new(@"<[^>\n]*>", RegexOptions.Compiled);

    /// <summary>A line comment, which is not page copy either.</summary>
    private static readonly Regex LineComment = new(@"//[^\n]*", RegexOptions.Compiled);

    /// <summary>
    /// Clause 1 — nothing coalesces an unmeasured §2.6 field or a denominator to zero.
    /// </summary>
    /// <remarks>
    /// This is <c>TF-005</c> arriving on a third stream. It is deliberately keyed to the eleven names
    /// where absent means <i>not measured</i>, so ordinary <c>?? 0</c> on a count that really is zero
    /// stays legal. ADR-026 explains why the fix is a predicate rather than a default.
    /// </remarks>
    [Fact]
    public void NotMeasuredIsNeverCoercedToZero()
    {
        var vFindings = ScanCode(
            CoercedToZero,
            "absent on this field means the window never looked, not that the value was zero. Coercing "
            + "it to 0 turns \"we did not look\" into a measurement, and the error always runs in the "
            + "direction that flatters the framework (TF-005, ADR-026). Return the figure wrapped in its "
            + "denominator instead.");

        vFindings.AddRange(ScanSql(
            CoercedToZeroInSql,
            "COALESCE on a §2.6 field manufactures a measurement the producer never emitted (TF-005, "
            + "ADR-026); exclude the row from the denominator instead."));

        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// Clause 2 — measured and unobserved are never added together, and each figure keeps its counts.
    /// </summary>
    /// <remarks>
    /// The arithmetic half is the one that can be checked today. The shape half asserts that when the
    /// ADR-026 result types exist they carry their denominators as properties, so a page cannot bind a
    /// spawn count or a token total without also holding the number of runs it rests on — the same
    /// "make the wrong number unrepresentable" technique as <c>Figure</c> and <c>MissCost</c>.
    /// </remarks>
    [Fact]
    public void MeasuredAndUnobservedAreNeverPooled()
    {
        var vFindings = ScanCode(
            PooledMeasuredAndUnobserved,
            "measured and unobserved are two different facts and their sum is neither. The two fan-out "
            + "exclusions in particular stay two counts because they have different futures — \"we did "
            + "not look\" can change tomorrow, \"we could not have looked\" never will (ADR-026).");

        var vRequired = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["TokenWindow"] = ["MeasuredN", "UnmeasuredN"],
            ["FanoutObservation"] = ["ObservedN", "UnobservedNotTree", "UnobservedPredatesField"]
        };

        foreach (var vType in typeof(ITelemetryStore).Assembly.GetTypes().Where(aType => aType.IsPublic))
        {
            if (!vRequired.TryGetValue(vType.Name, out var vProperties))
            {
                continue;
            }

            vFindings.AddRange(vProperties
                .Where(aName => vType.GetProperty(aName, BindingFlags.Public | BindingFlags.Instance) is null)
                .Select(aName =>
                    $"{vType.Name} has no `{aName}` property — every phase figure is returned wrapped in "
                    + "the count it rests on, so a page cannot render it without its exclusions beside "
                    + "it (ADR-026, BRD-169)."));
        }

        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// Clause 3 — no member, route or query yields effort, duration or tokens keyed by REQ.
    /// </summary>
    /// <remarks>
    /// The aggregation half requires BOTH a REQ-shaped key and an effort concept in the same statement,
    /// because grouping gates or misses by REQ is correct and the coverage engine does it constantly.
    /// The route half is scoped to files that already deal in effort, for the same reason: a per-REQ
    /// drill-down into <i>quality</i> is not what BRD-169 forbids.
    /// </remarks>
    [Fact]
    public void NoPerReqOrPerFeatureEffortView()
    {
        const string vWhy =
            "a run's window divided across the REQs it touched is arithmetic dressed as measurement. The "
            + "unit of work is the run, not the ticket: one *build-phase run has one duration and one "
            + "token window however many REQs it moved, and neither producer emits a per-REQ timing "
            + "field to divide from (BRD-169).";

        var vFindings = ScanCode(PerReqEffortMember, vWhy);

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            var vLines = File.ReadAllLines(vPath);
            var vRelative = RepoTree.Relative(vPath);
            var vIsEffortFile = vLines.Any(aLine => EffortConcept.IsMatch(aLine));

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (IsComment(vLine) || vLine.Contains(Waiver, StringComparison.Ordinal))
                {
                    continue;
                }

                var vMatch = ReqKeyedAggregation.Match(RepoTree.StripLiterals(vLine));

                if (vMatch.Success && AggregatesEffort(vLines, vIndex))
                {
                    vFindings.Add(Finding(vRelative, vIndex + 1, vMatch.Value.Trim(), vLine, vWhy));
                }

                var vRoute = PerReqRouteOrQuery.Match(vLine);

                if (vRoute.Success && vIsEffortFile)
                {
                    vFindings.Add(Finding(vRelative, vIndex + 1, vRoute.Value.Trim(), vLine, vWhy));
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// Clause 4 — no estimated dollars on an effort surface, and no estimate pooled with measured spend.
    /// </summary>
    /// <remarks>
    /// <c>RateCard</c> is legitimate and <c>/routing</c> and <c>/misses</c> both price from it, always
    /// carrying <c>RateCard.EstimateLabel</c> verbatim. What BRD-169 forbids is narrower and sharper: a
    /// rate-card dollar on an <i>effort</i> tile, where it would read as what the phase cost, and an
    /// estimate sharing a series or a total with measured <c>cost_usd</c>, where the label cannot travel
    /// with it.
    /// </remarks>
    [Fact]
    public void NoEstimatedDollarsOnAnEffortSurface()
    {
        const string vWhy =
            "a rate-card figure is an operator-editable input, not spend — nobody was billed it. On an "
            + "effort tile it reads as what the phase cost, and in a shared total it loses the label "
            + "that was the only thing distinguishing it from measured cost_usd (BRD-169, ADR-009).";

        var vFindings = ScanCode(EstimatePooledWithMeasured, vWhy);

        foreach (var vPath in RepoTree.Files("*.razor", "src/TfLens/Components"))
        {
            var vLines = File.ReadAllLines(vPath);
            var vRelative = RepoTree.Relative(vPath);

            if (!IsEffortSurface(vPath, vLines))
            {
                continue;
            }

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vLine = vLines[vIndex];

                if (IsComment(vLine) || vLine.Contains(Waiver, StringComparison.Ordinal))
                {
                    continue;
                }

                var vMatch = RateCardUse.Match(RepoTree.StripLiterals(vLine));

                if (vMatch.Success)
                {
                    vFindings.Add(Finding(vRelative, vIndex + 1, vMatch.Value.Trim(), vLine, vWhy));
                }
            }
        }

        // The label the product fixes for every estimate, asserted here so the wording cannot drift out
        // from under the surfaces that carry it verbatim.
        Assert.Contains("not measured spend", RateCard.EstimateLabel, StringComparison.Ordinal);
        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// Clause 5 — no dollar figure is attributed to an individual subagent.
    /// </summary>
    /// <remarks>
    /// The transcripts carry a subagent's token counts, not its price, so any per-subagent dollar would
    /// be a number the product invented — and the fan-out figures are exactly the place someone would
    /// reach for one, because the token counts are sitting right there next to a rate card.
    /// <para>
    /// The check distinguishes attribution from measurement, and the first run of this guardrail is why:
    /// it reported <c>PlaybookAgentSplit.SubagentCostUsd</c>, which is <b>measured</b> spend summed from
    /// the Playbook events' own <c>cost</c> field and <c>null</c> when none was reported. That is a
    /// producer fact, not an attribution, and banning it would have refused a measured number for
    /// resembling an invented one — so the pattern was narrowed to the attribution verbs and to a
    /// subagent field meeting the rate card.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoPerSubagentCostAttribution()
    {
        var vFindings = ScanCode(
            PerSubagentCost,
            "the transcripts do not carry a per-subagent cost, so any such figure would be invented "
            + "rather than measured (BRD-169). Subagent token counts exist; subagent dollars do not.");

        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// Clause 6 — no page copy frames one phase as inefficient for costing more than another.
    /// </summary>
    /// <remarks>
    /// The check reads the copy as the reader sees it: Razor comments, line comments and markup tags are
    /// blanked first, then each sentence is judged whole. A sentence carrying a negation cue passes,
    /// because BRD-169 does not just forbid the framing — it requires the page to say it is not a
    /// quality scoreboard, and "read this as a description of what ran, not as a ranking" is the rule
    /// being obeyed rather than broken. Comparison wording is judged only where the sentence is talking
    /// about phases; scoreboard wording is judged wherever it appears.
    /// </remarks>
    [Fact]
    public void NoEfficiencyScoreboardFramingInPageCopy()
    {
        const string vWhy =
            "*build-phase costing more than *log-miss is a fact about what those phases ARE, not "
            + "evidence that one is inefficient. Effort per phase is a budgeting and capacity view; "
            + "quality lives on /misses and /coverage (BRD-169).";

        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.razor", "src/TfLens/Components"))
        {
            var vRelative = RepoTree.Relative(vPath);
            var vCopy = RenderedCopy(File.ReadAllText(vPath));

            foreach (Match vMatch in ScoreboardWording.Matches(vCopy))
            {
                var vSentence = SentenceAround(vCopy, vMatch.Index);

                if (!NegationCue.IsMatch(vSentence))
                {
                    vFindings.Add(CopyFinding(vRelative, vCopy, vMatch, vSentence, vWhy));
                }
            }

            foreach (Match vMatch in ComparisonWording.Matches(vCopy))
            {
                var vSentence = SentenceAround(vCopy, vMatch.Index);

                if (PhaseWord.IsMatch(vSentence) && !NegationCue.IsMatch(vSentence))
                {
                    vFindings.Add(CopyFinding(vRelative, vCopy, vMatch, vSentence, vWhy));
                }
            }
        }

        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// The requirement itself — no configuration key, query parameter or toggle relaxes any clause.
    /// </summary>
    /// <remarks>
    /// This is the clause the other six rest on, and the one BRD-89 and BRD-130 both learned the hard
    /// way: an integrity rule with an off switch is a default, not an invariant, and the switch is
    /// always added for a reason that sounds good at the time.
    /// </remarks>
    [Fact]
    public void NoSwitchCanRelaxThePhaseEffortInvariants()
    {
        const string vWhy =
            "an integrity rule with an off switch is a default, not an invariant. BRD-169 allows the "
            + "phase-effort clauses no configuration key, no query parameter and no UI toggle, exactly "
            + "as BRD-89 allows the provenance rules none.";

        var vFindings = ScanCode(RelaxationSwitch, vWhy);

        foreach (var vPath in RepoTree.Files("*.json", "src"))
        {
            var vLines = File.ReadAllLines(vPath);
            var vRelative = RepoTree.Relative(vPath);

            for (var vIndex = 0; vIndex < vLines.Length; vIndex++)
            {
                var vMatch = RelaxationSwitch.Match(vLines[vIndex]);

                if (vMatch.Success && !vLines[vIndex].Contains(Waiver, StringComparison.Ordinal))
                {
                    vFindings.Add(Finding(vRelative, vIndex + 1, vMatch.Value.Trim(), vLines[vIndex], vWhy));
                }
            }
        }

        // TfLensOptions is the whole of the product's configuration surface, so an option added there
        // is the one place a relaxation could enter without looking like one.
        vFindings.AddRange(typeof(TfLensOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(aProperty => RelaxationSwitch.IsMatch(aProperty.Name))
            .Select(aProperty => $"TfLensOptions.{aProperty.Name} — {vWhy}"));

        Assert.True(vFindings.Count == 0, Report(vFindings));
    }

    /// <summary>
    /// The checks refuse the defects and permit the honest shapes, demonstrated on both.
    /// </summary>
    /// <remarks>
    /// The six clauses above pass on today's tree, which on its own proves only that nothing matched.
    /// This test proves the patterns can still tell the defect from the correct code beside it: an
    /// ordinary <c>?? 0</c> on a count that really is zero stays legal, grouping gates by REQ stays
    /// legal, <c>/routing</c>'s rate-card estimate stays legal, and the standing "not a ranking" copy
    /// stays legal — while the TF-005 coalesce, the per-REQ effort member, the per-subagent dollar and
    /// the efficiency verdict all fail. Narrowing any of these guardrails into uselessness fails here
    /// first.
    /// </remarks>
    [Fact]
    public void TheChecksRefuseTheDefectAndPermitTheHonestShape()
    {
        AssertAllowed(CoercedToZero, "        var vFixes = vRow.TokensIn ?? 0;");
        AssertAllowed(CoercedToZero, "            Records = vFact?.Records ?? 0,");
        AssertRefused(CoercedToZero, "        var vSpawns = aRun.SubagentRuns ?? 0;");
        AssertRefused(CoercedToZero, "        var vOut = aRun.ModelTokensOut.GetValueOrDefault();");
        AssertRefused(CoercedToZero, "            ObservedN = vWindow.ObservedN ?? 0,");

        // SQL is scanned raw, because every column in this schema is double-quoted and a
        // literal-stripping scan would blank the column name it is meant to judge.
        Assert.NotEmpty(ScanLines(
            CoercedToZeroInSql,
            "sample.sql",
            ["SELECT COALESCE(\"SubagentRuns\", 0) FROM \"Run\";"],
            "why",
            aStripLiterals: false));
        Assert.Empty(ScanLines(
            CoercedToZeroInSql,
            "sample.sql",
            ["SELECT COALESCE(\"GatesRun\", 0) FROM \"Run\";"],
            "why",
            aStripLiterals: false));

        AssertAllowed(PooledMeasuredAndUnobserved, "        var vAll = vMeasuredN + vRetriedN;");
        AssertRefused(PooledMeasuredAndUnobserved, "        var vAll = vMeasuredN + vUnmeasuredN;");

        AssertAllowed(PerReqEffortMember, "        var vGatesByReq = aGates.GroupBy(aGate => aGate.ReqId);");
        AssertRefused(PerReqEffortMember, "    public IReadOnlyList<Figure> EffortPerReq { get; init; } = [];");
        AssertRefused(PerReqEffortMember, "    private Dictionary<string, TimeSpan> objDurationByReq = new();");

        AssertAllowed(PerSubagentCost, "        var vSpawned = aRun.SubagentRuns;");
        AssertAllowed(PerSubagentCost, "    decimal? SubagentCostUsd,");
        AssertRefused(PerSubagentCost, "    public decimal CostPerSubagent { get; init; }");
        AssertRefused(PerSubagentCost, "    public decimal SubagentCostEstimateUsd { get; init; }");
        AssertRefused(
            PerSubagentCost,
            "        var vCost = objRateCard.EstimateUsd(aRun.TokensOutSubagents, 0, 0, 0);");

        AssertAllowed(EstimatePooledWithMeasured, "        vTotal = vEstimateUsd + vOtherEstimateUsd;");
        AssertRefused(EstimatePooledWithMeasured, "        vTotal = vEstimateUsd + vMeasuredUsdTotal;");

        AssertAllowed(RelaxationSwitch, "    public int PollIntervalMinutes { get; set; } = 15;");
        AssertRefused(RelaxationSwitch, "    public bool AllowPooledUnobserved { get; set; }");
        AssertRefused(RelaxationSwitch, "    public bool PoolingEnabled { get; set; }");

        // The escape hatch works, and only where it is declared.
        Assert.Empty(ScanLines(
            CoercedToZero,
            "sample.cs",
            ["        var vSpawns = aRun.SubagentRuns ?? 0; // REQ-NFR-023 measured: the caller has "
             + "already filtered to tree-scope runs, where absent genuinely means zero",
            ],
            "why"));

        // The copy check reads a sentence, not a keyword: the standing BRD-169 disclaimer must pass and
        // the verdict it disclaims must not.
        Assert.False(FlagsCopy("Read these as a description of what ran, not as a ranking of phases."));
        Assert.False(FlagsCopy("A phase costing more than another is not evidence that it is inefficient."));
        Assert.True(FlagsCopy("The worst phase by tokens is *build-phase, which is the most inefficient."));
        Assert.True(FlagsCopy("Phases are ranked here by spend so the team can see the leaderboard."));
    }

    /// <summary>Runs a pattern over every C# and Razor file under <c>src</c>.</summary>
    /// <param name="aPattern">The forbidden shape.</param>
    /// <param name="aWhy">One sentence explaining why it is forbidden.</param>
    /// <returns>One finding per offending line.</returns>
    private static List<string> ScanCode(Regex aPattern, string aWhy)
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.cs", "src").Concat(RepoTree.Files("*.razor", "src")))
        {
            vFindings.AddRange(ScanLines(aPattern, RepoTree.Relative(vPath), File.ReadAllLines(vPath), aWhy));
        }

        return vFindings;
    }

    /// <summary>Runs a pattern over every SQL file in the schema directory.</summary>
    /// <remarks>
    /// Literals are NOT stripped here, and the first smoke run of this guardrail is why. PostgreSQL
    /// folds unquoted identifiers to lower case, so every column in this schema is double-quoted —
    /// which means <c>COALESCE("SubagentRuns", 0)</c> looks exactly like a string literal to a C#-shaped
    /// scan, and blanking it would erase the very column name being judged.
    /// </remarks>
    /// <param name="aPattern">The forbidden shape.</param>
    /// <param name="aWhy">One sentence explaining why it is forbidden.</param>
    /// <returns>One finding per offending line.</returns>
    private static List<string> ScanSql(Regex aPattern, string aWhy)
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.sql", "database"))
        {
            vFindings.AddRange(ScanLines(
                aPattern,
                RepoTree.Relative(vPath),
                File.ReadAllLines(vPath),
                aWhy,
                aStripLiterals: false));
        }

        return vFindings;
    }

    /// <summary>Judges one file's lines against one pattern.</summary>
    /// <param name="aPattern">The forbidden shape.</param>
    /// <param name="aRelativePath">The repository-relative path, for the message.</param>
    /// <param name="aLines">The file's lines.</param>
    /// <param name="aWhy">One sentence explaining why it is forbidden.</param>
    /// <param name="aStripLiterals">
    /// Whether string and character literals are blanked before matching. True for C# and Razor, where a
    /// forbidden shape is always code; false for SQL, where a double-quoted token is an identifier.
    /// </param>
    /// <returns>One finding per offending line.</returns>
    private static List<string> ScanLines(
        Regex aPattern,
        string aRelativePath,
        IReadOnlyList<string> aLines,
        string aWhy,
        bool aStripLiterals = true)
    {
        var vFindings = new List<string>();

        for (var vIndex = 0; vIndex < aLines.Count; vIndex++)
        {
            var vLine = aLines[vIndex];

            if (IsComment(vLine) || vLine.Contains(Waiver, StringComparison.Ordinal))
            {
                continue;
            }

            var vMatch = aPattern.Match(aStripLiterals ? RepoTree.StripLiterals(vLine) : vLine);

            if (vMatch.Success)
            {
                vFindings.Add(Finding(aRelativePath, vIndex + 1, vMatch.Value.Trim(), vLine, aWhy));
            }
        }

        return vFindings;
    }

    /// <summary>Tells whether a Razor file is one of the effort surfaces BRD-169 governs.</summary>
    /// <remarks>
    /// Named narrowly on purpose. <c>/routing</c> and <c>/misses</c> price from the rate card correctly
    /// and must stay outside this check; what is governed is the effort page and the phase-total
    /// components that feed it.
    /// </remarks>
    /// <param name="aPath">The file's absolute path.</param>
    /// <param name="aLines">The file's lines.</param>
    /// <returns><c>true</c> when the file renders phase effort.</returns>
    private static bool IsEffortSurface(string aPath, IReadOnlyList<string> aLines)
    {
        var vName = Path.GetFileName(aPath);

        return vName.Contains("Effort", StringComparison.Ordinal)
            || vName.Contains("PhaseTotals", StringComparison.Ordinal)
            || aLines.Any(aLine => aLine.TrimStart().StartsWith("@page \"/effort", StringComparison.Ordinal));
    }

    /// <summary>
    /// Tells whether an aggregation keyed on the REQ id also aggregates an effort concept.
    /// </summary>
    /// <remarks>
    /// A LINQ chain wraps across lines, so the window runs to the end of the statement or five lines,
    /// whichever comes first. Grouping gates or misses by REQ is legitimate and common; it is only the
    /// pairing with a duration, a token count or a dollar that BRD-169 forbids.
    /// </remarks>
    /// <param name="aLines">The file's lines.</param>
    /// <param name="aIndex">The index of the grouping line.</param>
    /// <returns><c>true</c> when the statement also names an effort concept.</returns>
    private static bool AggregatesEffort(IReadOnlyList<string> aLines, int aIndex)
    {
        for (var vAhead = aIndex; vAhead <= aIndex + 5 && vAhead < aLines.Count; vAhead++)
        {
            if (EffortConcept.IsMatch(aLines[vAhead]))
            {
                return true;
            }

            if (aLines[vAhead].TrimEnd().EndsWith(';'))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Reduces a Razor file to the text a reader actually sees, without moving any character.
    /// </summary>
    /// <remarks>
    /// Comments and tags are blanked rather than removed so every index still maps to its original
    /// line, which is what lets a copy finding name a line number.
    /// </remarks>
    /// <param name="aRazor">The component's source.</param>
    /// <returns>The source with everything that is not rendered copy blanked out.</returns>
    private static string RenderedCopy(string aRazor) =>
        Blank(Blank(Blank(aRazor, RazorComment), LineComment), MarkupTag);

    /// <summary>Replaces every match with spaces, keeping newlines so line numbers survive.</summary>
    /// <param name="aText">The text to blank within.</param>
    /// <param name="aPattern">What to blank.</param>
    /// <returns>The text with matches blanked.</returns>
    private static string Blank(string aText, Regex aPattern) =>
        aPattern.Replace(
            aText,
            aMatch => new string(aMatch.Value.Select(aChar => aChar == '\n' ? '\n' : ' ').ToArray()));

    /// <summary>
    /// Lifts the sentence a match sits in, so the check judges a claim rather than a keyword.
    /// </summary>
    /// <param name="aText">The rendered copy.</param>
    /// <param name="aIndex">Where the match starts.</param>
    /// <returns>The surrounding sentence, whitespace normalised.</returns>
    private static string SentenceAround(string aText, int aIndex)
    {
        var vStart = aIndex;
        var vEnd = aIndex;

        while (vStart > 0 && aIndex - vStart < 400 && !IsSentenceEnd(aText, vStart))
        {
            vStart--;
        }

        while (vEnd < aText.Length - 1 && vEnd - aIndex < 400 && !IsSentenceEnd(aText, vEnd))
        {
            vEnd++;
        }

        return Regex.Replace(aText[vStart..Math.Min(vEnd + 1, aText.Length)], @"\s+", " ").Trim();
    }

    /// <summary>Tells whether a position ends a sentence — terminal punctuation, then whitespace.</summary>
    /// <param name="aText">The rendered copy.</param>
    /// <param name="aIndex">The position to test.</param>
    /// <returns><c>true</c> when the sentence breaks here.</returns>
    private static bool IsSentenceEnd(string aText, int aIndex) =>
        aText[aIndex] is '.' or '!' or '?' or ';'
        && aIndex + 1 < aText.Length
        && char.IsWhiteSpace(aText[aIndex + 1]);

    /// <summary>Tells whether a sentence of page copy would be reported by the copy check.</summary>
    /// <param name="aSentence">One sentence, as the reader would read it.</param>
    /// <returns><c>true</c> when the copy check reports it.</returns>
    private static bool FlagsCopy(string aSentence)
    {
        if (ScoreboardWording.IsMatch(aSentence) && !NegationCue.IsMatch(aSentence))
        {
            return true;
        }

        return ComparisonWording.IsMatch(aSentence)
            && PhaseWord.IsMatch(aSentence)
            && !NegationCue.IsMatch(aSentence);
    }

    /// <summary>Asserts a pattern leaves an honest line alone.</summary>
    /// <param name="aPattern">The pattern under test.</param>
    /// <param name="aLine">A line the product may legitimately contain.</param>
    private static void AssertAllowed(Regex aPattern, string aLine) =>
        Assert.True(
            ScanLines(aPattern, "sample.cs", [aLine], "why").Count == 0,
            "REQ-NFR-023 must leave honest code alone, and this check refused it — a guardrail that "
            + $"fires on correct code is one that gets deleted: {aLine}");

    /// <summary>Asserts a pattern catches the defect it was written for.</summary>
    /// <param name="aPattern">The pattern under test.</param>
    /// <param name="aLine">A line BRD-169 forbids.</param>
    private static void AssertRefused(Regex aPattern, string aLine) =>
        Assert.True(
            ScanLines(aPattern, "sample.cs", [aLine], "why").Count > 0,
            $"REQ-NFR-023 must refuse this and the check let it through: {aLine}");

    /// <summary>Renders one copy finding with its line, the sentence and the reason.</summary>
    /// <param name="aRelativePath">The repository-relative path.</param>
    /// <param name="aCopy">The rendered copy.</param>
    /// <param name="aMatch">The wording that matched.</param>
    /// <param name="aSentence">The sentence it sits in.</param>
    /// <param name="aWhy">One sentence explaining why it is forbidden.</param>
    /// <returns>The finding.</returns>
    private static string CopyFinding(
        string aRelativePath,
        string aCopy,
        Match aMatch,
        string aSentence,
        string aWhy) =>
        $"{aRelativePath}:{LineOf(aCopy, aMatch.Index)} — page copy reads \"{aSentence}\" and matched "
        + $"`{aMatch.Value}`. {aWhy}";

    /// <summary>Turns a character offset into a 1-based line number.</summary>
    /// <param name="aText">The text the offset is into.</param>
    /// <param name="aIndex">The offset.</param>
    /// <returns>The line number.</returns>
    private static int LineOf(string aText, int aIndex) => aText[..aIndex].Count(aChar => aChar == '\n') + 1;

    /// <summary>Renders one finding with its location, the text that matched and the reason.</summary>
    /// <param name="aRelativePath">The repository-relative path.</param>
    /// <param name="aLine">The 1-based line number.</param>
    /// <param name="aMatched">The text the pattern matched.</param>
    /// <param name="aSource">The whole source line.</param>
    /// <param name="aWhy">One sentence explaining why it is forbidden.</param>
    /// <returns>The finding.</returns>
    private static string Finding(
        string aRelativePath,
        int aLine,
        string aMatched,
        string aSource,
        string aWhy) =>
        $"{aRelativePath}:{aLine} — matched `{aMatched}` in `{aSource.Trim()}`. {aWhy} If this line is "
        + $"genuinely honest, say why on the line: `// {Waiver} why`.";

    /// <summary>Tells whether a source line is a comment and so cannot violate anything.</summary>
    /// <param name="aLine">One source line.</param>
    /// <returns><c>true</c> when the line is a comment.</returns>
    private static bool IsComment(string aLine)
    {
        var vTrimmed = aLine.TrimStart();

        return vTrimmed.StartsWith("//", StringComparison.Ordinal)
            || vTrimmed.StartsWith("*", StringComparison.Ordinal)
            || vTrimmed.StartsWith("@*", StringComparison.Ordinal)
            || vTrimmed.StartsWith("--", StringComparison.Ordinal);
    }

    /// <summary>Renders a finding list into a failure message a reader can act on.</summary>
    /// <param name="aFindings">Every violation found.</param>
    /// <returns>The message.</returns>
    private static string Report(IReadOnlyList<string> aFindings) =>
        $"REQ-NFR-023 (BRD-169) — {aFindings.Count} phase-effort integrity violation(s):"
        + Environment.NewLine
        + string.Join(Environment.NewLine, aFindings);
}
