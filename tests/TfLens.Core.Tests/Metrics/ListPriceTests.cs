using FluentAssertions;
using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// Pricing measured tokens at the published rate — a price, never a bill
/// (SCHEMA.md §2.5b, REQ-FN-136, REQ-FN-137).
/// </summary>
public sealed class ListPriceTests
{
    /// <summary>The rate card the estate's own figures are priced at.</summary>
    private static readonly RateCard Prices = new(
        new Dictionary<string, ModelRate>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-5"] = new(5m, 25m, 0.5m, 6.25m),
            ["claude-sonnet-5"] = new(2m, 10m, 0.2m, 2.5m)
        },
        "(test)");

    /// <summary>A single-model window prices from its own four token counters.</summary>
    [Fact]
    public void ASingleModelWindowPricesFromItsOwnTokens()
    {
        var vPrice = ListPrice.For(
            Run("claude-opus-5", aTokensIn: 288, aTokensOut: 148049, aCacheRead: 20366879, aCacheWrite: 314488),
            Prices);

        vPrice.Should().Be(15.851655d);
    }

    /// <summary>
    /// A price landing on a decimal midpoint rounds the way the reference rounds it.
    /// </summary>
    /// <remarks>
    /// The run above prices at <c>15.8516545</c> — a midpoint at the sixth place, whose nearest double
    /// sits fractionally ABOVE it, so it rounds up. <c>Math.Round(double, 6)</c> scales before comparing,
    /// sees a tie, rounds to even and publishes <c>15.851654</c>. One unit in the sixth decimal place is
    /// the whole difference between a passing parity gate and a failing one.
    /// </remarks>
    [Fact]
    public void AMidpointPriceRoundsOnTheValueRatherThanOnATie()
    {
        ListPrice.Round(15.8516545d, 6).Should().Be(15.851655d, "the true binary value is above the midpoint");
        ListPrice.Round(6.1239685d, 6).Should().Be(6.123969d);
    }

    /// <summary>
    /// A model the card cannot price is left out, never counted as free.
    /// </summary>
    /// <remarks>
    /// This is the one case where silence is honest and a zero is a lie: pricing an unknown model at
    /// nothing would make a phase that ran only that model look free.
    /// </remarks>
    [Fact]
    public void AnUnpricedModelIsLeftOutRatherThanCountedAsFree()
    {
        ListPrice.For(Run("some-model-nobody-priced", aTokensOut: 100_000), Prices).Should().BeNull();
    }

    /// <summary>A run naming no model at all cannot be priced.</summary>
    [Fact]
    public void ARunNamingNoModelCannotBePriced()
    {
        ListPrice.For(Run(null, aTokensOut: 100_000), Prices).Should().BeNull();
    }

    /// <summary>
    /// A mixed-model window shares its input and cache tokens by each model's output share.
    /// </summary>
    /// <remarks>
    /// Output is exact per model because the record carries the split; input and cache are counted for
    /// the window as a whole, so apportioning them is arithmetic rather than measurement — which is why
    /// such a window's price is never attributed to a single model in the per-model band.
    /// </remarks>
    [Fact]
    public void AMixedWindowSharesInputAndCacheByOutputShare()
    {
        var vRun = Run(null, aTokensIn: 1_000_000, aTokensOut: 0) with
        {
            ModelTokensOut = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["claude-opus-5"] = 750_000,
                ["claude-sonnet-5"] = 250_000
            }
        };

        // output: 0.75M × $25 + 0.25M × $10 = 18.75 + 2.50 = 21.25
        // input:  1M shared 75/25 → 0.75 × $5 + 0.25 × $2 = 3.75 + 0.50 = 4.25
        ListPrice.For(vRun, Prices).Should().Be(25.50d);
    }

    /// <summary>The phase block publishes the price, its records and the ones it could not price.</summary>
    [Fact]
    public void ThePhaseBlockPublishesWhatItCouldNotPrice()
    {
        var vAnalysis = PhaseMetrics.Compute(
        [
            Run("claude-opus-5", aTokensOut: 1_000_000) with { Cmd = "build-phase" },
            Run("some-model-nobody-priced", aTokensOut: 1_000_000) with { Cmd = "build-phase" }
        ],
            Prices);

        var vRow = vAnalysis.Phases.Should().ContainSingle().Subject;

        vRow.Money.ListUsd.Should().Be(25m, "only the priced run contributes");
        vRow.Money.ListUsdRecords.Should().Be(1);
        vRow.Money.ListUsdUnpricedN.Should().Be(1, "the unpriced run is counted, never priced at zero");
    }

    /// <summary>
    /// A subscription's dollars are never reported as money billed.
    /// </summary>
    /// <remarks>
    /// A flat monthly fee bills nothing extra, so its zero is true of the marginal cost and false of the
    /// money. Only <c>metered</c> records reach <c>money_usd</c>; a plan's dollars are allowance
    /// consumed against a limit, and they get their own line.
    /// </remarks>
    [Fact]
    public void OnlyMeteredDollarsAreReportedAsMoney()
    {
        var vAnalysis = PhaseMetrics.Compute(
        [
            Priced(BillingModes.Metered, 1.50m),
            Priced(BillingModes.Plan, 2.25m),
            Priced(BillingModes.Subscription, 0m)
        ],
            Prices);

        var vMoney = vAnalysis.Phases.Should().ContainSingle().Subject.Money;

        vMoney.MoneyUsd.Should().Be(1.50m);
        vMoney.MoneyRecords.Should().Be(1);
        vMoney.PlanAllowanceUsd.Should().Be(2.25m, "a plan's dollars are a meter, not an invoice");
        vMoney.PlanAllowanceRecords.Should().Be(1);
        vMoney.BillingModes.Should().HaveCount(3, "every mode is counted, including the one that bills nothing");
    }

    /// <summary>One priced run on a named billing mode.</summary>
    /// <param name="aMode">The billing mode.</param>
    /// <param name="aCost">What the provider reported.</param>
    /// <returns>The record.</returns>
    private static RunRecord Priced(string aMode, decimal aCost) =>
        Run("claude-opus-5", aTokensOut: 1_000) with { Cmd = "build-phase", BillingMode = aMode, CostUsd = aCost };

    /// <summary>One run with a measured token window.</summary>
    /// <param name="aModel">The dominant model label, or <c>null</c>.</param>
    /// <param name="aTokensIn">Input tokens.</param>
    /// <param name="aTokensOut">Output tokens.</param>
    /// <param name="aCacheRead">Cache-read tokens.</param>
    /// <param name="aCacheWrite">Cache-write tokens.</param>
    /// <returns>The record.</returns>
    private static RunRecord Run(
        string? aModel,
        int aTokensIn = 0,
        int aTokensOut = 0,
        int aCacheRead = 0,
        int aCacheWrite = 0) => new()
    {
        UserId = 7,
        Repo = "acme/alpha",
        SourceSha = "fixture",
        Ts = "2026-09-01T10:00:00Z",
        Started = "2026-09-01T10:00:00Z",
        Ended = "2026-09-01T10:30:00Z",
        Cmd = "build-phase",
        TokensScope = "main",
        Model = aModel,
        TokensIn = aTokensIn,
        TokensOut = aTokensOut,
        TokensCacheRead = aCacheRead,
        TokensCacheWrite = aCacheWrite
    };
}
