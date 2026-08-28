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

        return KeyFor(aRecord.ProjectType, aRecord.ProjectTypeInferred);
    }

    /// <summary>
    /// The segment key any record belongs to, from the two fields every stream carries.
    /// </summary>
    /// <remarks>
    /// One code path for every stream. The miss figures segment on <c>project_type</c> exactly as the
    /// three questions do (REQ-FN-077), and a second copy of this two-line rule is how the two would
    /// eventually disagree about what <c>unclassified</c> means.
    /// </remarks>
    /// <param name="aProjectType">The record's declared or inferred project type.</param>
    /// <param name="aProjectTypeInferred">True when the type was inferred rather than declared.</param>
    /// <returns><see cref="MetricsConstants.Unclassified"/> when the type was inferred, else the declared type, defaulting to <c>app</c>.</returns>
    public static string KeyFor(string? aProjectType, bool? aProjectTypeInferred) =>
        aProjectTypeInferred == true
            ? MetricsConstants.Unclassified
            : aProjectType ?? "app";

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

    /// <summary>
    /// Groups any stream's records by project type, in the reference's sorted key order.
    /// </summary>
    /// <remarks>
    /// The generic sibling of <see cref="ByProjectType(IEnumerable{GateRecord})"/>, added for the miss
    /// figures (REQ-FN-077). Like it, there is no overload, flag or option that returns an "all types"
    /// bucket: the only way to get figures out of this class is one project type at a time.
    /// </remarks>
    /// <typeparam name="T">The record type being segmented.</typeparam>
    /// <param name="aRecords">The records to segment; may be empty.</param>
    /// <param name="aProjectTypeOf">Reads a record's project type.</param>
    /// <param name="aInferredOf">Reads whether that type was inferred.</param>
    /// <returns>One bucket per project type present, keyed ordinally so the report order is stable.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static SortedDictionary<string, List<T>> ByProjectType<T>(
        IEnumerable<T> aRecords,
        Func<T, string?> aProjectTypeOf,
        Func<T, bool?> aInferredOf)
    {
        ArgumentNullException.ThrowIfNull(aRecords);
        ArgumentNullException.ThrowIfNull(aProjectTypeOf);
        ArgumentNullException.ThrowIfNull(aInferredOf);

        var vBuckets = new SortedDictionary<string, List<T>>(StringComparer.Ordinal);
        foreach (var vRecord in aRecords)
        {
            var vKey = KeyFor(aProjectTypeOf(vRecord), aInferredOf(vRecord));
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
