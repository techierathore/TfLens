using TfLens.Core.Import;
using TfLens.Core.Repos;

namespace TfLens.Core.Tests.Import;

/// <summary>
/// An imported bundle laid out as the repository names the app from its single
/// <c>docs/*-Checklist.md</c>, as the framework's script does (REQ-FN-152, BRD-220).
/// </summary>
public sealed class ImportChecklistAppTests
{
    private static readonly RepoRef Source = new("sample", "sample-empty");

    /// <summary>
    /// A bundle carrying <c>docs/SampleEmpty-Checklist.md</c> beside its streams records the name
    /// <c>SampleEmpty</c> for the source.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-152 — an import bundle with a docs checklist names the app")]
    public async Task BundleChecklistNamesTheApp()
    {
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(ImportTestSupport.TempRoot("fn152-named"), vStore);
        var vUpload = ImportUpload.FromBytes(
            "bundle.zip",
            ImportTestSupport.Zip(
                ("docs/metrics/gates.jsonl", ImportTestSupport.GateLines),
                ("docs/SampleEmpty-Checklist.md", "# SampleEmpty Checklist\n")));

        await vSubject.CommitAsync(ImportTestSupport.UserId, Source, vUpload, CancellationToken.None);

        Assert.Equal("SampleEmpty", vStore.ChecklistApps[Source.Repo]);
    }

    /// <summary>
    /// A bundle of streams alone says nothing about the name, so nothing is written and a name stored
    /// earlier stands.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact(DisplayName = "REQ-FN-152 — an import bundle with no checklist leaves the stored name alone")]
    public async Task BundleWithoutChecklistWritesNothing()
    {
        var vStore = new RecordingImportStore();
        var vSubject = ImportTestSupport.Subject(ImportTestSupport.TempRoot("fn152-silent"), vStore);
        var vUpload = ImportUpload.FromBytes(
            "bundle.zip", ImportTestSupport.Zip(("docs/metrics/gates.jsonl", ImportTestSupport.GateLines)));

        await vSubject.CommitAsync(ImportTestSupport.UserId, Source, vUpload, CancellationToken.None);

        Assert.Empty(vStore.ChecklistApps);
    }
}
