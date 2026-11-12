using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Export;
using TfLens.Core.Metrics;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.Export;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-147..REQ-FN-151 — the estimate stream, its reconciliation against the oracle, the fourth
/// separation, the two no-oracle figures and the export layout, all over the Sample app fixtures.
/// </summary>
/// <remarks>
/// <para>
/// The Sample app (BRD §3.1) is made up and exists only as the checked-in files under
/// <c>Fixtures/Estimates/</c>. <c>reference-estimates.json</c> and <c>reference-rollup.json</c> are
/// produced by running the oracle itself (<c>Fixtures/Estimates/make-reference.sh</c> →
/// <c>tf-metrics.sh --estimates / --rollup … --json</c>) over those same files. Nothing in them is
/// hand-written, and the oracle is never edited to match the app.
/// </para>
/// <para>
/// The Total row and the per-row actuals are in NEITHER reference — <c>tf-metrics.sh</c> does not
/// compute them (BRD-217, ADR-035). Their expected values below were hand-checked once against the
/// raw JSONL; the arithmetic is recorded in DECISIONS.md §8 (X-003).
/// </para>
/// </remarks>
public sealed class EstimateReconciliationTests
{
    private const int FixtureUserId = 7;
    private const string FixtureFramework = "techieflow";
    private const string SampleAppRepo = "sample/sample-app";
    private const string SampleEmptyRepo = "sample/sample-empty";

    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Estimates");

    // ---- REQ-FN-147: the stream is fetched, recognised, parsed and deduped like every other ----

    /// <summary>
    /// The Sample app estimates file parses into exactly 44 typed rows, the Sample empty file into
    /// none, and a missing file is an empty stream rather than an error (REQ-FN-147, BRD-211).
    /// </summary>
    [Fact(DisplayName = "REQ-FN-147 — the Sample app estimates parse into 44 typed rows and Sample empty into none")]
    public void SampleEstimatesParseIntoFortyFourTypedRows()
    {
        var vApp = ParseEstimates(SampleAppRepo, "sample__sample-app");
        var vEmpty = ParseEstimates(SampleEmptyRepo, "sample__sample-empty");
        var vMissing = ParseEstimates(SampleEmptyRepo, "no-such-directory");

        Assert.Equal(44, vApp.Count);
        Assert.Empty(vEmpty);
        Assert.Empty(vMissing);

        // Every documented field lands in a typed column (BRD-211), spot-checked on the first row
        // record and the first phase total of the fixture.
        var vRow = vApp[0];
        Assert.Equal("2026-10-01T09:00:00Z", vRow.Ts);
        Assert.Equal("SampleApp", vRow.App);
        Assert.Equal(1, vRow.Phase);
        Assert.Equal("REQ-FN-001", vRow.ReqId);
        Assert.Equal("build-phase", vRow.Cmd);
        Assert.Equal(10_000, vRow.TokensOutLow);
        Assert.Equal(15_000, vRow.TokensOutMid);
        Assert.Equal(20_000, vRow.TokensOutHigh);
        Assert.Equal(500, vRow.DurationSLow);
        Assert.Equal(800, vRow.DurationSMid);
        Assert.Equal(1_100, vRow.DurationSHigh);
        Assert.Equal("repo", vRow.Basis);
        Assert.Equal(6, vRow.BasisRuns);
        Assert.Equal("claude-opus-5-5", vRow.Model);
        Assert.Equal("claude-code", vRow.Harness);
        Assert.Null(vRow.BasisNote);

        var vTotal = vApp.First(aRecord => aRecord.ReqId is null);
        Assert.Equal(3, vTotal.RowsN);
        Assert.NotNull(vTotal.TokensOutMid);

        // The after-handoff rows are the fixture's "no precedent" case: basis none, six null legs,
        // never six zeros (SCHEMA.md §2.5).
        var vTriage = vApp.First(aRecord => aRecord.Cmd == "triage" && aRecord.ReqId is not null);
        Assert.Equal("none", vTriage.Basis);
        Assert.Null(vTriage.TokensOutLow);
        Assert.Null(vTriage.TokensOutMid);
        Assert.Null(vTriage.TokensOutHigh);
        Assert.Null(vTriage.DurationSLow);
        Assert.Null(vTriage.DurationSMid);
        Assert.Null(vTriage.DurationSHigh);
    }

    /// <summary>
    /// Re-importing the same estimates file adds nothing: the dedupe key collapses every record of a
    /// re-parse, first wins (REQ-FN-147, BRD-211).
    /// </summary>
    [Fact(DisplayName = "REQ-FN-147 — re-importing the same estimate stream adds nothing")]
    public void ReimportingTheSameEstimateStreamAddsNothing()
    {
        var vOnce = ParseEstimates(SampleAppRepo, "sample__sample-app");
        var vTwice = vOnce.Concat(vOnce).ToList();

        var vDeduped = Dedupe.Estimates(vTwice);

        Assert.Equal(44, vDeduped.Records.Count);
        Assert.Equal(44, vDeduped.Collapsed);
        Assert.Equal(vOnce, vDeduped.Records);
    }

    // ---- REQ-FN-148 / REQ-FN-151: the reconciliation and the export equal the oracle ----

    /// <summary>
    /// The engine's reconciliation over the Sample app fixtures equals <c>tf-metrics.sh --estimates
    /// --json</c> key for key, as exported in <c>tflens.json</c>'s <c>estimates</c> block
    /// (REQ-FN-148, BRD-212).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-148 — the reconciliation equals tf-metrics.sh --estimates --json key for key")]
    public async Task ReconciliationEqualsTheOracleEstimatesKeyForKey()
    {
        var vExported = await ExportSampleSnapshotAsync();
        var vReference = LoadReference("reference-estimates.json");

        var vDifferences = new List<string>();
        JsonDeepEquals(
            JsonNode.Parse(vReference.GetRawText()),
            vExported["estimates"],
            "estimates",
            vDifferences);

        Assert.True(
            vDifferences.Count == 0,
            "TfLens diverged from tf-metrics.sh --estimates:" + Environment.NewLine
            + string.Join(Environment.NewLine, vDifferences));
    }

    /// <summary>
    /// The snapshot export carries the <c>estimates</c> block in the exact key layout of
    /// <c>tf-metrics.sh --rollup --json</c>, with the two no-oracle figures BESIDE it under the
    /// <c>estimates_extras</c> key that <c>tools/parity-compare.py</c> lists in ADDED_KEYS
    /// (REQ-FN-151, BRD-218).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-151 — tflens.json carries the estimates block in the rollup oracle's key layout")]
    public async Task ExportCarriesTheEstimatesBlockInTheOracleLayout()
    {
        var vExported = await ExportSampleSnapshotAsync();
        var vRollup = LoadReference("reference-rollup.json");

        var vDifferences = new List<string>();
        JsonDeepEquals(
            JsonNode.Parse(vRollup.GetProperty("estimates").GetRawText()),
            vExported["estimates"],
            "estimates",
            vDifferences);

        Assert.True(
            vDifferences.Count == 0,
            "the exported estimates block diverged from the rollup oracle:" + Environment.NewLine
            + string.Join(Environment.NewLine, vDifferences));

        // The extras block sits beside the parity surface, never inside it, and labels itself.
        var vExtras = vExported["estimates_extras"]?.AsObject();
        Assert.NotNull(vExtras);
        Assert.Contains("no parity oracle", vExtras!["note"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, vExtras["phases"]!.AsArray().Count);
        Assert.All(
            vExtras["phases"]!.AsArray(),
            aCard => Assert.True(aCard!["no_oracle"]!.GetValue<bool>()));
    }

    /// <summary>
    /// <c>tools/parity-compare.py</c> — the project's own parity check, run for real — diffs the oracle's
    /// <c>--rollup --json</c> over the Sample app files against the exported <c>tflens.json</c>: the
    /// <c>estimates</c> block is covered in full and has no finding (REQ-FN-151, BRD-218).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The C# key walk above proves the block alone; this runs the script the owner runs, allow-lists
    /// and coverage checks included. The repository key differs by construction (<c>sample-app</c> is the
    /// oracle's scratch directory, <c>sample/sample-app</c> TfLens's owner/name), so the script's
    /// documented <c>--allow-environment-keys</c> is passed; it never touches a figure.
    /// </para>
    /// <para>
    /// This judges the estimates block alone, as the owner decided on 2026-11-12 (DECISIONS.md D-013);
    /// the whole document's exit code is REQ-FN-152's test below.
    /// </para>
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-151 — parity-compare.py covers every estimates figure and finds nothing in the block")]
    public async Task ParityCompareFindsNothingInEstimates()
    {
        var (vExitCode, vReport) = await RunParityCompareAsync();
        var vLines = vReport.Split('\n');

        // The script ran to its verdict (0 PASS, 1 findings) rather than crashing on its input.
        Assert.True(vExitCode is 0 or 1, $"parity-compare.py could not run ({vExitCode}):{Environment.NewLine}{vReport}");

        // Every estimates figure BRD-218 names was on both documents and compared…
        Assert.Contains(vLines, aLine => aLine.Contains("COVERED", StringComparison.Ordinal)
                                         && aLine.TrimEnd().EndsWith("estimates", StringComparison.Ordinal));

        // …and not one finding sits in the estimates block or its extras.
        var vEstimateFindings = vLines
            .Where(aLine => aLine.TrimStart().StartsWith("FAIL", StringComparison.Ordinal)
                            && aLine.Contains("estimates", StringComparison.Ordinal))
            .ToList();
        Assert.True(vEstimateFindings.Count == 0, string.Join(Environment.NewLine, vEstimateFindings));
    }

    /// <summary>
    /// The whole parity check passes: parity-compare.py over the oracle's <c>--rollup --json</c> for the
    /// Sample app files and the exported <c>tflens.json</c> exits 0 — the discovery cost keys and the
    /// empty repository's app name included (REQ-FN-152, BRD-220 acceptance).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-152 — parity-compare.py over the Sample app rollup and tflens.json exits 0")]
    public async Task WholeParityCheckPassesOverTheSampleApp()
    {
        var (vExitCode, vReport) = await RunParityCompareAsync();

        Assert.True(vExitCode == 0, $"parity-compare.py exited {vExitCode}:{Environment.NewLine}{vReport}");
    }

    // ---- REQ-FN-150: the two figures with no oracle, hand-checked against the raw JSONL ----

    /// <summary>
    /// The three Totals read 55.5k, 83.5k and 58.0k beside 76.0k, 76.0k and 78.0k — the acceptance
    /// line of REQ-FN-150, hand-checked against the raw JSONL and recorded in DECISIONS.md X-003.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-150 — the three Totals read 55.5k, 83.5k and 58.0k beside 76.0k, 76.0k and 78.0k")]
    public async Task TotalsCarryTheHandCheckedFigures()
    {
        var vCards = (await ReconcileAsync()).Phases;
        Assert.Equal(3, vCards.Count);

        // Phase 1 · Opus. Estimate: build rows 28.0k/42.0k/56.0k + machine-QA rows 7.5k/13.5k/19.5k.
        // Actual: build 30000 + 12000 + 20000×½ = 52000, machine QA 6000+6000+3000+4000+5000 = 24000.
        AssertTotal(
            vCards[0],
            aTokensLow: 35_500, aTokensMid: 55_500, aTokensHigh: 75_500,
            aDurationLow: 2_600, aDurationMid: 4_400, aDurationHigh: 6_200,
            aActualTokens: 76_000, aActualDuration: 6_300, aRunsN: 8, aApportionedN: 1);

        // Phase 1 · Sonnet. Same actual (one run set), a dearer forecast: 70.0k + 13.5k.
        AssertTotal(
            vCards[1],
            aTokensLow: 62_500, aTokensMid: 83_500, aTokensHigh: 104_500,
            aDurationLow: 3_800, aDurationMid: 5_500, aDurationHigh: 7_200,
            aActualTokens: 76_000, aActualDuration: 6_300, aRunsN: 8, aApportionedN: 1);

        // Phase 2 · Opus. Actual: build 20000×½ + 40000 + 15000 = 65000, machine QA 4000+4000+2000+3000.
        AssertTotal(
            vCards[2],
            aTokensLow: 38_000, aTokensMid: 58_000, aTokensHigh: 78_000,
            aDurationLow: 3_200, aDurationMid: 4_700, aDurationHigh: 6_200,
            aActualTokens: 78_000, aActualDuration: 5_700, aRunsN: 7, aApportionedN: 1);

        // After handoff is never inside the Total: the fixture has no triage run at all, and the
        // total still exists (BRD-217).
        Assert.All(vCards, aCard => Assert.Null(aCard.Total.NotOneFigure));
    }

    /// <summary>
    /// The per-row drill-down follows the BRD-215 rule: each run counts 1 ÷ reqs_count of its tokens
    /// and time, apportioned below one, insufficient under three — the §3.1 per-row table exactly
    /// (REQ-FN-150, REQ-UI-079).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-150 — the per-row actuals follow the 1 ÷ reqs_count rule of BRD-215")]
    public async Task PerRowActualsFollowTheStatedRule()
    {
        var vCard = (await ReconcileAsync()).Phases[0]; // phase 1 · Opus
        var vRows = vCard.Rows.ToDictionary(aRow => aRow.ReqId, StringComparer.Ordinal);

        Assert.Equal(new[] { "REQ-FN-001", "REQ-FN-002", "REQ-UI-001" }, vCard.Rows.Select(aRow => aRow.ReqId));

        // REQ-FN-001 build: run 1 counts ⅓ (10000 tok, 600 s), run 2 counts whole (12000, 600).
        // Two runs — under the floor, so no number on either figure.
        var vFn1Build = vRows["REQ-FN-001"].Build;
        Assert.Equal(15_000, vFn1Build.TokensOut!.Mid);
        Assert.Equal(2, vFn1Build.Actual.RunsN);
        Assert.Equal(1, vFn1Build.Actual.ApportionedN);
        Assert.Equal("insufficient data (n=2)", vFn1Build.Actual.TokensOut.Display());
        Assert.Equal("insufficient data (n=2)", vFn1Build.Actual.DurationS.Display());

        // REQ-FN-002 machine QA: verify ⅓+⅓+whole (2000+2000+3000) + fix whole (4000) = 11000 tokens
        // over 4 runs; duration 300+300+300+600 = 1500 s (25m00s), apportioned.
        var vFn2Qa = vRows["REQ-FN-002"].MachineQa;
        Assert.Equal(4_500, vFn2Qa.TokensOut!.Mid);
        Assert.Equal(4, vFn2Qa.Actual.RunsN);
        Assert.True(vFn2Qa.Actual.IsApportioned);
        Assert.True(vFn2Qa.Actual.TokensOut.TryGetValue(out var vFn2Tokens));
        Assert.Equal(11_000, (long)Math.Round(vFn2Tokens, MidpointRounding.ToEven));
        Assert.True(vFn2Qa.Actual.DurationS.TryGetValue(out var vFn2Seconds));
        Assert.Equal(1_500, (long)Math.Round(vFn2Seconds, MidpointRounding.ToEven));

        // REQ-UI-001 machine QA: verify ⅓+⅓ (2000+2000) + fix whole (5000) = 9000 over exactly three
        // runs — at the floor, so it IS a number: 9.0k tok, 20m00s, apportioned.
        var vUi1Qa = vRows["REQ-UI-001"].MachineQa;
        Assert.Equal(3, vUi1Qa.Actual.RunsN);
        Assert.True(vUi1Qa.Actual.TokensOut.TryGetValue(out var vUi1Tokens));
        Assert.Equal(9_000, (long)Math.Round(vUi1Tokens, MidpointRounding.ToEven));
        Assert.True(vUi1Qa.Actual.DurationS.TryGetValue(out var vUi1Seconds));
        Assert.Equal(1_200, (long)Math.Round(vUi1Seconds, MidpointRounding.ToEven));

        // REQ-FN-002 build: one run only (run 1, ⅓ share) — insufficient data (n=1).
        Assert.Equal("insufficient data (n=1)", vRows["REQ-FN-002"].Build.Actual.TokensOut.Display());

        // Nothing on a row line is summed into a per-row total: the type has no member for one.
        Assert.Null(typeof(EstimateRowLine).GetProperty("Total"));
        Assert.DoesNotContain(
            typeof(EstimateRowPart).GetProperties(),
            aProperty => aProperty.Name.Contains("Total", StringComparison.Ordinal));
    }

    // ---- REQ-UI-080's engine side: no precedent and empty streams stay words, never zeros ----

    /// <summary>
    /// A step whose estimate is null reports no precedent, an actual under the floor reports
    /// insufficient data with its count, rowless runs are counted per step and never shared out, and
    /// a repository with an empty stream is not a repository with estimates (REQ-UI-080, BRD-216).
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-148 — no precedent, insufficient data and rowless runs stay words and counts")]
    public async Task NoPrecedentAndRowlessCountsStayHonest()
    {
        var vResult = await ReconcileAsync();

        Assert.Equal(1, vResult.ReposWithEstimates); // Sample empty has no record at all

        foreach (var vCard in vResult.Phases)
        {
            // After handoff: basis none on every row, so no forecast figure anywhere — null, never 0.
            Assert.Null(vCard.AfterHandoff.TokensOut);
            Assert.Null(vCard.AfterHandoff.DurationS);
            Assert.Equal(0, vCard.AfterHandoff.RowsCovered);
            Assert.Equal("insufficient data (n=0)", vCard.AfterHandoff.Actual.TokensOut.Display());
            Assert.Equal("insufficient data (n=0)", vCard.AfterHandoff.Actual.DurationS.Display());

            var vTriage = vCard.Cmds.First(aEntry => aEntry.Key == "triage").Value;
            Assert.Equal("none", vTriage.Estimate.Basis);
            Assert.Null(vTriage.Estimate.TokensOut);
            Assert.Null(vTriage.ActualInRange.TokensOut); // the oracle's dash
            Assert.Null(vTriage.ActualInRange.DurationS);

            // The one fix run that named no row is counted in the repository and shared into no phase.
            Assert.Equal(1, vCard.Cmds.First(aEntry => aEntry.Key == "fix-issues").Value.Actual.RowlessRunsInRepoN);
            Assert.Equal(0, vCard.Cmds.First(aEntry => aEntry.Key == "build-phase").Value.Actual.RowlessRunsInRepoN);
            Assert.Equal(0, vCard.Cmds.First(aEntry => aEntry.Key == "verify-phase").Value.Actual.RowlessRunsInRepoN);
            Assert.Equal(0, vTriage.Actual.RowlessRunsInRepoN);
        }

        // The fix step on phase 1 is the fixture's two-run case: insufficient data (n=2), never 9000.
        var vPhase1Fix = vResult.Phases[0].Cmds.First(aEntry => aEntry.Key == "fix-issues").Value;
        Assert.Equal("insufficient data (n=2)", vPhase1Fix.Actual.TokensOut.Display());
        Assert.Equal(1, vPhase1Fix.Actual.RowlessRunsInRepoN);

        // Phase 2's fix step has a single run: insufficient data (n=1).
        var vPhase2Fix = vResult.Phases[2].Cmds.First(aEntry => aEntry.Key == "fix-issues").Value;
        Assert.Equal("insufficient data (n=1)", vPhase2Fix.Actual.TokensOut.Display());

        // Positions: phase 1 build within (Opus) and below (Sonnet); phase 1 verify above; phase 2
        // verify within; refused figures have no position (§3.1's By step line).
        Assert.Equal("within", vResult.Phases[0].Cmds[0].Value.ActualInRange.TokensOut);
        Assert.Equal("below", vResult.Phases[1].Cmds[0].Value.ActualInRange.TokensOut);
        Assert.Equal("above", vResult.Phases[0].Cmds[1].Value.ActualInRange.TokensOut);
        Assert.Equal("within", vResult.Phases[2].Cmds[1].Value.ActualInRange.TokensOut);
        Assert.Null(vPhase1Fix.ActualInRange.TokensOut);
    }

    // ---- REQ-FN-149: the fourth separation, enforced by shape ----

    /// <summary>
    /// No member of the result tree combines an estimate with an actual: no ratio, difference or
    /// accuracy member exists, and only the five container records hold both sides side by side —
    /// the estimate side is always an <see cref="EstimateRange"/> and the actual always a
    /// <see cref="Figure"/> (REQ-FN-149, BRD-213, ADR-034).
    /// </summary>
    [Fact(DisplayName = "REQ-FN-149 — no result type can hold a ratio, difference or blended figure")]
    public void ResultTypesCannotExpressAPooledFigure()
    {
        var vForbidden = new[] { "Ratio", "Delta", "Difference", "Accuracy", "Percent", "Pooled", "Blended", "Variance" };
        var vTypes = ReachableTypes(typeof(EstimateReconciliation));

        foreach (var vType in vTypes)
        {
            foreach (var vMember in vType.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var vWord in vForbidden)
                {
                    // Word-boundary match: "DurationS" carries the letters "ratio" inside a word about
                    // time, and a substring scan would forbid the oracle's own duration key.
                    Assert.False(
                        System.Text.RegularExpressions.Regex.IsMatch(
                            vMember.Name, $@"\b{vWord}", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        $"{vType.Name}.{vMember.Name} could express a pooled figure (BRD-213)");
                }
            }
        }

        // The forecast triple is exactly the three legs tf-estimate.sh wrote — nothing derived.
        Assert.Equal(
            ["High", "Low", "Mid"],
            typeof(EstimateRange).GetProperties().Select(aProperty => aProperty.Name).OrderBy(aName => aName, StringComparer.Ordinal));

        // The actual side never carries a forecast type…
        foreach (var vProperty in typeof(EstimateActual).GetProperties())
        {
            Assert.False(
                vProperty.PropertyType == typeof(EstimateRange),
                $"EstimateActual.{vProperty.Name} holds an estimate — the two sides must not mix");
        }

        // …and the estimate side never carries a measurement type.
        foreach (var vSideType in new[] { typeof(EstimateSide), typeof(EstimateRowStep) })
        {
            foreach (var vProperty in vSideType.GetProperties())
            {
                Assert.False(
                    vProperty.PropertyType == typeof(Figure),
                    $"{vSideType.Name}.{vProperty.Name} holds an actual — the two sides must not mix");
            }
        }

        // The two sides meet only in the container records, as TWO members — never one. EstimateTotal
        // is not listed: it wraps an EstimatePart whole and adds only its refusal text, so the rule is
        // asserted where the two sides actually sit side by side.
        var vContainers = new[]
        {
            typeof(EstimateCmdFigures), typeof(EstimatePart), typeof(EstimateRowPart)
        };

        foreach (var vContainer in vContainers)
        {
            var vProperties = vContainer.GetProperties();

            // The estimate side may arrive as the range itself or wrapped in its side/step/part type;
            // the actual side always arrives as an EstimateActual. Two members, never one.
            var vEstimateSide = new[]
            {
                typeof(EstimateRange), typeof(EstimateSide), typeof(EstimateRowStep), typeof(EstimatePart)
            };

            Assert.Contains(vProperties, aProperty => vEstimateSide.Any(aType => Mentions(aProperty.PropertyType, aType)));
            Assert.Contains(vProperties, aProperty => Mentions(aProperty.PropertyType, typeof(EstimateActual)));
        }
    }

    // ---- helpers ----

    /// <summary>Asserts one card's Total line, tokens and time, estimate beside actual.</summary>
    /// <param name="aCard">The card.</param>
    /// <param name="aTokensLow">Expected token low.</param>
    /// <param name="aTokensMid">Expected token mid.</param>
    /// <param name="aTokensHigh">Expected token high.</param>
    /// <param name="aDurationLow">Expected seconds low.</param>
    /// <param name="aDurationMid">Expected seconds mid.</param>
    /// <param name="aDurationHigh">Expected seconds high.</param>
    /// <param name="aActualTokens">Expected actual tokens.</param>
    /// <param name="aActualDuration">Expected actual seconds.</param>
    /// <param name="aRunsN">Expected runs.</param>
    /// <param name="aApportionedN">Expected apportioned runs.</param>
    private static void AssertTotal(
        EstimatePhaseEntry aCard,
        long aTokensLow,
        long aTokensMid,
        long aTokensHigh,
        long aDurationLow,
        long aDurationMid,
        long aDurationHigh,
        long aActualTokens,
        long aActualDuration,
        int aRunsN,
        int aApportionedN)
    {
        var vTotal = aCard.Total.Part;

        Assert.NotNull(vTotal.TokensOut);
        Assert.Equal(aTokensLow, vTotal.TokensOut!.Low);
        Assert.Equal(aTokensMid, vTotal.TokensOut.Mid);
        Assert.Equal(aTokensHigh, vTotal.TokensOut.High);
        Assert.NotNull(vTotal.DurationS);
        Assert.Equal(aDurationLow, vTotal.DurationS!.Low);
        Assert.Equal(aDurationMid, vTotal.DurationS.Mid);
        Assert.Equal(aDurationHigh, vTotal.DurationS.High);

        Assert.Equal(aRunsN, vTotal.Actual.RunsN);
        Assert.Equal(aApportionedN, vTotal.Actual.ApportionedN);
        Assert.True(vTotal.Actual.TokensOut.TryGetValue(out var vTokens), $"no token figure on {aCard.App} phase {aCard.Phase}");
        Assert.Equal(aActualTokens, (long)Math.Round(vTokens, MidpointRounding.ToEven));
        Assert.True(vTotal.Actual.DurationS.TryGetValue(out var vSeconds), $"no duration figure on {aCard.App} phase {aCard.Phase}");
        Assert.Equal(aActualDuration, (long)Math.Round(vSeconds, MidpointRounding.ToEven));
    }

    /// <summary>Runs the reconciliation over the fixture-backed store.</summary>
    /// <returns>The reconciliation.</returns>
    private static async Task<EstimateReconciliation> ReconcileAsync() =>
        await EstimateMetrics.ReconcileAsync(BuildStore(), FixtureUserId, FixtureFramework);

    /// <summary>
    /// Builds the fixture store: both Sample repositories through the PRODUCTION parser — the same
    /// <see cref="StreamParser"/> the sync and the import use — so a field this test lost would be a
    /// field production loses too (the EstateParityHarness lesson).
    /// </summary>
    /// <returns>The store.</returns>
    private static FixtureTelemetryStore BuildStore()
    {
        var vAppRuns = Parse(SampleAppRepo, "sample__sample-app", "runs.jsonl", StreamKind.Runs);
        var vAppEstimates = ParseEstimates(SampleAppRepo, "sample__sample-app");
        var vEmptyEstimates = ParseEstimates(SampleEmptyRepo, "sample__sample-empty");

        return new FixtureTelemetryStore()
            .Seed(FixtureUserId, SampleAppRepo, FixtureFramework, aRuns: vAppRuns.Runs)
            .SeedEstimates(FixtureUserId, SampleAppRepo, FixtureFramework, vAppEstimates)
            .SeedEstimates(FixtureUserId, SampleEmptyRepo, FixtureFramework, vEmptyEstimates)
            // make-reference.sh lays each repository out with its own docs/<App>-Checklist.md, which is
            // where the reference names an app from; a sync or import reads the same file (BRD-220).
            .WithChecklistApp(FixtureUserId, SampleAppRepo, "SampleApp")
            .WithChecklistApp(FixtureUserId, SampleEmptyRepo, "SampleEmpty");
    }

    /// <summary>Exports the fixture snapshot and returns the parsed <c>tflens.json</c>.</summary>
    /// <returns>The export document.</returns>
    private static async Task<JsonObject> ExportSampleSnapshotAsync()
    {
        var vDataRoot = ExportFixture.TemporaryDataRoot();

        try
        {
            // No prices.json is written: the card TfLens ships with prices claude-opus-5-5 at the list
            // price Anthropic publishes, as the reference's card does (owner request 2026-11-12).
            var vExporter = ExportFixture.Exporter(vDataRoot, BuildStore());
            var vResult = await vExporter.ExportAsync(FixtureUserId, FixtureFramework, ExportFixture.Date);
            return JsonNode.Parse(await File.ReadAllTextAsync(vResult.JsonPath))!.AsObject();
        }
        finally
        {
            Directory.Delete(vDataRoot, recursive: true);
        }
    }

    /// <summary>Parses one fixture stream file through the production parser.</summary>
    /// <param name="aRepo">The <c>owner/name</c> the records belong to.</param>
    /// <param name="aDirectory">The fixture directory name.</param>
    /// <param name="aFile">The stream file.</param>
    /// <param name="aKind">The stream kind.</param>
    /// <returns>The parse result.</returns>
    private static ParseResult Parse(string aRepo, string aDirectory, string aFile, StreamKind aKind)
    {
        var vPath = Path.Combine(FixtureRoot, aDirectory, aFile);
        var vText = File.Exists(vPath) ? File.ReadAllText(vPath) : string.Empty;
        return new StreamParser().Parse(FixtureUserId, aRepo, "fixture", aKind, vText);
    }

    /// <summary>Parses the fixture's estimate records.</summary>
    /// <param name="aRepo">The <c>owner/name</c>.</param>
    /// <param name="aDirectory">The fixture directory name.</param>
    /// <returns>The parsed estimate records.</returns>
    /// <summary>
    /// Exports the Sample app snapshot and runs tools/parity-compare.py over it and the oracle's rollup.
    /// </summary>
    /// <returns>The script's exit code and everything it printed.</returns>
    private static async Task<(int ExitCode, string Report)> RunParityCompareAsync()
    {
        var vRepoRoot = RepositoryRoot();
        var vWork = Path.Combine(vRepoRoot, "tests", ".artifacts", "estimates-parity");
        Directory.CreateDirectory(vWork);

        var vTflens = Path.Combine(vWork, "tflens.json");
        await File.WriteAllTextAsync(vTflens, (await ExportSampleSnapshotAsync()).ToJsonString());

        var vStart = new System.Diagnostics.ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = vRepoRoot
        };
        foreach (var vArgument in new[]
                 {
                     Path.Combine(vRepoRoot, "tools", "parity-compare.py"),
                     Path.Combine(FixtureRoot, "reference-rollup.json"),
                     vTflens,
                     "--allow-environment-keys"
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

    /// <summary>The repository root — the directory holding <c>TfLens.slnx</c>.</summary>
    /// <returns>The absolute path.</returns>
    private static string RepositoryRoot()
    {
        var vDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (vDirectory is not null && !File.Exists(Path.Combine(vDirectory.FullName, "TfLens.slnx")))
        {
            vDirectory = vDirectory.Parent;
        }

        Assert.True(vDirectory is not null, "TfLens.slnx was not found above the test binaries.");
        return vDirectory!.FullName;
    }

    private static IReadOnlyList<EstimateRecord> ParseEstimates(string aRepo, string aDirectory) =>
        Parse(aRepo, aDirectory, "estimates.jsonl", StreamKind.Estimates).Estimates;

    /// <summary>Loads one oracle reference file.</summary>
    /// <param name="aFileName">The reference file name.</param>
    /// <returns>The root element.</returns>
    private static JsonElement LoadReference(string aFileName)
    {
        var vPath = Path.Combine(FixtureRoot, aFileName);
        Assert.True(File.Exists(vPath), $"{aFileName} is missing at {vPath} — run make-reference.sh.");
        return JsonDocument.Parse(File.ReadAllText(vPath)).RootElement.Clone();
    }

    /// <summary>
    /// Deep-compares two JSON trees: objects key by key (order-free), arrays positionally, numbers by
    /// value across int/float spellings — the same tolerance <c>parity-compare.py</c> applies, because
    /// presentation is never a finding.
    /// </summary>
    /// <param name="aExpected">The oracle's node.</param>
    /// <param name="aActual">TfLens's node.</param>
    /// <param name="aPath">The key path, for the finding text.</param>
    /// <param name="aDifferences">Collects any mismatch.</param>
    private static void JsonDeepEquals(JsonNode? aExpected, JsonNode? aActual, string aPath, List<string> aDifferences)
    {
        if (aExpected is null && aActual is null)
        {
            return;
        }

        if (aExpected is null || aActual is null)
        {
            aDifferences.Add($"{aPath}: {(aExpected is null ? "MISSING in TfLens" : "ADDED by TfLens")}");
            return;
        }

        switch (aExpected)
        {
            case JsonObject vExpectedObject:
                if (aActual is not JsonObject vActualObject)
                {
                    aDifferences.Add($"{aPath}: TYPE object vs {NodeKind(aActual)}");
                    return;
                }

                foreach (var vKey in vExpectedObject.Select(aPair => aPair.Key).Concat(
                             vActualObject.Select(aPair => aPair.Key)))
                {
                    var vHasExpected = vExpectedObject.ContainsKey(vKey);
                    var vHasActual = vActualObject.ContainsKey(vKey);

                    if (!vHasExpected)
                    {
                        aDifferences.Add($"{aPath}.{vKey}: ADDED by TfLens");
                    }
                    else if (!vHasActual)
                    {
                        aDifferences.Add($"{aPath}.{vKey}: MISSING in TfLens");
                    }
                    else
                    {
                        JsonDeepEquals(vExpectedObject[vKey], vActualObject[vKey], $"{aPath}.{vKey}", aDifferences);
                    }
                }

                break;

            case JsonArray vExpectedArray:
                if (aActual is not JsonArray vActualArray)
                {
                    aDifferences.Add($"{aPath}: TYPE array vs {NodeKind(aActual)}");
                    return;
                }

                if (vExpectedArray.Count != vActualArray.Count)
                {
                    aDifferences.Add($"{aPath}: LENGTH {vExpectedArray.Count} vs {vActualArray.Count}");
                    return;
                }

                for (var vIndex = 0; vIndex < vExpectedArray.Count; vIndex++)
                {
                    JsonDeepEquals(vExpectedArray[vIndex], vActualArray[vIndex], $"{aPath}[{vIndex}]", aDifferences);
                }

                break;

            case JsonValue vExpectedValue:
                if (aActual is not JsonValue vActualValue)
                {
                    aDifferences.Add($"{aPath}: TYPE value vs {NodeKind(aActual)}");
                    return;
                }

                var vNumbers = vExpectedValue.TryGetValue<decimal>(out var vExpectedNumber)
                               & vActualValue.TryGetValue<decimal>(out var vActualNumber);

                if (vNumbers)
                {
                    // decimal compare — int vs float spellings of one number are presentation, never
                    // a finding (the same tolerance parity-compare.py applies).
                    if (vExpectedNumber != vActualNumber)
                    {
                        aDifferences.Add($"{aPath}: DIFF {vExpectedNumber} vs {vActualNumber}");
                    }
                }
                else if (!string.Equals(ValueText(vExpectedValue), ValueText(vActualValue), StringComparison.Ordinal))
                {
                    aDifferences.Add($"{aPath}: DIFF {ValueText(vExpectedValue)} vs {ValueText(vActualValue)}");
                }

                break;
        }
    }

    /// <summary>Names a node's kind for a finding.</summary>
    /// <param name="aNode">The node.</param>
    /// <returns>The kind name.</returns>
    private static string NodeKind(JsonNode? aNode) =>
        aNode is JsonObject ? "object" : aNode is JsonArray ? "array" : aNode is null ? "null" : "value";

    /// <summary>Reads a value node's text for a finding.</summary>
    /// <param name="aValue">The value.</param>
    /// <returns>The text.</returns>
    private static string ValueText(JsonValue aValue) => aValue.ToJsonString();

    /// <summary>True when a property type mentions the target type directly or as a generic argument.</summary>
    /// <param name="aPropertyType">The property's type.</param>
    /// <param name="aTarget">The type to look for.</param>
    /// <returns>Whether the property exposes the target.</returns>
    private static bool Mentions(Type aPropertyType, Type aTarget)
    {
        var vType = Nullable.GetUnderlyingType(aPropertyType) ?? aPropertyType;

        return vType == aTarget
               || (vType.IsGenericType && vType.GetGenericArguments().Any(aArgument => Mentions(aArgument, aTarget)));
    }

    /// <summary>
    /// Every type reachable from the reconciliation's public surface — the tree a page can bind to.
    /// </summary>
    /// <param name="aRoot">The root type.</param>
    /// <returns>The reachable TfLens contract types.</returns>
    private static IReadOnlyCollection<Type> ReachableTypes(Type aRoot)
    {
        var vSeen = new HashSet<Type>();
        var vQueue = new Queue<Type>();
        vQueue.Enqueue(aRoot);

        while (vQueue.Count > 0)
        {
            var vType = vQueue.Dequeue();
            if (!vSeen.Add(vType))
            {
                continue;
            }

            foreach (var vProperty in vType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var vPropertyType = Nullable.GetUnderlyingType(vProperty.PropertyType) ?? vProperty.PropertyType;

                if (vPropertyType.IsGenericType)
                {
                    foreach (var vArgument in vPropertyType.GetGenericArguments())
                    {
                        EnqueueContracts(vArgument, vQueue);
                    }
                }

                EnqueueContracts(vPropertyType, vQueue);
            }
        }

        return vSeen;
    }

    /// <summary>Queues a type — and its generic arguments — when it is one of this app's contract records.</summary>
    /// <param name="aType">The candidate.</param>
    /// <param name="aQueue">The traversal queue.</param>
    private static void EnqueueContracts(Type aType, Queue<Type> aQueue)
    {
        var vType = Nullable.GetUnderlyingType(aType) ?? aType;

        if (vType.IsGenericType)
        {
            foreach (var vArgument in vType.GetGenericArguments())
            {
                EnqueueContracts(vArgument, aQueue);
            }
        }

        if (vType.Namespace is { } vNamespace
            && vNamespace.StartsWith("TfLens.Core.Contracts", StringComparison.Ordinal)
            && vType != typeof(Figure))
        {
            aQueue.Enqueue(vType);
        }
    }
}
