using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Abstractions;

namespace TfLens.Services.Sync;

/// <summary>
/// The background poller: every <see cref="TfLensOptions.PollIntervalMinutes"/> it syncs every
/// connected repository of every user.
/// </summary>
/// <remarks>
/// <para>
/// BRD-12 / REQ-FN-020 — the service starts with the host and stops cleanly on the host's cancellation
/// token. A <see cref="PeriodicTimer"/> drives it and the pass is awaited before the next tick is
/// waited for, so an overrunning tick cannot stack with the one behind it.
/// </para>
/// <para>
/// A pass that throws is logged and swallowed: the poller must keep ticking, because a transient
/// GitHub or database outage is exactly the condition the next tick is meant to recover from.
/// </para>
/// </remarks>
public sealed class RepoSyncService : BackgroundService
{
    /// <summary>The shortest interval the poller will run at, whatever the configuration says.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider objServices;
    private readonly TfLensOptions objOptions;
    private readonly ILogger<RepoSyncService> objLogger;

    /// <summary>
    /// Creates the poller.
    /// </summary>
    /// <param name="aServices">The root provider; the runner is a singleton and opens its own per-pass scope.</param>
    /// <param name="aOptions">TfLens configuration, read for the poll interval.</param>
    /// <param name="aLogger">Logger; it records counts and durations only.</param>
    public RepoSyncService(
        IServiceProvider aServices,
        IOptions<TfLensOptions> aOptions,
        ILogger<RepoSyncService> aLogger)
    {
        objServices = aServices;
        objOptions = aOptions.Value;
        objLogger = aLogger;
    }

    /// <summary>
    /// Resolves the poll interval from configuration.
    /// </summary>
    /// <remarks>
    /// A zero or negative setting would spin the timer, so the interval is floored at
    /// <see cref="MinimumInterval"/> rather than trusted blindly.
    /// </remarks>
    /// <param name="aOptions">TfLens configuration.</param>
    /// <returns>The interval the <see cref="PeriodicTimer"/> runs at.</returns>
    public static TimeSpan ResolveInterval(TfLensOptions aOptions)
    {
        var vInterval = TimeSpan.FromMinutes(aOptions.PollIntervalMinutes);
        return vInterval < MinimumInterval ? MinimumInterval : vInterval;
    }

    /// <summary>
    /// Runs one pass over every user's repositories, absorbing whatever it throws.
    /// </summary>
    /// <remarks>The poller must survive a failed pass and keep ticking (REQ-FN-020).</remarks>
    /// <param name="aCancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes when the pass has been attempted.</returns>
    public async Task RunPassAsync(CancellationToken aCancellationToken)
    {
        try
        {
            var vRunner = objServices.GetService<IRepoSyncRunner>();

            if (vRunner is null)
            {
                objLogger.LogWarning("The sync runner is not registered; the poller has nothing to do.");
                return;
            }

            // BRD-103: the poller's pass covers every user; Sync now passes a user id instead.
            var vReport = await vRunner.SyncAsync(null, aCancellationToken).ConfigureAwait(false);

            objLogger.LogInformation(
                "Poll pass complete: {Updated} updated, {Skipped} skipped, {Errors} errors",
                vReport.UpdatedCount,
                vReport.SkippedCount,
                vReport.ErrorCount);
        }
        catch (OperationCanceledException) when (aCancellationToken.IsCancellationRequested)
        {
            objLogger.LogInformation("Poll pass cancelled during shutdown.");
        }
        catch (Exception vEx)
        {
            objLogger.LogError(vEx, "Poll pass failed; the poller will try again on the next tick.");
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken aStoppingToken)
    {
        var vInterval = ResolveInterval(objOptions);

        objLogger.LogInformation("Repository poller started; interval {Minutes} minutes", vInterval.TotalMinutes);

        using var vTimer = new PeriodicTimer(vInterval);

        try
        {
            while (await vTimer.WaitForNextTickAsync(aStoppingToken).ConfigureAwait(false))
            {
                await RunPassAsync(aStoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down; there is nothing to report.
        }

        objLogger.LogInformation("Repository poller stopped.");
    }
}
