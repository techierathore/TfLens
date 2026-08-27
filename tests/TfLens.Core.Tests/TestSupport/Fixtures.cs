using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.TestSupport;

/// <summary>
/// Locates the JSONL fixture streams and names the users and repositories the tests share.
/// </summary>
/// <remarks>
/// The fixture layout mirrors the raw archive the fetcher writes — <c>{owner}__{name}/{stream}.jsonl</c>
/// — so a rebuild test can copy a directory straight into <c>data/raw/{userId}/</c>. The user ids are
/// the AppManager ids of the canonical test accounts in the UsageGuide's Test-users table; no test
/// invents an account.
/// </remarks>
public static class Fixtures
{
    /// <summary>AppManager user id of test user #1, <c>tflensdemo@techierathore.com</c> (UsageGuide).</summary>
    public const int DemoUserId = 2;

    /// <summary>AppManager user id of test user #2, <c>tflenstest2@techierathore.com</c> — the isolation counterpart.</summary>
    public const int SecondUserId = 3;

    /// <summary>
    /// Reserved user id for <b>destructive database</b> tests. Deliberately NOT a real AppManager account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DemoUserId"/> and <see cref="SecondUserId"/> are the owner's real accounts, and the
    /// store tests are destructive: they call <c>PostgresStore.RebuildAsync(userId)</c>, which drops
    /// <b>every</b> row for that user and replays from whatever raw archive the caller points at. Run
    /// against user 2 with a temp archive — which is exactly what the tests use — that silently wiped the
    /// owner's live telemetry on every <c>dotnet test</c>, leaving the running app with empty report
    /// pages until someone re-ran <c>rebuild</c> by hand. A test that destroys real data to prove a point
    /// about counting is not a test anyone can afford to run.
    /// </para>
    /// <para>
    /// These ids are above any id AppManager will issue, so a row written here can never collide with a
    /// real account, and cross-user isolation is still provable — that property needs two distinct ids,
    /// not two <i>real</i> ids. Keep destructive database work on these; the real ids stay for in-memory
    /// fixtures and for read-only assertions about live data.
    /// </para>
    /// </remarks>
    public const int StoreTestUserId = 90002;

    /// <summary>The isolation counterpart to <see cref="StoreTestUserId"/>; likewise not a real account.</summary>
    public const int StoreTestSecondUserId = 90003;

    /// <summary>The busy fixture repository, <c>project_type app</c>.</summary>
    public const string TrSetupRepo = "techierathore/TrSetup";

    /// <summary>The stale fixture repository, <c>project_type library</c> — sessions and commits are 11 days old.</summary>
    public const string TrBlazeUiRepo = "techierathore/TrBlazeUI";

    /// <summary>The SHA the fixtures pretend to have been fetched at.</summary>
    public const string SourceSha = "f1e2d3c";

    /// <summary>
    /// Absolute path of the <c>Fixtures</c> directory beside the test binary.
    /// </summary>
    /// <returns>The fixture root.</returns>
    public static string Root() => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>
    /// Absolute path of one repository's fixture directory.
    /// </summary>
    /// <param name="aRepo"><c>owner/name</c> of the fixture repository.</param>
    /// <returns>The directory holding that repository's four stream files.</returns>
    public static string RepoDirectory(string aRepo) =>
        Path.Combine(Root(), aRepo.Replace("/", "__", StringComparison.Ordinal));

    /// <summary>
    /// Reads one fixture stream file.
    /// </summary>
    /// <param name="aRepo"><c>owner/name</c> of the fixture repository.</param>
    /// <param name="aStream">Which stream to read.</param>
    /// <returns>The raw file text, exactly as the fetcher would have archived it.</returns>
    public static string Read(string aRepo, StreamKind aStream) =>
        File.ReadAllText(Path.Combine(RepoDirectory(aRepo), StreamNames.ToName(aStream) + ".jsonl"));
}
