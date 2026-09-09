using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of <c>tainted = {rk(g) for g in back}</c> — the requirements a live first-pass rate must
/// not count.
/// </summary>
/// <remarks>
/// <para>
/// A requirement with any backfilled gate record has its live <c>attempt</c> numbering restarted at 1
/// (SCHEMA.md §3.1), so including it in the live first-pass rate would flatter the number. The set is
/// computed once per analysis and both applied and displayed (REQ-FN-049).
/// </para>
/// <para>
/// <b>Membership is <see cref="ReqKey"/>, never a bare id (REQ-FN-111, BRD-178).</b> Keyed on the id
/// alone, one project's backfilled <c>REQ-UI-001</c> silently excluded every other project's live
/// <c>REQ-UI-001</c> from its own first-pass rate — a cross-project exclusion nobody asked for, on a
/// figure the reader has no way to see it in. The set is exported and displayed as
/// <c>app:req_id</c> for the same reason: a badge reading <c>REQ-UI-001</c> cannot tell a reader which
/// project's requirement was dropped.
/// </para>
/// </remarks>
public static class TaintSet
{
    /// <summary>
    /// Collects the requirements carried by backfilled gate records.
    /// </summary>
    /// <param name="aGates">Every gate record read for the user and framework, live and backfilled.</param>
    /// <returns>
    /// The tainted <c>(project, req_id)</c> keys; a key with a <c>null</c> id is a member when a
    /// backfilled record carried no REQ ID, mirroring the reference's Python set.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="aGates"/> is <c>null</c>.</exception>
    public static HashSet<ReqKey> FromBackfilled(IEnumerable<GateRecord> aGates)
    {
        ArgumentNullException.ThrowIfNull(aGates);

        var vTainted = new HashSet<ReqKey>();
        foreach (var vGate in aGates)
        {
            if (vGate.Backfilled == true)
            {
                vTainted.Add(ReqKey.Of(vGate));
            }
        }

        return vTainted;
    }

    /// <summary>
    /// Renders the tainted set for display, the way the reference sorts it.
    /// </summary>
    /// <remarks>
    /// The reference's own <c>sorted("%s:%s" % x for x in tainted if x[1])</c>: a key naming no
    /// requirement is dropped from the rendering — there is nothing to name — while the key itself stays
    /// in the set, because a backfilled record with no <c>req_id</c> still taints nothing rather than
    /// everything.
    /// </remarks>
    /// <param name="aTainted">The set from <see cref="FromBackfilled"/>.</param>
    /// <returns>The keys as <c>app:req_id</c>, in ordinal order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aTainted"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string> ForDisplay(IEnumerable<ReqKey> aTainted)
    {
        ArgumentNullException.ThrowIfNull(aTainted);

        return aTainted
            .Where(aKey => aKey.HasReqId)
            .Select(aKey => aKey.Display())
            .OrderBy(aText => aText, StringComparer.Ordinal)
            .ToList();
    }
}
