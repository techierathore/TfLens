using System.Globalization;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using TfLens.Core;
using TfLens.Core.Abstractions;

namespace TfLens.Configuration;

/// <summary>
/// The anonymous <c>/healthz</c> endpoint.
/// </summary>
/// <remarks>
/// BRD-78 fixes what this may say: database reachability and the age of the last successful sync —
/// <b>nothing else</b>. No version, no configuration, no repository name, no user data and no metric.
/// A monitoring probe is an unauthenticated caller, so every extra fact here is a fact given away.
/// </remarks>
public static class HealthEndpoint
{
    /// <summary>
    /// Reads the newest successful sync timestamp across every user.
    /// </summary>
    /// <remarks>
    /// This is deliberately not a method on <c>ITelemetryStore</c>. Every method there takes
    /// <c>userId</c> as a mandatory parameter (ADR-013); an unscoped read sitting among them is the
    /// exact shape that invites the next cross-user leak. The age is a whole-installation fact, so it
    /// is read here and nowhere else — recorded as D-009 in <c>DECISIONS.md</c>.
    /// </remarks>
    private const string LastSyncSql =
        """
        SELECT "LastSyncTs"
        FROM "SyncState"
        WHERE "LastError" IS NULL AND "LastSyncTs" IS NOT NULL
        """;

    /// <summary>
    /// Maps <c>GET /healthz</c>.
    /// </summary>
    /// <param name="aApp">The web application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapHealthEndpoint(this WebApplication aApp)
    {
        aApp.MapGet("/healthz", async (
            ITelemetryStore aStore,
            IOptions<TfLensOptions> aOptions,
            CancellationToken aCancellationToken) =>
        {
            var vDatabaseUp = await aStore.PingAsync(aCancellationToken);

            if (!vDatabaseUp)
            {
                return Results.Json(
                    new { status = "unhealthy", database = "down", lastSuccessfulSyncAgeSeconds = (long?)null },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var vAgeSeconds = await ReadLastSyncAgeSecondsAsync(aOptions.Value, aCancellationToken);

            return Results.Ok(new
            {
                status = "ok",
                database = "up",
                lastSuccessfulSyncAgeSeconds = vAgeSeconds
            });
        }).AllowAnonymous();

        return aApp;
    }

    /// <summary>
    /// Computes how long ago the most recent error-free sync finished.
    /// </summary>
    /// <param name="aOptions">The bound options, carrying the connection string.</param>
    /// <param name="aCancellationToken">Cancels the call.</param>
    /// <returns>The age in whole seconds, or <c>null</c> when no repository has ever synced cleanly.</returns>
    /// <remarks>
    /// Any failure degrades to <c>null</c>. A health probe must never turn a bookkeeping problem into
    /// a 500, and it must never carry an exception message — that is a channel for the connection
    /// string to escape (BRD-10).
    /// </remarks>
    private static async Task<long?> ReadLastSyncAgeSecondsAsync(
        TfLensOptions aOptions,
        CancellationToken aCancellationToken)
    {
        try
        {
            await using var vConnection = new NpgsqlConnection(aOptions.DbConnection);
            await vConnection.OpenAsync(aCancellationToken);

            var vStamps = await vConnection.QueryAsync<string?>(
                new CommandDefinition(LastSyncSql, cancellationToken: aCancellationToken));

            DateTimeOffset? vNewest = null;

            foreach (var vStamp in vStamps)
            {
                if (string.IsNullOrWhiteSpace(vStamp))
                {
                    continue;
                }

                if (!DateTimeOffset.TryParse(
                        vStamp,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var vParsed))
                {
                    continue;
                }

                if (vNewest is null || vParsed > vNewest)
                {
                    vNewest = vParsed;
                }
            }

            if (vNewest is null)
            {
                return null;
            }

            var vAge = (long)Math.Max(0, (DateTimeOffset.UtcNow - vNewest.Value).TotalSeconds);
            return vAge;
        }
        catch (Exception)
        {
            // Deliberately swallowed and unlogged at this level: the probe reports "unknown", and the
            // real fault surfaces through the ping above or the sync log.
            return null;
        }
    }
}
