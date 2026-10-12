using TfLens.Core.Import;
using TfLens.Core.Repos;

namespace TfLens.Core.Tests.Import;

/// <summary>
/// An import records the session duplicates it collapsed (REQ-FN-146).
/// </summary>
/// <remarks>
/// The collapsed records are never written, so no read-time <c>COUNT(*)</c> can recover them; the
/// pooled <c>session_duplicates_collapsed</c> sums the stored <c>"SyncState"</c> figure. Before this row
/// an imported source stored 0 whatever its file held.
/// </remarks>
public sealed class ImportSessionCollapseTests
{
    private static readonly RepoRef Source = new("techierathore", "Chatur");

    /// <summary>Three records for one session plus one other: two duplicates.</summary>
    private const string SessionLines = """
        {"v":1,"ts":"2026-10-01T10:00:00Z","kind":"session","app":"Chatur","harness":"opencode","session_id":"s1","output_tokens":10}
        {"v":1,"ts":"2026-10-01T10:05:00Z","kind":"session","app":"Chatur","harness":"opencode","session_id":"s1","output_tokens":20}
        {"v":1,"ts":"2026-10-01T10:09:00Z","kind":"session","app":"Chatur","harness":"opencode","session_id":"s1","output_tokens":30}
        {"v":1,"ts":"2026-10-01T11:00:00Z","kind":"session","app":"Chatur","harness":"opencode","session_id":"s2","output_tokens":5}
        """;

    /// <summary>
    /// A committed sessions stream with records sharing a <c>session_id</c> writes their count to the
    /// source's sync state.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-146 — an import writes the session duplicates it collapsed")]
    public async Task ImportWritesSessionCollapses()
    {
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(ImportTestSupport.TempRoot("fn146-writes"), vStore);

        await vSubject.CommitAsync(ImportTestSupport.UserId, Source, Upload(), CancellationToken.None);

        Assert.Equal(2, Assert.Single(vStore.SyncStatesWritten).SessionDuplicatesCollapsed);
    }

    /// <summary>
    /// Re-importing the identical bundle sets the same figure again rather than adding to it.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-146 — re-importing the same bundle never accumulates the session duplicates")]
    public async Task ReimportNeverAccumulates()
    {
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(ImportTestSupport.TempRoot("fn146-reimport"), vStore);

        await vSubject.CommitAsync(ImportTestSupport.UserId, Source, Upload(), CancellationToken.None);
        await vSubject.CommitAsync(ImportTestSupport.UserId, Source, Upload(), CancellationToken.None);

        Assert.All(vStore.SyncStatesWritten, aS => Assert.Equal(2, aS.SessionDuplicatesCollapsed));
    }

    /// <summary>A bundle with no sessions stream records no session duplicates.</summary>
    [Fact(DisplayName = "REQ-FN-146 — a bundle with no sessions stream records no session duplicates")]
    public async Task NoSessionsStreamRecordsNothing()
    {
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(ImportTestSupport.TempRoot("fn146-nosessions"), vStore);
        var vUpload = ImportUpload.FromBytes(
            "metrics.zip", ImportTestSupport.Zip(("docs/metrics/gates.jsonl", ImportTestSupport.GateLines)));

        await vSubject.CommitAsync(ImportTestSupport.UserId, Source, vUpload, CancellationToken.None);

        Assert.Equal(0, Assert.Single(vStore.SyncStatesWritten).SessionDuplicatesCollapsed);
    }

    /// <summary>Builds the sessions-only upload.</summary>
    /// <returns>The upload.</returns>
    private static ImportUpload Upload() =>
        ImportUpload.FromBytes("metrics.zip", ImportTestSupport.Zip(("docs/metrics/sessions.jsonl", SessionLines)));
}
