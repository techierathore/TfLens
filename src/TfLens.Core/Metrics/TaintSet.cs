using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of <c>tainted = {g.get("req_id") for g in back}</c> — the REQs a live first-pass rate
/// must not count.
/// </summary>
/// <remarks>
/// A REQ with any backfilled gate record has its live <c>attempt</c> numbering restarted at 1
/// (SCHEMA.md §3.1), so including it in the live first-pass rate would flatter the number. The set is
/// computed once per analysis and both applied and displayed (REQ-FN-049).
/// </remarks>
public static class TaintSet
{
    /// <summary>
    /// Collects the REQ IDs carried by backfilled gate records.
    /// </summary>
    /// <param name="aGates">Every gate record read for the user and framework, live and backfilled.</param>
    /// <returns>The tainted REQ IDs; <c>null</c> is a member when a backfilled record carried no REQ ID, mirroring the reference's Python set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aGates"/> is <c>null</c>.</exception>
    public static HashSet<string?> FromBackfilled(IEnumerable<GateRecord> aGates)
    {
        ArgumentNullException.ThrowIfNull(aGates);

        var vTainted = new HashSet<string?>(StringComparer.Ordinal);
        foreach (var vGate in aGates)
        {
            if (vGate.Backfilled == true)
            {
                vTainted.Add(vGate.ReqId);
            }
        }

        return vTainted;
    }

    /// <summary>
    /// Renders the tainted set for display, the way the reference sorts it.
    /// </summary>
    /// <param name="aTainted">The set from <see cref="FromBackfilled"/>.</param>
    /// <returns>The non-null REQ IDs in ordinal order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aTainted"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string> ForDisplay(IEnumerable<string?> aTainted)
    {
        ArgumentNullException.ThrowIfNull(aTainted);

        return aTainted
            .Where(aId => !string.IsNullOrEmpty(aId))
            .Select(aId => aId!)
            .OrderBy(aId => aId, StringComparer.Ordinal)
            .ToList();
    }
}
