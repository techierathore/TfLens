using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of <c>seg()</c> in <c>tf-metrics.sh</c> — splits gate records by <c>project_type</c>.
/// </summary>
/// <remarks>
/// A record whose <c>project_type</c> was inferred rather than declared segments as
/// <see cref="MetricsConstants.Unclassified"/>, never as <c>app</c> (REQ-FN-048). There is no
/// overload, flag or option that returns an "all types" bucket: the only way to get figures out of
/// this class is one project type at a time.
/// </remarks>
public static class Segment
{
    /// <summary>
    /// The segment key one gate record belongs to.
    /// </summary>
    /// <param name="aRecord">The gate record to classify.</param>
    /// <returns><see cref="MetricsConstants.Unclassified"/> when the type was inferred, else the declared type, defaulting to <c>app</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRecord"/> is <c>null</c>.</exception>
    public static string KeyFor(GateRecord aRecord)
    {
        ArgumentNullException.ThrowIfNull(aRecord);

        return aRecord.ProjectTypeInferred == true
            ? MetricsConstants.Unclassified
            : aRecord.ProjectType ?? "app";
    }

    /// <summary>
    /// Groups gate records by project type, in the reference's sorted key order.
    /// </summary>
    /// <param name="aRecords">The records to segment; may be empty.</param>
    /// <returns>One bucket per project type present, keyed ordinally so the report order is stable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRecords"/> is <c>null</c>.</exception>
    public static SortedDictionary<string, List<GateRecord>> ByProjectType(IEnumerable<GateRecord> aRecords)
    {
        ArgumentNullException.ThrowIfNull(aRecords);

        var vBuckets = new SortedDictionary<string, List<GateRecord>>(StringComparer.Ordinal);
        foreach (var vRecord in aRecords)
        {
            var vKey = KeyFor(vRecord);
            if (!vBuckets.TryGetValue(vKey, out var vBucket))
            {
                vBucket = [];
                vBuckets[vKey] = vBucket;
            }

            vBucket.Add(vRecord);
        }

        return vBuckets;
    }
}
