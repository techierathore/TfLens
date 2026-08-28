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
            MissFixtures.Fix("M1", aCostAttribution: "sole", aTokensOut: 100),
            MissFixtures.Fix("M2", aCostAttribution: "sole", aTokensOut: 200),
            MissFixtures.Fix("M3", aCostAttribution: "sole", aTokensOut: 300),
            MissFixtures.Fix("M4", aCostAttribution: "shared:3", aTokensOut: 900),
            MissFixtures.Fix("M5", aCostAttribution: "shared:3", aTokensOut: 900),
            MissFixtures.Fix("M6", aCostAttribution: "shared:3", aTokensOut: 900)
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
            MissFixtures.Fix("M1", aCostAttribution: "sole", aTokensOut: 100),
            MissFixtures.Fix("M2", aCostAttribution: "sole", aTokensOut: 200),
            MissFixtures.Fix("M3", aCostAttribution: "sole", aTokensOut: 300),
            MissFixtures.Fix("M4", aCostAttribution: "shared:2", aTokensOut: 100),
            MissFixtures.Fix("M5", aCostAttribution: "shared:2", aTokensOut: 200),
            MissFixtures.Fix("M6", aCostAttribution: "shared:2", aTokensOut: 300)
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
            MissFixtures.Fix("M1", aCostAttribution: "sole", aTokensOut: 100),
            MissFixtures.Fix("M2", aCostAttribution: "sole", aTokensOut: 200),
            MissFixtures.Fix("M3", aCostAttribution: "sole", aTokensOut: 300),
            MissFixtures.Fix("M4", aCostAttribution: "none", aFixRunId: null),
            MissFixtures.Fix("M5", aCostAttribution: "none", aFixRunId: null)
        ]);

        vMoney.TokensPerMissFixed.NoneCount.Should().Be(2);
        vMoney.TokensPerMissFixed.Sole.SupportingRecords.Should().Be(3, "the unattributable pair is not a denominator");
        vMoney.TokensPerMissFixed.Sole.Display().Should().Be("200");
    }

    /// <summary>An absent attribution is missing data and is never folded into <c>none</c>.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnAbsentAttributionIsNeverFoldedIntoNone()
    {
        var vMoney = await MoneyAsync(
        [
            MissFixtures.Fix("M1", aCostAttribution: "none"),
            MissFixtures.Fix("M2"),
            MissFixtures.Fix("M3", aCostAttribution: "shared:oops")
        ]);

        vMoney.TokensPerMissFixed.NoneCount.Should().Be(1);
        vMoney.AttributionMissing.Should().Be(2);
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
            MissFixtures.Fix("M1", aCostAttribution: "sole", aTokensOut: 100),
            MissFixtures.Fix("M2", aCostAttribution: "sole", aTokensOut: 200),
            MissFixtures.Fix("M3", aCostAttribution: "sole", aTokensOut: 300),
            MissFixtures.Fix("M4", aCostAttribution: "sole")
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
