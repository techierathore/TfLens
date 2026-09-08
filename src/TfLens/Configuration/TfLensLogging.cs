using Serilog;
using Serilog.Core;
using TfLens.Services.Auth;

namespace TfLens.Configuration;

/// <summary>
/// Builds the one Serilog pipeline the whole process logs through.
/// </summary>
/// <remarks>
/// <para>
/// Two sinks are unconditional: console — what <c>docker logs tflens</c> shows — and a rolling file
/// under <c>logs/</c>, which is a bind mount in deployment so a crash loop leaves evidence behind on
/// the server. A third sink, Seq, is added <b>only</b> when a URL is configured. That is what keeps
/// local development free of any logging setup: nothing is configured, so nothing is registered and
/// nothing warns about a Seq server that was never meant to exist on a developer's machine.
/// </para>
/// <para>
/// The Seq sink must never become a startup dependency. Serilog's Seq sink batches in memory and
/// retries on its own, so a Seq server that is down, slow, or simply started after TfLens costs the
/// app nothing at boot — see the deployment brief §0, "the app must start normally".
/// </para>
/// <para>
/// The reset-token redaction is attached to the <b>logger</b> rather than to any middleware, because
/// the line that once leaked a live password-reset link came from ASP.NET Core's own hosting
/// diagnostics, which logs the whole request URL before any TfLens middleware runs (BRD-92). Building
/// every sink here means Seq inherits that redaction too — and a reset link that reached Seq would be
/// both searchable and retained.
/// </para>
/// </remarks>
public static class TfLensLogging
{
    /// <summary>Configuration key holding the Seq ingestion URL (compose: <c>Seq__Url</c>).</summary>
    /// <remarks>
    /// Deliberately outside the <c>TfLens:</c> section and its PascalCase environment provider. Seq is
    /// infrastructure shared by every app on the VPS, and the deployment brief fixes the spelling of
    /// these two keys across the whole portfolio so one compose template shape serves all of them.
    /// </remarks>
    public const string SeqUrlKey = "Seq:Url";

    /// <summary>Configuration key holding this app's Seq API key (compose: <c>Seq__ApiKey</c>).</summary>
    public const string SeqApiKeyKey = "Seq:ApiKey";

    /// <summary>The value stamped on every event as the <c>App</c> property.</summary>
    /// <remarks>
    /// This is the infrastructure identity from the deployment brief §3 (<c>APP_NAME</c>), not the C#
    /// project name — Seq, the container, the image and the Caddy snippet all use this one spelling.
    /// The per-app Seq API key stamps the same property server-side, so events stay attributable even
    /// if this enrichment is ever lost.
    /// </remarks>
    public const string AppName = "tflens";

    /// <summary>
    /// Creates the logger.
    /// </summary>
    /// <param name="aSeqUrl">Seq ingestion URL, or <c>null</c>/empty to omit the Seq sink entirely.</param>
    /// <param name="aSeqApiKey">The per-app Seq API key; ignored when no URL is supplied.</param>
    /// <returns>A configured logger. The caller owns it and disposes it via <c>Log.CloseAndFlush</c>.</returns>
    public static Logger Create(string? aSeqUrl = null, string? aSeqApiKey = null)
    {
        var vConfiguration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.With(new ResetTokenRedaction())
            .Enrich.WithProperty("App", AppName)
            .WriteTo.Console()
            .WriteTo.File("logs/tflens-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);

        if (!string.IsNullOrWhiteSpace(aSeqUrl))
        {
            vConfiguration = vConfiguration.WriteTo.Seq(
                aSeqUrl,
                apiKey: string.IsNullOrWhiteSpace(aSeqApiKey) ? null : aSeqApiKey);
        }

        return vConfiguration.CreateLogger();
    }
}
