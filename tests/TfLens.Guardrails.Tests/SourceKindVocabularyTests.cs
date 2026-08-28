using TfLens.Core.Contracts;
using TfLens.Core.Import;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// Pins BRD-132's two-vocabulary rule: the <c>SourceKind</c> column stores <c>api</c> | <c>import</c>,
/// while <c>/repos</c> and Coverage show <i>Synced</i> | <i>Imported</i>.
/// </summary>
/// <remarks>
/// These were collapsed into one during the 2026-08-28 build — the stored value was briefly the badge
/// wording — which would have made rewording a badge a schema migration and changed the export's
/// <c>source_kind</c> key under a downstream consumer. The distinction is cheap to restate and
/// expensive to rediscover, so it is pinned here rather than left to review.
/// </remarks>
public class SourceKindVocabularyTests
{
    /// <summary>The stored vocabulary is exactly the two lower-case words BRD-132 names.</summary>
    [Fact]
    public void StoredSourceKindsAreTheBrdVocabulary()
    {
        Assert.Equal("api", SourceKinds.Api);
        Assert.Equal("import", SourceKinds.Import);
        Assert.Equal(SourceKinds.Api, SourceKinds.Default);
    }

    /// <summary>The badge wording differs from the stored value and is reached through one helper.</summary>
    [Fact]
    public void DisplayLabelsAreDistinctFromTheStoredValues()
    {
        Assert.Equal("Synced", SourceKinds.ApiLabel);
        Assert.Equal("Imported", SourceKinds.ImportLabel);

        Assert.NotEqual(SourceKinds.Api, SourceKinds.ApiLabel);
        Assert.NotEqual(SourceKinds.Import, SourceKinds.ImportLabel);

        Assert.Equal(SourceKinds.ApiLabel, SourceKinds.DisplayName(SourceKinds.Api));
        Assert.Equal(SourceKinds.ImportLabel, SourceKinds.DisplayName(SourceKinds.Import));
    }

    /// <summary>An absent or unrecognised stored value reads as a fetched source, matching the column default.</summary>
    [Fact]
    public void AnUnknownStoredValueReadsAsFetched()
    {
        foreach (var vUnknown in new string?[] { null, "", "  ", "Synced", "nonsense" })
        {
            Assert.False(SourceKinds.IsImport(vUnknown));
            Assert.Equal(SourceKinds.ApiLabel, SourceKinds.DisplayName(vUnknown));
            Assert.True(ImportedSourceRules.CanSync(vUnknown));
        }
    }

    /// <summary>The import module aliases the contract's vocabulary rather than declaring a second one.</summary>
    [Fact]
    public void ImportRulesAliasTheOneVocabulary()
    {
        Assert.Equal(SourceKinds.Api, ImportedSourceRules.SyncedKind);
        Assert.Equal(SourceKinds.Import, ImportedSourceRules.ImportedKind);
    }

    /// <summary>A row carries a commit SHA or a bundle SHA, never both (REQ-FN-084, ADR-022).</summary>
    [Fact]
    public void ASourceHasExactlyOneDatasetIdentity()
    {
        Assert.True(ImportedSourceRules.HasSingleDatasetIdentity("abc123", null));
        Assert.True(ImportedSourceRules.HasSingleDatasetIdentity(null, "9fb3e491"));
        Assert.True(ImportedSourceRules.HasSingleDatasetIdentity(null, null));
        Assert.False(ImportedSourceRules.HasSingleDatasetIdentity("abc123", "9fb3e491"));

        Assert.Equal("9fb3e491", ImportedSourceRules.DatasetIdentity(null, "9fb3e491"));
        Assert.Equal("abc123", ImportedSourceRules.DatasetIdentity("abc123", null));
        Assert.Throws<InvalidOperationException>(
            () => ImportedSourceRules.DatasetIdentity("abc123", "9fb3e491"));
    }
}
