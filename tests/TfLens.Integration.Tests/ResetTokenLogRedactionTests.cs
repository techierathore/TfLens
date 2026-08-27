using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TfLens.Services.Auth;

namespace TfLens.Integration.Tests;

/// <summary>
/// REQ-FN-003 / BRD-92 — the reset token never reaches a log sink, whoever wrote the line.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the token was being logged, twice per visit, and no amount of care in TfLens's
/// own code would have stopped it. The token travels in the query string of the emailed link, and
/// ASP.NET Core's hosting diagnostics logs every request URL — so <c>Request starting … GET
/// /reset-password?token=…</c> and its <c>Request finished</c> partner both carried a live
/// password-reset link into the console and into the fourteen-day rolling log file.
/// </para>
/// <para>
/// The tests therefore drive the real Serilog pipeline with the real message templates the framework
/// emits, not the enricher in isolation: what matters is the text a sink finally receives.
/// </para>
/// </remarks>
public sealed class ResetTokenLogRedactionTests
{
    /// <summary>The template ASP.NET Core's hosting diagnostics logs every request with.</summary>
    private const string HostingTemplate =
        "Request finished {Protocol} {Method} {Scheme}://{Host}{PathBase}{Path}{QueryString} - {StatusCode}";

    /// <summary>A token distinctive enough that any survival is unmistakable.</summary>
    private const string SecretResetToken = "rst-CANARY-9f3ac71e-do-not-log";

    /// <summary>The framework's own request log cannot carry a reset token to a sink.</summary>
    [Fact]
    public void TheFrameworkRequestLogCannotCarryAResetToken()
    {
        var vSink = new CapturingSink();
        using var vLogger = BuildLogger(vSink);

        vLogger.Information(
            HostingTemplate,
            "HTTP/1.1",
            "GET",
            "http",
            "localhost:5105",
            string.Empty,
            "/reset-password",
            $"?token={SecretResetToken}",
            200);

        vSink.Rendered.Should().NotContain(SecretResetToken);
        vSink.Rendered.Should().Contain("/reset-password");
        vSink.Rendered.Should().Contain(ResetTokenRedaction.Placeholder);
        vSink.Rendered.Should().Contain("200", "redaction must not cost the line its usefulness");
    }

    /// <summary>Redaction survives the token sharing a query string with other parameters.</summary>
    /// <param name="aQueryString">The query string as it reaches the log.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("?token=rst-CANARY-9f3ac71e-do-not-log")]
    [InlineData("?token=rst-CANARY-9f3ac71e-do-not-log&error=expired")]
    [InlineData("?error=expired&token=rst-CANARY-9f3ac71e-do-not-log")]
    [InlineData("?returnUrl=%2Frepos&token=rst-CANARY-9f3ac71e-do-not-log&sent=1")]
    public void RedactionSurvivesEveryQueryStringShape(string aQueryString)
    {
        var vSink = new CapturingSink();
        using var vLogger = BuildLogger(vSink);

        vLogger.Information("Request starting {Method} {Path}{QueryString}", "GET", "/reset-password", aQueryString);

        vSink.Rendered.Should().NotContain(SecretResetToken);
        vSink.Rendered.Should().Contain("/reset-password");
    }

    /// <summary>An exception message that picked the link up on its way is redacted too.</summary>
    /// <remarks>
    /// A <c>NavigationException</c> or an <c>HttpRequestException</c> renders the URL it was given, and
    /// an exception reaches the sink through a different route from the message template.
    /// </remarks>
    [Fact]
    public void ARedactedLineCoversAPropertyCarryingAWholeUrl()
    {
        var vSink = new CapturingSink();
        using var vLogger = BuildLogger(vSink);

        vLogger.Warning(
            "Navigation failed for {Uri}",
            $"https://tflens.example/reset-password?token={SecretResetToken}");

        vSink.Rendered.Should().NotContain(SecretResetToken);
        vSink.Rendered.Should().Contain("tflens.example/reset-password");
    }

    /// <summary>Redaction touches the reset token and leaves everything else alone.</summary>
    /// <param name="aText">The text to pass through redaction.</param>
    /// <remarks>
    /// Over-redacting is its own failure: a rule that blanks anything token-shaped hides the refresh
    /// and antiforgery tokens too, and those have their own guards whose whole value is that they fail
    /// loudly. This pins the blast radius to the one parameter.
    /// </remarks>
    [Theory]
    [InlineData("/repos?page=2")]
    [InlineData("/login?returnUrl=%2Fcoverage")]
    [InlineData("/reset-password")]
    [InlineData("?refreshToken=rt-abc123")]
    [InlineData("?antiforgeryToken=CfDJ8abc")]
    public void RedactionLeavesEverythingElseUntouched(string aText)
    {
        ResetTokenRedaction.Redact(aText).Should().Be(aText);
    }

    /// <summary>
    /// Builds a logger with the production redaction attached and one capturing sink.
    /// </summary>
    /// <param name="aSink">The sink to capture with.</param>
    /// <returns>The logger under test.</returns>
    private static Logger BuildLogger(CapturingSink aSink) => new LoggerConfiguration()
        .MinimumLevel.Verbose()
        .Enrich.With(new ResetTokenRedaction())
        .WriteTo.Sink(aSink)
        .CreateLogger();

    /// <summary>A sink that keeps the rendered text of everything written to it.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<string> objLines = [];

        /// <summary>Everything the sink received, joined into one searchable document.</summary>
        public string Rendered => string.Join('\n', objLines);

        /// <inheritdoc />
        public void Emit(LogEvent aLogEvent)
        {
            objLines.Add(aLogEvent.RenderMessage());

            // The structured properties are what a JSON sink writes, and a value can survive there
            // even when the rendered line is clean.
            objLines.AddRange(aLogEvent.Properties.Select(aProperty => $"{aProperty.Key}={aProperty.Value}"));

            if (aLogEvent.Exception is not null)
            {
                objLines.Add(aLogEvent.Exception.ToString());
            }
        }
    }
}
