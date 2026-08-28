using TfLens.Core.Import;

namespace TfLens.Core.Tests.Import;

/// <summary>
/// REQ-FN-084 and REQ-FN-085 — a source carries one dataset identity, and an imported source is never
/// polled.
/// </summary>
public sealed class ImportedSourceRulesTests
{
    /// <summary>The two stored values are the ones the column takes, and <c>Synced</c> is the default.</summary>
    [Fact]
    public void TheTwoSourceKindsAreSyncedAndImported()
    {
        Assert.Equal("api", ImportedSourceRules.SyncedKind);
        Assert.Equal("import", ImportedSourceRules.ImportedKind);

        Assert.True(ImportedSourceRules.IsImported("import"));
        Assert.False(ImportedSourceRules.IsImported("api"));

        // A row written before the column existed reads as the column default.
        Assert.False(ImportedSourceRules.IsImported(null));
        Assert.False(ImportedSourceRules.IsImported(string.Empty));
    }

    /// <summary>
    /// The poller and the header's Sync skip an imported source, so neither makes an outbound request.
    /// </summary>
    [Fact]
    public void AnImportedSourceIsNeverSynced()
    {
        Assert.False(ImportedSourceRules.CanSync(ImportedSourceRules.ImportedKind));
        Assert.True(ImportedSourceRules.CanSync(ImportedSourceRules.SyncedKind));
        Assert.True(ImportedSourceRules.CanSync(null));

        Assert.Contains("re-import", ImportedSourceRules.CannotSyncMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A source carries <c>LastSha</c> or <c>BundleSha</c>, never both (ADR-022).</summary>
    [Fact]
    public void ASourceCarriesOneDatasetIdentityNeverTwo()
    {
        Assert.True(ImportedSourceRules.HasSingleDatasetIdentity("abc1234", null));
        Assert.True(ImportedSourceRules.HasSingleDatasetIdentity(null, new string('f', 64)));
        Assert.True(ImportedSourceRules.HasSingleDatasetIdentity(null, null));

        Assert.False(ImportedSourceRules.HasSingleDatasetIdentity("abc1234", new string('f', 64)));

        Assert.Throws<InvalidOperationException>(
            () => ImportedSourceRules.AssertSingleDatasetIdentity("abc1234", new string('f', 64)));
    }

    /// <summary>The bundle sha stands exactly where a commit SHA stands.</summary>
    [Fact]
    public void TheBundleShaIsTheIdentityOfAnImportedSource()
    {
        var vBundle = new string('a', 64);

        Assert.Equal("abc1234", ImportedSourceRules.DatasetIdentity("abc1234", null));
        Assert.Equal(vBundle, ImportedSourceRules.DatasetIdentity(null, vBundle));
        Assert.Null(ImportedSourceRules.DatasetIdentity(null, null));
    }
}
