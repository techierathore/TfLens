using FluentAssertions;
using TfLens.Core.Metrics;
using TfLens.Core.Tests.Fakes;

namespace TfLens.Core.Tests.Metrics.Extras;

/// <summary>
/// BRD-56..BRD-60, ADR-009 and SCHEMA.md §4 — routing drift and the repricing estimate.
/// </summary>
/// <remarks>
/// As with the harness comparison, the expected values were obtained independently from the raw JSONL
/// with <c>python3</c> and are recorded in <c>DECISIONS.md</c> as the REQ-FN-064 hand spot-check.
/// </remarks>
public sealed class RoutingRepricingTests : IDisposable
{
    private readonly string objDataRoot = ExtrasFixture.TemporaryDataRoot();

    /// <summary>Removes the throwaway data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>
    /// Drift counts match the hand count, and the unrouted run is listed first so the signal is at the
    /// top of the table rather than buried in it.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task DriftCountsMatchAndUnroutedRunsComeFirst()
    {
        var vRouting = await ExtrasFixture.Extras(objDataRoot)
            .AnalyseRoutingAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        vRouting.RunsWithRoutingFields.Should().Be(9);
        vRouting.UnroutedRuns.Should().Be(1);
        vRouting.DistinctModels.Should().Be(5);

        vRouting.Drift.Should().HaveCount(9);
        vRouting.Drift[0].Routed.Should().BeFalse();
        vRouting.Drift[0].TierModel.Should().Be("claude-opus-4-6");
        vRouting.Drift[0].Model.Should().Be("claude-sonnet-4-6");
        vRouting.Drift.Skip(1).Should().OnlyContain(aRow => aRow.Routed != false);
    }

    /// <summary>
    /// Tokens by observed model sum the four §2.5 run fields per model, largest total first.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TokensByModelMatchTheHandCountFromRawJsonl()
    {
        var vRouting = await ExtrasFixture.Extras(objDataRoot)
            .AnalyseRoutingAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        vRouting.TokensByModel.Select(aM => aM.Model).Should().Equal(
            "claude-opus-4-6",
            "claude-sonnet-4-6",
            "anthropic/claude-sonnet-4-6",
            "claude-haiku-4-5",
            "gpt-5-codex");

        var vOpus = vRouting.TokensByModel[0];
        vOpus.TokensIn.Should().Be(200000);
        vOpus.TokensOut.Should().Be(30000);
        vOpus.TokensCacheRead.Should().Be(800000);
        vOpus.TokensCacheWrite.Should().Be(100000);
        vOpus.Total.Should().Be(1130000);
    }

    /// <summary>
    /// The repricing estimates match the hand calculation, the counterfactual reprices to the most
    /// expensive observed model, and both figures are computed over the same token base.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task RepricingMatchesTheHandCalculation()
    {
        var vRouting = await ExtrasFixture.Extras(objDataRoot)
            .AnalyseRoutingAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        // Hand calculation, corrected 2026-08-27 (recorded in DECISIONS.md). The seven priceable runs
        // are, exactly: 0.975 + 0.6375 + 0.05125 + 0.435 + 0.192 + 2.775 + 0.01795 = 5.0837 -> 5.08.
        // The previous expectation of 5.07 omitted the last row (the triage-issues haiku run) — an
        // off-by-one in the original hand count, not a code defect. The code was independently wrong:
        // it rounded each run to cents before summing, which gave 5.10.
        vRouting.ActualMixUsd.Should().Be(5.08m);
        vRouting.AllAtMaxUsd.Should().Be(6.85m);
        vRouting.MostExpensiveModel.Should().Be("claude-opus-4-6");
        vRouting.DeltaUsd.Should().Be(1.77m);
    }

    /// <summary>
    /// A run whose <c>tokens_scope</c> is <c>none</c> is excluded from the repricing and counted, never
    /// treated as having spent nothing.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task RunsWithoutATokenScopeAreExcludedAndCounted()
    {
        var vRouting = await ExtrasFixture.Extras(objDataRoot)
            .AnalyseRoutingAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        vRouting.RunsExcludedNoTokenScope.Should().Be(1);
    }

    /// <summary>
    /// An observed model the rate card does not price is named rather than priced at zero, and its
    /// tokens are left out of both repricing figures so the two stay comparable.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task UnpricedObservedModelsAreNamedNotPricedAtZero()
    {
        var vRouting = await ExtrasFixture.Extras(objDataRoot)
            .AnalyseRoutingAsync(ExtrasFixture.UserId, ExtrasFixture.Framework);

        vRouting.MissingPriceModels.Should().Equal("gpt-5-codex");
    }

    /// <summary>
    /// A provider-prefixed model id resolves to the bare rate-card line, so OpenCode's
    /// <c>anthropic/claude-sonnet-4-6</c> is priced by the same entry as Claude Code's
    /// <c>claude-sonnet-4-6</c>.
    /// </summary>
    [Fact]
    public void ProviderPrefixedModelIdResolvesToTheBareRateCardLine()
    {
        var vCard = RateCard.Default();

        vCard.Find("anthropic/claude-sonnet-4-6").Should().Be(vCard.Find("claude-sonnet-4-6"));
        vCard.Find("gpt-5-codex").Should().BeNull();
    }

    /// <summary>
    /// The rate card written on first run announces in the file itself that it is an input and not a
    /// measurement, because JSON cannot carry a comment.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task DefaultRateCardDeclaresItselfAnEstimateInput()
    {
        var vPath = Path.Combine(objDataRoot, "prices.json");
        await RateCard.EnsureDefaultsAsync(vPath);

        var vText = await File.ReadAllTextAsync(vPath);

        vText.Should().Contain("NOT A MEASUREMENT");
        vText.Should().Contain(RateCard.EstimateLabel);
        vText.Should().Contain("\"estimate_only\": true");
        File.Exists(Path.Combine(objDataRoot, "README.md")).Should().BeTrue();
    }
}
