using System.Globalization;
using TfLens.Core.Metrics;

namespace TfLens.Services.Ui;

/// <summary>
/// One page of one provider's rates, as the Price providers screen shows it (REQ-UI-072).
/// </summary>
/// <remarks>
/// <para>
/// OpenRouter alone publishes more than four hundred rates, so a provider's table is paged and, once it
/// is long, filtered by model id. The mockup draws the pager as a Previous and a Next button in the
/// card's footer beside a "5 of 10 rates shown" count, and the filter as a search field above the
/// table; this record is everything those controls need, worked out in one place so the page never
/// does paging arithmetic in markup.
/// </para>
/// <para>
/// Rows are ordered by model id, ordinal, so a page is the same page on every visit and a model can
/// always be found by where its name sorts.
/// </para>
/// </remarks>
/// <param name="Rows">The rates on this page, at most <see cref="PageSize"/>.</param>
/// <param name="PageIndex">Zero-based page shown, clamped into range.</param>
/// <param name="PageCount">Pages the matching rates fill; at least one.</param>
/// <param name="MatchCount">Rates that match the filter; every rate when there is none.</param>
/// <param name="TotalCount">Every rate the provider holds.</param>
/// <param name="IsFiltered">Whether a filter narrowed the rates.</param>
public sealed record PriceRatePage(
    IReadOnlyList<PriceProviderModel> Rows,
    int PageIndex,
    int PageCount,
    int MatchCount,
    int TotalCount,
    bool IsFiltered)
{
    /// <summary>Rates on one page — the mockup's page length.</summary>
    public const int PageSize = 5;

    /// <summary>A provider holding more rates than this gets a filter above its table.</summary>
    public const int FilterFrom = 20;

    /// <summary>Whether the matching rates run past one page, so Previous and Next are shown.</summary>
    public bool HasPager => MatchCount > PageSize;

    /// <summary>Whether there is a page before this one.</summary>
    public bool CanGoBack => PageIndex > 0;

    /// <summary>Whether there is a page after this one.</summary>
    public bool CanGoForward => PageIndex < PageCount - 1;

    /// <summary>
    /// The count the footer prints beside the unit, or <c>null</c> when every rate is on the page and
    /// nothing was filtered, so there is nothing to say.
    /// </summary>
    public string? Summary
    {
        get
        {
            if (!HasPager && !IsFiltered)
            {
                return null;
            }

            var vShown = string.Create(
                CultureInfo.InvariantCulture,
                $"{Rows.Count} of {MatchCount} {(MatchCount == 1 ? "rate" : "rates")} shown");

            var vMatching = IsFiltered ? $" (filtered from {TotalCount})" : string.Empty;
            var vPage = PageCount > 1
                ? string.Create(CultureInfo.InvariantCulture, $" · page {PageIndex + 1} of {PageCount}")
                : string.Empty;

            return vShown + vMatching + vPage;
        }
    }

    /// <summary>Whether a provider holding this many rates is long enough to be given a filter.</summary>
    /// <param name="aRateCount">The provider's rate count.</param>
    /// <returns><c>true</c> when the filter is shown.</returns>
    public static bool NeedsFilter(int aRateCount) => aRateCount > FilterFrom;

    /// <summary>Works out one page of a provider's rates.</summary>
    /// <param name="aModels">Every rate the provider holds.</param>
    /// <param name="aFilter">Text a model id must contain, case-insensitively; blank for none.</param>
    /// <param name="aPageIndex">The zero-based page wanted; clamped into range.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aModels"/> is <c>null</c>.</exception>
    public static PriceRatePage Of(IReadOnlyList<PriceProviderModel> aModels, string? aFilter, int aPageIndex)
    {
        ArgumentNullException.ThrowIfNull(aModels);

        var vFilter = aFilter?.Trim() ?? string.Empty;
        var vMatches = Ordered(aModels)
            .Where(aModel => vFilter.Length == 0 || aModel.Model.Contains(vFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var vPageCount = Math.Max(1, (vMatches.Count + PageSize - 1) / PageSize);
        var vIndex = Math.Clamp(aPageIndex, 0, vPageCount - 1);

        return new PriceRatePage(
            vMatches.Skip(vIndex * PageSize).Take(PageSize).ToList(),
            vIndex,
            vPageCount,
            vMatches.Count,
            aModels.Count,
            vFilter.Length > 0);
    }

    /// <summary>
    /// The unfiltered page a model sits on, so a rate just saved can be shown where it landed.
    /// </summary>
    /// <param name="aModels">Every rate the provider holds.</param>
    /// <param name="aModel">The model id.</param>
    /// <returns>The zero-based page, or 0 when the model is not held.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aModels"/> is <c>null</c>.</exception>
    public static int PageOf(IReadOnlyList<PriceProviderModel> aModels, string aModel)
    {
        ArgumentNullException.ThrowIfNull(aModels);

        var vPosition = Ordered(aModels)
            .Select(aRate => aRate.Model)
            .ToList()
            .FindIndex(aId => string.Equals(aId, aModel, StringComparison.OrdinalIgnoreCase));

        return vPosition < 0 ? 0 : vPosition / PageSize;
    }

    /// <summary>The rates in the order every page reads them.</summary>
    /// <param name="aModels">The rates.</param>
    /// <returns>The rates by model id, ordinal.</returns>
    private static IEnumerable<PriceProviderModel> Ordered(IReadOnlyList<PriceProviderModel> aModels) =>
        aModels.OrderBy(aModel => aModel.Model, StringComparer.Ordinal);
}
