using System.Globalization;

namespace TfLens.Services.Ui;

/// <summary>
/// Renders a timestamp as the short relative phrase the header badge and the repo grid use.
/// </summary>
/// <remarks>
/// The wording is fixed by the mockups ("synced 12 min ago"), so it lives in one place rather than being
/// re-invented per screen. Everything TfLens stores is ISO-8601 UTC, so parsing is round-trip parsing.
/// </remarks>
public static class RelativeTime
{
    /// <summary>
    /// Parses an ISO-8601 timestamp as stored by the sync bookkeeping.
    /// </summary>
    /// <param name="aTimestamp">The stored value, or <c>null</c>.</param>
    /// <returns>The instant, or <c>null</c> when the value is missing or unparseable.</returns>
    public static DateTimeOffset? Parse(string? aTimestamp)
    {
        if (string.IsNullOrWhiteSpace(aTimestamp))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            aTimestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var vWhen)
            ? vWhen
            : null;
    }

    /// <summary>
    /// Describes how long ago an instant was, in the shell's wording.
    /// </summary>
    /// <param name="aWhen">The instant, or <c>null</c> when it never happened.</param>
    /// <param name="aNow">The instant to measure from — passed in so the phrasing is testable.</param>
    /// <returns>A phrase such as <c>12 min ago</c>, or <c>never</c> when there is no instant.</returns>
    public static string Describe(DateTimeOffset? aWhen, DateTimeOffset aNow)
    {
        if (aWhen is null)
        {
            return "never";
        }

        var vElapsed = aNow - aWhen.Value;

        if (vElapsed < TimeSpan.Zero)
        {
            vElapsed = TimeSpan.Zero;
        }

        if (vElapsed.TotalSeconds < 60)
        {
            return "just now";
        }

        if (vElapsed.TotalMinutes < 60)
        {
            return $"{(int)vElapsed.TotalMinutes} min ago";
        }

        if (vElapsed.TotalHours < 24)
        {
            var vHours = (int)vElapsed.TotalHours;
            return vHours == 1 ? "1 hour ago" : $"{vHours} hours ago";
        }

        var vDays = (int)vElapsed.TotalDays;
        return vDays == 1 ? "1 day ago" : $"{vDays} days ago";
    }

    /// <summary>
    /// Builds the header's last-sync badge text (REQ-UI-007).
    /// </summary>
    /// <param name="aWhen">The newest successful sync, or <c>null</c> when nothing has synced.</param>
    /// <param name="aNow">The instant to measure from.</param>
    /// <returns><c>synced 12 min ago</c>, or <c>never synced</c>.</returns>
    public static string SyncBadge(DateTimeOffset? aWhen, DateTimeOffset aNow) =>
        aWhen is null ? "never synced" : $"synced {Describe(aWhen, aNow)}";
}
