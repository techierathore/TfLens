using TfLens.Core.Contracts;
using TfLens.Core.Metrics;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// REQ-FN-048 — segmentation by <c>project_type</c>, and the inferred-type rule.
/// </summary>
public sealed class SegmentTests
{
    /// <summary>A record whose project type was inferred segments as <c>unclassified</c>, never as <c>app</c>.</summary>
    [Fact]
    public void InferredProjectTypeSegmentsAsUnclassified()
    {
        var vRecord = GateFixtures.Gate(aProjectType: "app", aProjectTypeInferred: true);

        Assert.Equal(MetricsConstants.Unclassified, Segment.KeyFor(vRecord));
    }

    /// <summary>A record with a declared project type segments under that type.</summary>
    [Fact]
    public void DeclaredProjectTypeSegmentsUnderItself()
    {
        var vRecord = GateFixtures.Gate(aProjectType: "library");

        Assert.Equal("library", Segment.KeyFor(vRecord));
    }

    /// <summary>A record carrying no project type at all falls back to <c>app</c>, as the reference does.</summary>
    [Fact]
    public void AbsentProjectTypeFallsBackToApp()
    {
        var vRecord = GateFixtures.Gate(aProjectType: null);

        Assert.Equal("app", Segment.KeyFor(vRecord));
    }

    /// <summary>Inferred and declared records of the same nominal type land in different buckets and never pool.</summary>
    [Fact]
    public void InferredAndDeclaredRecordsNeverShareABucket()
    {
        var vBuckets = Segment.ByProjectType([
            GateFixtures.Gate(aProjectType: "app"),
            GateFixtures.Gate(aProjectType: "app", aProjectTypeInferred: true)
        ]);

        Assert.Equal(["app", MetricsConstants.Unclassified], vBuckets.Keys);
        Assert.Single(vBuckets["app"]);
        Assert.Single(vBuckets[MetricsConstants.Unclassified]);
    }

    /// <summary>Buckets come out in the reference's ordinal key order so report order is stable.</summary>
    [Fact]
    public void BucketsAreOrdinallyOrdered()
    {
        var vBuckets = Segment.ByProjectType([
            GateFixtures.Gate(aProjectType: "library"),
            GateFixtures.Gate(aProjectType: "app"),
            GateFixtures.Gate(aProjectType: "docs")
        ]);

        Assert.Equal(["app", "docs", "library"], vBuckets.Keys);
    }
}
