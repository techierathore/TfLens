using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace TfLens.Services.Auth;

/// <summary>
/// Strips a password-reset token out of every log event before any sink can see it (BRD-92, BRD-10).
/// </summary>
/// <remarks>
/// <para>
/// TfLens's own code is careful never to log the token — but the token arrives in the query string of
/// the emailed link, and ASP.NET Core's hosting diagnostics logs every request URL, query string
/// included. The result was two <c>Information</c> lines per visit, each carrying a live
/// password-reset link, written to the console and to the fourteen-day rolling file. No amount of care
/// inside the application could have prevented it: the leak happens in framework middleware that runs
/// before anything TfLens wrote.
/// </para>
/// <para>
/// The redaction is therefore applied at the sink boundary, where every log event has to pass whatever
/// produced it. It rewrites only the value of a <c>token</c> parameter, so a path, a status code and
/// every other query parameter survive intact and the request log stays useful.
/// </para>
/// </remarks>
public sealed class ResetTokenRedaction : ILogEventEnricher
{
    /// <summary>What replaces the token's value, so a redacted line still reads as a reset request.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>A <c>token</c> query parameter and its value, wherever it appears in a string.</summary>
    /// <remarks>
    /// Anchored on the separator so <c>?token=</c> and <c>&amp;token=</c> both match while
    /// <c>refreshToken=</c> and <c>antiforgeryToken=</c> do not — those are other secrets with other
    /// guards, and silently swallowing them here would hide a leak rather than fix it.
    /// </remarks>
    private static readonly Regex TokenParameter = new(
        @"(?<=[?&])token=[^&\s""]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    /// <remarks>
    /// Every scalar string property is examined rather than a fixed list of names. The property
    /// carrying the URL is <c>QueryString</c> today, but it is <c>RequestPath</c> under Serilog's own
    /// request logging and <c>Uri</c> under others — and a redaction that only covers the shape in
    /// front of it is a redaction that quietly stops working.
    /// </remarks>
    public void Enrich(LogEvent aLogEvent, ILogEventPropertyFactory aPropertyFactory)
    {
        foreach (var vProperty in aLogEvent.Properties.ToArray())
        {
            if (vProperty.Value is not ScalarValue { Value: string vText } || !HasToken(vText))
            {
                continue;
            }

            aLogEvent.AddOrUpdateProperty(
                new LogEventProperty(vProperty.Key, new ScalarValue(Redact(vText))));
        }
    }

    /// <summary>
    /// Replaces the value of any <c>token</c> query parameter in a string.
    /// </summary>
    /// <param name="aText">The text to redact.</param>
    /// <returns>The text with every reset token replaced by the placeholder.</returns>
    public static string Redact(string aText) =>
        TokenParameter.Replace(aText, $"token={Placeholder}");

    /// <summary>
    /// Whether a string carries a <c>token</c> query parameter at all.
    /// </summary>
    /// <param name="aText">The text to test.</param>
    /// <returns><c>true</c> when redaction would change it.</returns>
    /// <remarks>
    /// A cheap ordinal test in front of the regex: this runs on every property of every log event, and
    /// the overwhelming majority carry no query string of any kind.
    /// </remarks>
    private static bool HasToken(string aText) =>
        aText.Contains("token=", StringComparison.OrdinalIgnoreCase) && TokenParameter.IsMatch(aText);
}
