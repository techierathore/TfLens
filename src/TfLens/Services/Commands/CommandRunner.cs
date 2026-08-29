using TfLens.Core.Abstractions;
using TfLens.Core.AppManager;
using TfLens.Core.Contracts;
using TfLens.Core.Provenance;

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

    /// <summary>
    /// The <c>provision-test-accounts</c> verb — restores the Usage Guide's test accounts (REQ-NFR-012).
    /// </summary>
    public const string ProvisionTestAccounts = "provision-test-accounts";

    /// <summary>
    /// The <c>provenance-check</c> verb — reports stored SHAs no ingest path obtained (REQ-NFR-019).
    /// </summary>
    public const string ProvenanceCheck = "provenance-check";

    private static readonly string[] Verbs =
        [Rebuild, Sync, Export, ProvisionTestAccounts, ProvenanceCheck];

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
            ProvisionTestAccounts => await RunProvisionTestAccountsAsync(vScope.ServiceProvider, aArgs),
            ProvenanceCheck => await RunProvenanceCheckAsync(vScope.ServiceProvider, aArgs),
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

    /// <summary>
    /// Restores the AppManager accounts the Usage Guide documents (REQ-NFR-012).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The repeatable procedure the requirement asks for. The accounts live on a shared external
    /// service, so nothing in this repository can rebuild them from a schema — but the credential the
    /// suite uses <b>is</b> in this repository, in <c>docs/TfLens-UsageGuide.md</c>, and that is enough
    /// to make the restore mechanical: for every account the guide lists, sign in with the documented
    /// password; if that fails, register the account with that password and
    /// <c>applicationRoleCode: "Manager"</c>.
    /// </para>
    /// <para>
    /// <b>Idempotent by construction.</b> An account that already works is signed into and left exactly
    /// as it was — nothing is renamed, no password is set, no role is re-applied. Registration only ever
    /// runs for an account AppManager does not have.
    /// </para>
    /// <para>
    /// <b>It prints no credential.</b> The passwords it handles come from the guide and go no further
    /// than the request body; the report carries the email, the userId and the application role only.
    /// </para>
    /// <para>
    /// One case it deliberately cannot repair on its own: an account that exists in AppManager under a
    /// <i>different</i> password. Registration then answers <c>DUPLICATE_EMAIL</c>, and the only fixes
    /// are outside this repository — reset the password through <c>/forgot-password</c>, or delete and
    /// re-create the account in the AppManager admin UI, then correct the guide. The verb says so in as
    /// many words rather than failing opaquely, because that is the exact state that blocked the suite
    /// on 2026-08-28.
    /// </para>
    /// </remarks>
    /// <param name="aServices">A scoped service provider.</param>
    /// <param name="aArgs">The command line; optional <c>--guide &lt;path&gt;</c> overrides the guide's location.</param>
    /// <returns>Zero when every documented account can sign in, 1 when any could not be restored.</returns>
    private static async Task<int> RunProvisionTestAccountsAsync(IServiceProvider aServices, string[] aArgs)
    {
        var vClient = aServices.GetRequiredService<IAppManagerClient>();
        var vGuidePath = ReadOption(aArgs, "--guide") ?? TestAccountRegistry.LocateGuide(AppContext.BaseDirectory);

        IReadOnlyList<TestAccount> vAccounts;
        try
        {
            vAccounts = TestAccountRegistry.Read(vGuidePath);
        }
        catch (Exception vReadEx) when (vReadEx is FileNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"{ProvisionTestAccounts}: {vReadEx.Message}");
            return 1;
        }

        Console.WriteLine($"{ProvisionTestAccounts}: reading {vGuidePath}");

        if (vAccounts.Count == 0)
        {
            Console.Error.WriteLine(
                $"{ProvisionTestAccounts}: the Test-users table lists no account with both an email " +
                "and a password, so there is nothing to restore.");
            return 1;
        }

        var vFailures = 0;

        foreach (var vAccount in vAccounts)
        {
            var vOutcome = await RestoreOneAccountAsync(vClient, vAccount);

            Console.WriteLine($"  {vAccount.Email,-38} {vOutcome.Summary}");

            if (!vOutcome.Restored)
            {
                vFailures++;
            }
        }

        Console.WriteLine(
            $"{ProvisionTestAccounts}: {vAccounts.Count - vFailures} of {vAccounts.Count} documented " +
            $"accounts usable.");

        return vFailures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Signs one documented account in, registering it first when AppManager does not have it.
    /// </summary>
    /// <param name="aClient">The live AppManager client.</param>
    /// <param name="aAccount">The account as the Usage Guide records it.</param>
    /// <returns>Whether the account now works, and a one-line report that carries no credential.</returns>
    private static async Task<(bool Restored, string Summary)> RestoreOneAccountAsync(
        IAppManagerClient aClient,
        TestAccount aAccount)
    {
        try
        {
            var vAuth = await aClient.LoginAsync(aAccount.Email, aAccount.Password);

            return (true, $"already usable  userId {vAuth.UserId}  applicationRole " +
                          $"'{vAuth.ApplicationRole}'");
        }
        catch (AppManagerException vLoginEx) when (vLoginEx.Code == AppManagerException.Codes.InvalidCredentials)
        {
            // Either the account does not exist, or it exists under another password. Registration is
            // what tells the two apart, and it is safe: it cannot overwrite an existing account.
        }
        catch (AppManagerException vLoginEx)
        {
            return (false, $"cannot sign in — AppManager answered {vLoginEx.Code}");
        }

        try
        {
            var vCreated = await aClient.RegisterAsync(
                new RegisterRequest(aAccount.Email, aAccount.Password, aAccount.FirstName, aAccount.LastName));

            return (true, $"registered      userId {vCreated.UserId}  applicationRole " +
                          $"'{vCreated.ApplicationRole}'");
        }
        catch (AppManagerException vRegisterEx) when (vRegisterEx.Code == AppManagerException.Codes.DuplicateEmail)
        {
            return (false,
                "EXISTS UNDER ANOTHER PASSWORD — AppManager holds this address but rejects the password " +
                "the Usage Guide records. Reset it through /forgot-password, or delete and re-create the " +
                "account in the AppManager admin UI, then correct the guide's Test-users table.");
        }
        catch (AppManagerException vRegisterEx)
        {
            return (false, $"could not be registered — AppManager answered {vRegisterEx.Code}");
        }
    }

    /// <summary>
    /// Reports every stored <c>SourceSha</c> that no sync and no import ever obtained (REQ-NFR-019).
    /// </summary>
    /// <remarks>
    /// <para>
    /// BRD-143 clause 3. The check runs entirely against the store — the provenance ledger, the
    /// <c>"SyncState"</c> SHA, the <c>"UserRepo"."BundleSha"</c> and the raw archive's file names — so it
    /// needs <b>no network call</b> and does not compare counts against GitHub by hand. That matters:
    /// the 155 fabricated rows found on 2026-08-29 were caught only because their counts happened to
    /// disagree with upstream, and a smaller forgery would have read as plausible.
    /// </para>
    /// <para>
    /// It has no <c>--fix</c>, no <c>--ignore</c> and no threshold. Deciding what to delete from a store
    /// of real telemetry is the owner's call with a backup to hand, not a verb's; this reports, and the
    /// export refuses to stamp QUOTABLE while a finding stands.
    /// </para>
    /// </remarks>
    /// <param name="aServices">A scoped service provider.</param>
    /// <param name="aArgs">The command line; <c>--user &lt;id&gt;</c> narrows the audit to one user.</param>
    /// <returns>Zero when nothing is unaccounted, 1 when any source SHA is.</returns>
    private static async Task<int> RunProvenanceCheckAsync(IServiceProvider aServices, string[] aArgs)
    {
        var vStore = aServices.GetRequiredService<ITelemetryStore>();
        var vReport = await vStore.AuditProvenanceAsync(ReadUserId(aArgs));

        foreach (var vLine in ProvenanceAudit.Describe(vReport))
        {
            Console.WriteLine(vLine);
        }

        if (!vReport.IsSupported)
        {
            Console.Error.WriteLine(
                $"{ProvenanceCheck}: the configured store cannot answer this question.");
            return 1;
        }

        return vReport.HasOrphans ? 1 : 0;
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
