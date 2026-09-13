using FluentAssertions;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// The price-provider book — where a rate came from, and how current it is (REQ-FN-139).
/// </summary>
public sealed class PriceProviderTests
{
    /// <summary>
    /// A rate the endpoint declines to publish is skipped, never stored and never coerced to zero.
    /// </summary>
    /// <remarks>
    /// OpenRouter publishes <c>-1</c> for a model whose price is variable or not disclosed. It is the
    /// endpoint saying it cannot answer — not saying the model is cheap. Storing it poisons the card
    /// with a negative rate; coercing it to zero is worse, because every run on that model would then
    /// report as free and nothing on the page would look wrong. Found by running the real refresh
    /// against the real endpoint, which returned exactly this for <c>openrouter/auto-beta</c>.
    /// </remarks>
    [Fact]
    public void AnUndisclosedRateIsSkippedRatherThanStoredOrZeroed()
    {
        var vModels = PriceProviders.ReadOpenRouter(
            """
            {"data":[
              {"id":"vendor/priced","pricing":{"prompt":"0.000001","completion":"0.000002"}},
              {"id":"vendor/auto-beta","pricing":{"prompt":"-1","completion":"-1"}}
            ]}
            """);

        vModels.Should().ContainSingle().Which.Model.Should().Be("vendor/priced");
    }

    /// <summary>Per-token prices are converted to the per-million unit every other rate uses.</summary>
    [Fact]
    public void PerTokenPricesBecomePerMillion()
    {
        var vModel = PriceProviders.ReadOpenRouter(
            """{"data":[{"id":"vendor/m","pricing":{"prompt":"0.000005","completion":"0.000025"}}]}""")
            .Should().ContainSingle().Subject;

        vModel.InputPerMillion.Should().Be(5m);
        vModel.OutputPerMillion.Should().Be(25m);
    }

    /// <summary>A model with no readable price at all is skipped.</summary>
    [Fact]
    public void AModelWithNoReadablePriceIsSkipped()
    {
        PriceProviders.ReadOpenRouter("""{"data":[{"id":"vendor/m","pricing":{}}]}""")
            .Should().BeEmpty();
    }

    /// <summary>The book round-trips through its file format.</summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheBookRoundTripsThroughItsFile()
    {
        var vPath = Path.Combine(Path.GetTempPath(), "tflens-tests", Guid.NewGuid().ToString("N"), "providers.json");

        try
        {
            await PriceProviders.SaveAsync(vPath, PriceProviders.Seed());

            var vRead = await PriceProviders.LoadAsync(vPath);

            vRead.Select(aP => aP.Id).Should().Equal(PriceProviders.Seed().Select(aP => aP.Id));
            vRead.Single(aP => aP.Id == "openrouter").Fetch.Should().Be(PriceFetch.Api);
            vRead.Single(aP => aP.Id == "anthropic").Models
                .Should().Contain(aM => aM.Model == "claude-opus-5" && aM.OutputPerMillion == 25m);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(vPath)!, recursive: true);
        }
    }

    /// <summary>
    /// Superseded models keep their rate, so a past phase never becomes cheaper than it was.
    /// </summary>
    /// <remarks>
    /// A record naming an older model is history and still has to report what it cost. Dropping the
    /// rate would move those runs silently into <c>list_usd_unpriced_n</c>, and the phase they belong
    /// to would read as costing less than it did.
    /// </remarks>
    [Fact]
    public void SupersededModelsKeepTheirRate()
    {
        var vAnthropic = PriceProviders.Seed().Single(aP => aP.Id == "anthropic");

        vAnthropic.Models.Select(aM => aM.Model).Should()
            .Contain(["claude-opus-4-8", "claude-opus-4-7", "claude-opus-4-6", "claude-sonnet-4-6"]);
    }

    /// <summary>
    /// Where two providers price the same model the first wins, and the clash is reported.
    /// </summary>
    /// <remarks>
    /// Never averaged: the mean of two published rates is a number nobody publishes, and a figure built
    /// on it could not be defended by pointing at either provider's page.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ADuplicateModelIsReportedRatherThanAveraged()
    {
        var vFolder = Path.Combine(Path.GetTempPath(), "tflens-tests", Guid.NewGuid().ToString("N"));
        var vPrices = Path.Combine(vFolder, "prices.json");

        var vProviders = new List<PriceProvider>
        {
            new("first", "First", null, null, PriceFetch.Manual, null,
                [new PriceProviderModel("shared-model", 1m, 2m, 0m, 0m)]),
            new("second", "Second", null, null, PriceFetch.Manual, null,
                [new PriceProviderModel("shared-model", 9m, 9m, 0m, 0m)])
        };

        try
        {
            PriceProviders.Clashes(vProviders).Should().Equal("shared-model");

            var vCard = await PriceProviders.ApplyAsync(vPrices, vProviders);

            vCard.Find("shared-model")!.OutputPerMillion.Should().Be(2m, "the first provider wins outright");
        }
        finally
        {
            Directory.Delete(vFolder, recursive: true);
        }
    }
}
