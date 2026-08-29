using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The rework cost figures (REQ-FN-079, BRD-122, BRD-123, ADR-019).
/// </summary>
/// <remarks>
/// A fix run that repaired three misses has one token window; dividing by three is arithmetic, not
/// measurement. These tests pin the arithmetic and the split — the <i>shape</i> that makes a blended
/// number unrepresentable is pinned in <c>MissInvariantTests</c>.
/// </remarks>
public sealed class MissCostTests
{
    private const string Framework = "techieflow";

    /// <summary>The headline column counts <c>sole</c> records only; a <c>shared:3</c> never reaches it.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ASharedRecordNeverReachesTheSoleColumn()
    {
        var vMoney = await MoneyAsync(
        [
            // Three runs that each closed ONE miss, and one run that closed three. The divisor is
            // DERIVED from that (2026-08-29): the stored string is no longer what decides the split,
            // so the fixture has to model the runs rather than assert the answer.
            MissFixtures.Fix("M1", aCostAttribution: "sole", aTokensOut: 100, aFixRunId: "R1"),
            MissFixtures.Fix("M2", aCostAttribution: "sole", aTokensOut: 200, aFixRunId: "R2"),
            MissFixtures.Fix("M3", aCostAttribution: "sole", aTokensOut: 300, aFixRunId: "R3"),
            MissFixtures.Fix("M4", aTokensOut: 900, aFixRunId: "R4"),
            MissFixtures.Fix("M5", aTokensOut: 900, aFixRunId: "R4"),
            MissFixtures.Fix("M6", aTokensOut: 900, aFixRunId: "R4")
        ]);

        vMoney.SoleRecords.Should().Be(3);
        vMoney.SharedRecords.Should().Be(3);
        vMoney.TokensPerMissFixed.Sole.Display().Should().Be("200", "the mean of 100, 200 and 300");
        vMoney.TokensPerMissFixed.Apportioned.Display().Should().Be("300", "900 divided across three misses");
    }

    /// <summary>Neither column is ever the blend of both.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NeitherColumnIsTheBlendOfBoth()
    {
        var vMoney = await MoneyAsync(
        [
            MissFixtures.Fix("M1", aCostAttribution: "sole", aTokensOut: 100, aFixRunId: "R1"),
            MissFixtures.Fix("M2", aCostAttribution: "sole", aTokensOut: 200, aFixRunId: "R2"),
            MissFixtures.Fix("M3", aCostAttribution: "sole", aTokensOut: 300, aFixRunId: "R3"),
            // One run closed all three, so each window divides by three: 50, 100, 150 — mean 100.
            MissFixtures.Fix("M4", aTokensOut: 150, aFixRunId: "R4"),
            MissFixtures.Fix("M5", aTokensOut: 300, aFixRunId: "R4"),
            MissFixtures.Fix("M6", aTokensOut: 450, aFixRunId: "R4")
        ]);

        vMoney.TokensPerMissFixed.Sole.TryGetValue(out var vSole).Should().BeTrue();
        vMoney.TokensPerMissFixed.Apportioned.TryGetValue(out var vApportioned).Should().BeTrue();

        vSole.Should().Be(200d);
        vApportioned.Should().Be(100d);
        vSole.Should().NotBe(150d, "150 is the blended mean the shape exists to make unrepresentable");
    }

    /// <summary>A <c>none</c> attribution is a count, never a divisor, and is correct data.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NoneIsACountAndNeverADivisor()
    {
        var vMoney = await MoneyAsync(
        [
            MissFixtures.Fix("M1", aCostAttribution: "sole", aTokensOut: 100, aFixRunId: "R1"),
            MissFixtures.Fix("M2", aCostAttribution: "sole", aTokensOut: 200, aFixRunId: "R2"),
            MissFixtures.Fix("M3", aCostAttribution: "sole", aTokensOut: 300, aFixRunId: "R3"),
            // No run to divide by: genuinely unattributable, and still not a denominator.
            MissFixtures.Fix("M4", aCostAttribution: "none", aFixRunId: null),
            MissFixtures.Fix("M5", aCostAttribution: "none", aFixRunId: null)
        ]);

        vMoney.TokensPerMissFixed.NoneCount.Should().Be(2);
        vMoney.TokensPerMissFixed.Sole.SupportingRecords.Should().Be(3, "the unattributable pair is not a denominator");
        vMoney.TokensPerMissFixed.Sole.Display().Should().Be("200");
    }

    /// <summary>
    /// A stored attribution is recomputed, never trusted — and only a record with no window at all
    /// is unattributable.
    /// </summary>
    /// <remarks>
    /// Replaced <c>AnAbsentAttributionIsNeverFoldedIntoNone</c> on 2026-08-29, when BRD §13 caught
    /// TfLens reading the stored string. The stored value cannot be trusted for two reasons the
    /// stream can prove about itself: it is written one record at a time (a run closing four misses
    /// stamps shared:1..shared:4 and only the last is right), and records written before 2026-08-28
    /// carry <c>none</c> from the empty-<c>reqs_touched</c> bug. What survives from the old test is
    /// the SCHEMA.md §2.5 point it existed to make — absent and <c>none</c> are different facts —
    /// now expressed where it still bites: a stale <c>none</c> with a real window is RECOVERED,
    /// while a record with no window is unattributable however it is labelled.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStaleAttributionIsRecomputedRatherThanTrusted()
    {
        var vMoney = await MoneyAsync(
        [
            // Stored `none`, but it names a run and carries a window: the divisor recovers it.
            MissFixtures.Fix("M1", aCostAttribution: "none", aTokensOut: 100, aFixRunId: "R1"),
            // Never stamped at all — recomputed, not discarded.
            MissFixtures.Fix("M2", aTokensOut: 200, aFixRunId: "R2"),
            // Stamped with something unparseable — the recompute does not care what it says.
            MissFixtures.Fix("M3", aCostAttribution: "shared:oops", aTokensOut: 300, aFixRunId: "R3"),
            // No window: unattributable, and no divisor can invent one.
            MissFixtures.Fix("M4", aCostAttribution: "sole", aTokensOut: 400, aTokensScope: null),
            MissFixtures.Fix("M5", aCostAttribution: "sole", aTokensOut: 500, aFixRunId: null)
        ]);

        vMoney.SoleRecords.Should().Be(3, "three runs each closed exactly one miss");
        vMoney.SharedRecords.Should().Be(0);
        vMoney.TokensPerMissFixed.NoneCount.Should().Be(2, "no window means nothing to divide");
        vMoney.RecoveredRecords.Should().Be(1, "one stored `none` had a real window after all");
        vMoney.TokensPerMissFixed.Sole.Display().Should().Be("200", "the mean of 100, 200 and 300");
    }

    /// <summary>Measured dollars come from OpenCode records and are never summed across harnesses.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task MeasuredDollarsComeFromOpenCodeOnly()
    {
        var vMoney = await MoneyAsync(
        [
            MissFixtures.Fix("M1", aHarness: "opencode", aCostUsd: 0.10m, aTokensOut: 100),
            MissFixtures.Fix("M2", aHarness: "opencode", aCostUsd: 0.20m, aTokensOut: 100),
            MissFixtures.Fix("M3", aHarness: "opencode", aCostUsd: 0.30m, aTokensOut: 100),
            MissFixtures.Fix("M4", aHarness: "claude-code", aCostUsd: 99.00m, aTokensOut: 400),
            MissFixtures.Fix("M5", aHarness: "codex", aCostUsd: 99.00m, aTokensOut: 400)
        ]);

        var vOpenCode = vMoney.ByHarness.Single(aRow => aRow.Harness == "opencode");
        var vClaude = vMoney.ByHarness.Single(aRow => aRow.Harness == "claude-code");
        var vCodex = vMoney.ByHarness.Single(aRow => aRow.Harness == "codex");

        vOpenCode.MeasuredUsdTotal.Should().Be(0.60m);
        vOpenCode.MeasuredUsdRecords.Should().Be(3);
        vOpenCode.MeasuredUsdPerMiss.Display().Should().Be("0.2");
        vOpenCode.EstimateLabel.Should().BeNull("OpenCode dollars are measured, not estimated");

        vClaude.MeasuredUsdTotal.Should().BeNull("cost_usd is a measurement on OpenCode and nowhere else");
        vClaude.MeasuredUsdRecords.Should().Be(0);
        vClaude.MeasuredUsdPerMiss.Kind.Should().Be(FigureKind.NotApplicable);
        vClaude.TokensOut.Should().Be(400, "tokens are the primary figure for Claude Code");
        vClaude.TokenRecords.Should().Be(1);
        vClaude.EstimateLabel.Should().Be(RateCard.EstimateLabel);
        vCodex.EstimateLabel.Should().Be(RateCard.EstimateLabel);
    }

    /// <summary>A harness with no records still gets a row, rendered as an em dash rather than vanishing.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AHarnessWithNoRecordsStillGetsARow()
    {
        var vMoney = await MoneyAsync([MissFixtures.Fix("M1", aHarness: "opencode")]);

        vMoney.ByHarness.Select(aRow => aRow.Harness).Should().Equal(ExtraMetrics.HarnessOrder);
        vMoney.ByHarness.Single(aRow => aRow.Harness == "codex").Records.Should().Be(0);
        vMoney.ByHarness.Single(aRow => aRow.Harness == "codex").MeasuredUsdPerMiss.Display().Should().Be("—");
    }

    /// <summary>
    /// A token sum of zero is distinguishable from no counts having been recorded at all.
    /// </summary>
    /// <remarks>
    /// This is the shape of the live data: every <c>log-miss --fixed</c> record carries <c>null</c>
    /// tokens, and a bare <c>tokens_out = 0</c> would read as "the rework cost nothing".
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AZeroTokenSumIsDistinguishableFromNoCountsRecorded()
    {
        var vMoney = await MoneyAsync(
        [
            MissFixtures.Fix("M1", aHarness: "claude-code", aCostAttribution: "none", aFixRunId: null),
            MissFixtures.Fix("M2", aHarness: "claude-code", aCostAttribution: "none", aFixRunId: null)
        ]);

        var vClaude = vMoney.ByHarness.Single(aRow => aRow.Harness == "claude-code");

        vClaude.Records.Should().Be(2);
        vClaude.TokenRecords.Should().Be(0, "no record carried a count, which is not the same as zero tokens");
        vClaude.TokensOut.Should().Be(0);
    }

    /// <summary>A fix carrying no token count contributes nothing rather than a zero.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AFixCarryingNoTokenCountIsNotCountedAsZero()
    {
        var vMoney = await MoneyAsync(
        [
            MissFixtures.Fix("M1", aCostAttribution: "sole", aTokensOut: 100, aFixRunId: "R1"),
            MissFixtures.Fix("M2", aCostAttribution: "sole", aTokensOut: 200, aFixRunId: "R2"),
            MissFixtures.Fix("M3", aCostAttribution: "sole", aTokensOut: 300, aFixRunId: "R3"),
            MissFixtures.Fix("M4", aCostAttribution: "sole", aFixRunId: "R4")
        ]);

        vMoney.SoleRecords.Should().Be(4);
        vMoney.TokensPerMissFixed.Sole.SupportingRecords.Should().Be(3, "only three carried a count");
        vMoney.TokensPerMissFixed.Sole.Display().Should().Be("200", "a fourth zero would have made it 150");
    }

    /// <summary>Runs the engine over fix records and returns the <c>app</c> segment's money block.</summary>
    /// <param name="aFixes">The fix records to seed; a miss is seeded for each.</param>
    /// <returns>The money block.</returns>
    private static async Task<MissMoney> MoneyAsync(IReadOnlyList<MissFixRecord> aFixes)
    {
        var vMisses = aFixes
            .Select(aFix => aFix.MissId)
            .Distinct(StringComparer.Ordinal)
            .Select(aId => MissFixtures.Miss(aId))
            .ToList();

        var vStore = new FixtureTelemetryStore()
            .SeedMisses(MissFixtures.UserId, MissFixtures.Repo, Framework, vMisses, aFixes);

        var vAnalysis = await new MetricsEngine(vStore, NullLogger<MetricsEngine>.Instance)
            .AnalyseAsync(MissFixtures.UserId, Framework);

        return vAnalysis.Misses.Live["app"].Cost;
    }
}
