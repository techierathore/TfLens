using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The identity of a requirement — <c>(project, req_id)</c> and never <c>req_id</c> alone
/// (REQ-FN-111, BRD-178).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every project emits a <c>REQ-UI-001</c>.</b> A rollup keyed on the id alone fuses TfLens's with
/// TechieBlog's and reports one requirement where there are two, and the error runs in the direction
/// that flatters: the framework's own combined first-pass rate read <b>72%</b> under that bug and
/// <b>48%</b> once keyed correctly. This type exists so the wrong key is <i>unrepresentable</i> rather
/// than merely forbidden — a set, a dictionary or a join written against it cannot forget the project,
/// because there is no constructor that takes an id on its own.
/// </para>
/// <para>
/// <b>The project axis is <c>app</c>.</b> That is the oracle's own key —
/// <c>rk(r) = (r.get("app"), r.get("req_id"))</c> in <c>.tfcore/telemetry/tf-metrics.sh</c> — and the
/// oracle is the specification (Architecture §7, BRD §13), so the two documents key on the same field
/// and the parity compare walks <c>tainted_reqs</c> string for string. <see cref="GateRecord.Repo"/>
/// would be a stricter axis, and it is what a future amendment should move to if two repositories ever
/// declare the same <c>app</c>; today the estate is one app per repository, so the two agree and
/// following the oracle costs nothing.
/// </para>
/// <para>
/// A record carrying no <c>app</c> keys under <see cref="AbsentProject"/> rather than under the empty
/// string, so "no project named" is a visible bucket rather than a value that reads as a project.
/// </para>
/// </remarks>
/// <param name="Project">The <c>app</c> the requirement belongs to; <c>null</c> when the record named none.</param>
/// <param name="ReqId">The requirement id, e.g. <c>REQ-UI-001</c>; <c>null</c> when the record named none.</param>
public readonly record struct ReqKey(string? Project, string? ReqId)
{
    /// <summary>The bucket a record naming no <c>app</c> is displayed under.</summary>
    /// <remarks>
    /// The same spelling the oracle uses for every other absent categorical (<c>cmd</c>,
    /// <c>harness</c>), so a reader meets one word for "the record did not say" across the document.
    /// </remarks>
    public const string AbsentProject = "?";

    /// <summary>The separator between the project and the id in the exported spelling.</summary>
    /// <remarks>The oracle's own <c>"%s:%s"</c>, so the export diffs key-for-key and value-for-value.</remarks>
    public const string Separator = ":";

    /// <summary>
    /// The identity of the requirement a gate record is a verdict about.
    /// </summary>
    /// <param name="aRecord">The gate record.</param>
    /// <returns>Its <c>(app, req_id)</c> key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRecord"/> is <c>null</c>.</exception>
    public static ReqKey Of(GateRecord aRecord)
    {
        ArgumentNullException.ThrowIfNull(aRecord);

        return new ReqKey(aRecord.App, aRecord.ReqId);
    }

    /// <summary>True when the record named a requirement at all.</summary>
    /// <remarks>
    /// A key with no id is still a key — it is what the taint set carries for a backfilled record that
    /// named no REQ, mirroring the oracle's Python set — but it is never <i>displayed</i>, because a
    /// badge reading <c>TfLens:</c> tells a reader nothing.
    /// </remarks>
    public bool HasReqId => !string.IsNullOrEmpty(ReqId);

    /// <summary>
    /// The exported and rendered spelling — <c>app:req_id</c>.
    /// </summary>
    /// <returns>The key as one string, with <see cref="AbsentProject"/> standing in for an absent project.</returns>
    public string Display() =>
        (string.IsNullOrEmpty(Project) ? AbsentProject : Project) + Separator + ReqId;
}
