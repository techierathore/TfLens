using TfLens.Core.Abstractions;

namespace TfLens.Services.Commands;

/// <summary>
/// Runs the command verbs the single executable also serves the UI from.
/// </summary>
/// <remarks>
/// ADR-005 — <c>dotnet TfLens.dll rebuild|sync|export</c> shares the engine the pages use, so a parity
/// run exercises production code rather than a second implementation. Each verb writes a human-readable
/// report to stdout and returns a process exit code.
/// </remarks>
public static class CommandRunner
{
    /// <summary>The <c>rebuild</c> verb — drops the tables and replays <c>data/raw/</c>.</summary>
    public const string Rebuild = "rebuild";

    /// <summary>The <c>sync</c> verb — runs one poll pass over the connected repositories.</summary>
    public const string Sync = "sync";

    /// <summary>The <c>export</c> verb — writes the snapshot pair for a user and framework.</summary>
    public const string Export = "export";

    private static readonly string[] Verbs = [Rebuild, Sync, Export];

    /// <summary>
    /// Tells whether the first argument names a command verb rather than a host switch.
    /// </summary>
    /// <param name="aArgument">The first command-line argument.</param>
    /// <returns><c>true</c> when the process should run a verb and exit instead of serving.</returns>
    public static bool IsVerb(string aArgument) =>
        Verbs.Contains(aArgument, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs one verb.
    /// </summary>
    /// <param name="aServices">The built application's service provider.</param>
    /// <param name="aArgs">The full command line; <c>aArgs[0]</c> is the verb.</param>
    /// <returns>Zero on success, non-zero when the verb failed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The verb is not recognised — callers gate on <see cref="IsVerb"/>.</exception>
    public static async Task<int> RunAsync(IServiceProvider aServices, string[] aArgs)
    {
        await using var vScope = aServices.CreateAsyncScope();
        var vVerb = aArgs[0].ToLowerInvariant();

        return vVerb switch
        {
            Rebuild => await RunRebuildAsync(vScope.ServiceProvider, aArgs),
            Sync => await RunSyncAsync(vScope.ServiceProvider, aArgs),
            Export => await RunExportAsync(vScope.ServiceProvider, aArgs),
            _ => throw new ArgumentOutOfRangeException(nameof(aArgs), vVerb, "Unknown verb.")
        };
    }

    /// <summary>
    /// Drops every stream table and replays the raw archive.
    /// </summary>
    /// <param name="aServices">A scoped service provider.</param>
    /// <param name="aArgs">The command line; <c>--user &lt;id&gt;</c> narrows the rebuild to one user.</param>
    /// <returns>Zero on success.</returns>
    private static async Task<int> RunRebuildAsync(IServiceProvider aServices, string[] aArgs)
    {
        var vStore = aServices.GetRequiredService<ITelemetryStore>();
        var vUserId = ReadUserId(aArgs);

        var vReport = await vStore.RebuildAsync(vUserId);

        Console.WriteLine(
            $"rebuild: {vReport.FilesReplayed} files replayed, {vReport.RecordsWritten} records written, " +
            $"{vReport.DuplicatesCollapsed} duplicates collapsed, {vReport.InvalidLines} invalid lines skipped " +
            $"({vReport.StartedTs} → {vReport.EndedTs})");

        return 0;
    }

    /// <summary>
    /// Runs one sync pass.
    /// </summary>
    /// <param name="aServices">A scoped service provider.</param>
    /// <param name="aArgs">The command line; <c>--user &lt;id&gt;</c> narrows the pass to one user.</param>
    /// <returns>Zero when every repository was skipped or updated, 1 when any failed.</returns>
    private static async Task<int> RunSyncAsync(IServiceProvider aServices, string[] aArgs)
    {
        var vSync = aServices.GetService<IRepoSyncRunner>();
        if (vSync is null)
        {
            Console.Error.WriteLine("sync: the sync service is not registered.");
            return 1;
        }

        var vReport = await vSync.SyncAsync(ReadUserId(aArgs));

        foreach (var vResult in vReport.Results)
        {
            Console.WriteLine($"  {vResult.Repo,-40} {vResult.Outcome,-8} {vResult.Error ?? string.Empty}");
        }

        Console.WriteLine(
            $"sync: {vReport.UpdatedCount} updated, {vReport.SkippedCount} skipped, {vReport.ErrorCount} errors");

        return vReport.ErrorCount == 0 ? 0 : 1;
    }

    /// <summary>
    /// Writes the snapshot pair — one snapshot per framework (REQ-FN-070).
    /// </summary>
    /// <remarks>
    /// With no <c>--framework</c> the verb writes <b>every</b> framework's snapshot rather than
    /// defaulting to TechieFlow. A snapshot is per user, per date, per framework (REQ-FN-056), and a
    /// default that silently produced only one of them was the reason a user with Playbook repositories
    /// ended the run with no <c>playbook/</c> folder on disk at all. Naming a framework still writes
    /// exactly that one.
    /// </remarks>
    /// <param name="aServices">A scoped service provider.</param>
    /// <param name="aArgs">The command line; <c>--user &lt;id&gt;</c> and optional <c>--framework &lt;name&gt;</c>.</param>
    /// <returns>Zero on success, 1 when no user was named or the framework is not recognised.</returns>
    private static async Task<int> RunExportAsync(IServiceProvider aServices, string[] aArgs)
    {
        var vExporter = aServices.GetRequiredService<ISnapshotExporter>();
        var vUserId = ReadUserId(aArgs);

        if (vUserId is null)
        {
            Console.Error.WriteLine("export: --user <id> is required.");
            return 1;
        }

        var vNamed = ReadOption(aArgs, "--framework");
        if (vNamed is not null && !Core.Contracts.FrameworkNames.All.Contains(vNamed, StringComparer.Ordinal))
        {
            Console.Error.WriteLine(
                $"export: unknown framework '{vNamed}'; expected one of "
                + string.Join(", ", Core.Contracts.FrameworkNames.All) + ".");
            return 1;
        }

        IReadOnlyList<string> vFrameworks = vNamed is null ? Core.Contracts.FrameworkNames.All : [vNamed];
        var vDate = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var vFramework in vFrameworks)
        {
            var vResult = await vExporter.ExportAsync(vUserId.Value, vFramework, vDate);

            Console.WriteLine($"export: {vResult.MarkdownPath}");
            Console.WriteLine($"export: {vResult.JsonPath}");
            Console.WriteLine(
                $"export: framework {vResult.Framework}, parser {vResult.ParserVersion}, parity {vResult.ParityStatus}");
        }

        return 0;
    }

    /// <summary>Reads <c>--user &lt;id&gt;</c> from the command line.</summary>
    /// <param name="aArgs">The command line.</param>
    /// <returns>The user id, or <c>null</c> when the switch is absent or unparseable.</returns>
    private static int? ReadUserId(string[] aArgs) =>
        int.TryParse(ReadOption(aArgs, "--user"), out var vId) ? vId : null;

    /// <summary>Reads the value that follows a named switch.</summary>
    /// <param name="aArgs">The command line.</param>
    /// <param name="aName">The switch, including its dashes.</param>
    /// <returns>The following argument, or <c>null</c>.</returns>
    private static string? ReadOption(string[] aArgs, string aName)
    {
        var vIndex = Array.FindIndex(aArgs, aA => string.Equals(aA, aName, StringComparison.OrdinalIgnoreCase));
        return vIndex >= 0 && vIndex + 1 < aArgs.Length ? aArgs[vIndex + 1] : null;
    }
}
