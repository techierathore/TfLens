using FluentAssertions;
using TfLens.Core.Metrics;
using TfLens.Services.Ui;

namespace TfLens.Core.Tests.Ui;

/// <summary>
/// The pager and filter behind each provider's rate table on the Price providers screen (REQ-UI-072).
/// </summary>
public sealed class PriceRatePageTests
{
    /// <summary>Ten rates: two pages of five, in model-id order.</summary>
    private static readonly IReadOnlyList<PriceProviderModel> Ten =
        [.. Enumerable.Range(0, 10).Reverse().Select(aI => Rate($"model-{aI:00}"))];

    /// <summary>
    /// A provider with more rates than a page shows the first five, in model-id order, with a pager.
    /// </summary>
    [Fact]
    public void FirstPageHoldsFiveRatesInModelOrder()
    {
        var vPage = PriceRatePage.Of(Ten, null, 0);

        vPage.Rows.Select(aRow => aRow.Model).Should().Equal("model-00", "model-01", "model-02", "model-03", "model-04");
    }

    /// <summary>The footer says how many of how many are shown, and which page it is.</summary>
    [Fact]
    public void SummaryCountsTheRatesShown()
    {
        PriceRatePage.Of(Ten, null, 0).Summary.Should().Be("5 of 10 rates shown · page 1 of 2");
    }

    /// <summary>On the first page there is no page before it, and on the last none after it.</summary>
    [Fact]
    public void PagerStopsAtBothEnds()
    {
        var vFirst = PriceRatePage.Of(Ten, null, 0);
        var vLast = PriceRatePage.Of(Ten, null, 1);

        (vFirst.CanGoBack, vFirst.CanGoForward, vLast.CanGoBack, vLast.CanGoForward)
            .Should().Be((false, true, true, false));
    }

    /// <summary>A page asked for past the end shows the last page rather than an empty table.</summary>
    [Fact]
    public void PageBeyondTheEndIsClampedToTheLast()
    {
        PriceRatePage.Of(Ten, null, 9).PageIndex.Should().Be(1);
    }

    /// <summary>
    /// A provider whose rates all fit on one page has no pager and nothing to count, so the footer
    /// carries the unit alone.
    /// </summary>
    [Fact]
    public void ShortProviderHasNoPagerAndNoSummary()
    {
        var vPage = PriceRatePage.Of([Rate("gpt-5.6-sol")], null, 0);

        (vPage.HasPager, vPage.Summary).Should().Be((false, (string?)null));
    }

    /// <summary>The filter keeps the model ids that contain the text, ignoring case.</summary>
    [Fact]
    public void FilterMatchesModelIdsIgnoringCase()
    {
        var vPage = PriceRatePage.Of([Rate("anthropic/claude-fable-5"), Rate("openai/gpt-5"), Rate("Anthropic/Claude-Opus")], "CLAUDE", 0);

        vPage.Rows.Select(aRow => aRow.Model).Should().Equal("Anthropic/Claude-Opus", "anthropic/claude-fable-5");
    }

    /// <summary>A filtered count says it was filtered, and from how many.</summary>
    [Fact]
    public void FilteredSummaryNamesTheWholeCount()
    {
        PriceRatePage.Of(Ten, "model-03", 0).Summary.Should().Be("1 of 1 rate shown (filtered from 10)");
    }

    /// <summary>A filter that matches nothing yields no rows and one page, never a negative index.</summary>
    [Fact]
    public void FilterMatchingNothingIsAnEmptyFirstPage()
    {
        var vPage = PriceRatePage.Of(Ten, "no-such-model", 3);

        (vPage.Rows.Count, vPage.PageIndex, vPage.PageCount, vPage.MatchCount).Should().Be((0, 0, 1, 0));
    }

    /// <summary>Only a long provider — more than twenty rates — is given a filter.</summary>
    [Fact]
    public void OnlyALongProviderGetsAFilter()
    {
        (PriceRatePage.NeedsFilter(10), PriceRatePage.NeedsFilter(21), PriceRatePage.NeedsFilter(434))
            .Should().Be((false, true, true));
    }

    /// <summary>A rate just saved is shown on the page where its model id sorts.</summary>
    [Fact]
    public void PageOfFindsWhereAModelSorts()
    {
        (PriceRatePage.PageOf(Ten, "model-07"), PriceRatePage.PageOf(Ten, "MODEL-02"), PriceRatePage.PageOf(Ten, "absent"))
            .Should().Be((1, 0, 0));
    }

    /// <summary>A rate line for a model, the figures immaterial to paging.</summary>
    /// <param name="aModel">The model id.</param>
    /// <returns>The rate line.</returns>
    private static PriceProviderModel Rate(string aModel) => new(aModel, 1m, 2m, 0.1m, 0m);
}
