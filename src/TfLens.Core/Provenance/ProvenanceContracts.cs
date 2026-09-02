namespace TfLens.Core.Provenance;

/// <summary>
/// The user-id band reserved for seeding, fixture and smoke harnesses (REQ-NFR-019, BRD-143).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a band and not a flag.</b> Acceptance clause 2 offers two ways to keep demo data out of a
/// published figure: forbid a harness from writing into the application's store at all, or make it
/// write under a user id the application's own queries exclude. TfLens takes the second, because the
/// store <i>is</i> the thing the smokes exercise and a harness pointed at a second database would prove
/// nothing about the one the app reads. The exclusion is a property of the id, so it cannot be
/// forgotten at a call site and there is no switch, query parameter or setting that relaxes it
/// (BRD-89 / REQ-NFR-009).
/// </para>
/// <para>
/// <see cref="Floor"/> formalises a convention the suite already followed by hand — <c>90002</c>,
/// <c>90004</c>, <c>990001</c>, <c>991001</c> are all documented in <c>Fixtures.cs</c> as "above any id
/// AppManager will issue". Naming it once means the export can refuse the whole band rather than
/// re-deciding the question per test.
/// </para>
/// </remarks>
public static class ReservedUserIds
{
    /// <summary>The lowest reserved id; every id at or above it belongs to a harness, never a person.</summary>
    public const int Floor = 90_000;

    /// <summary>
    /// Tells whether a user id belongs to the harness band.
    /// </summary>
    /// <param name="aUserId">The AppManager user id, or a harness id.</param>
    /// <returns><c>true</c> when the id is reserved and its rows may never reach a published figure.</returns>
    public static bool IsReserved(int aUserId) => aUserId >= Floor;
}

/// <summary>How a stored <c>SourceSha</c> came to be known — the vocabulary of the provenance ledger.</summary>
/// <remarks>
/// The three values are ranked by how much they attest. <see cref="Api"/> and <see cref="Import"/> are
/// written at the moment the bytes were <i>obtained</i>, so nothing but the real ingest path can produce
/// them. <see cref="Archive"/> is derived from a raw-archive file name; the archive is the app's own
/// record of what a sync fetched (BRD-19) and is therefore evidence, but it is evidence a file-system
/// write can forge, so the audit reports it as the weaker of the two.
/// </remarks>
public static class ProvenanceKinds
{
    /// <summary>A sync obtained the SHA from GitHub and recorded it as it wrote the rows.</summary>
    public const string Api = "api";

    /// <summary>An import committed a bundle and recorded its sha256 as the dataset identity (BRD-134).</summary>
    public const string Import = "import";

    /// <summary>The SHA names a file in the raw archive the sync path wrote (BRD-19).</summary>
    public const string Archive = "archive";
}

/// <summary>
/// One entry in the provenance ledger — a SHA some ingest path states it actually obtained.
/// </summary>
/// <param name="UserId">The user the data belongs to.</param>
/// <param name="Repo"><c>owner/name</c> of the source repository.</param>
/// <param name="SourceSha">The commit SHA a sync fetched at, or the sha256 of an imported bundle.</param>
/// <param name="Kind">One of the <see cref="ProvenanceKinds"/> constants.</param>
/// <param name="ObtainedTs">ISO-8601 timestamp the ingest path obtained it.</param>
public sealed record SourceProvenanceRecord(
    int UserId,
    string Repo,
    string SourceSha,
    string Kind,
    string ObtainedTs);

/// <summary>
/// One distinct <c>(user, repo, source SHA)</c> the store holds rows under, and how many.
/// </summary>
/// <param name="UserId">The user the rows belong to.</param>
/// <param name="Repo"><c>owner/name</c> of the source repository.</param>
/// <param name="SourceSha">The provenance the rows claim.</param>
/// <param name="Table">The stream table the rows sit in.</param>
/// <param name="Rows">How many rows in that table carry the SHA.</param>
public sealed record StoredProvenance(int UserId, string Repo, string SourceSha, string Table, int Rows);

/// <summary>
/// A stored <c>SourceSha</c> that no ingest path ever recorded obtaining — provenance nobody has.
/// </summary>
/// <remarks>
/// This is the finding REQ-NFR-019 exists to produce. It is stated as rows-and-tables rather than as a
/// bare SHA because the answer an operator needs is "what would disappear if this were purged", and a
/// count that disagrees with the upstream repository is precisely the signal that arrived too late on
/// 2026-08-29.
/// </remarks>
/// <param name="UserId">The user whose figures the rows are inside.</param>
/// <param name="Repo"><c>owner/name</c> the rows are attributed to.</param>
/// <param name="SourceSha">The SHA no ledger entry, sync state, bundle or archive file accounts for.</param>
/// <param name="Tables">The stream tables holding the rows, ordinal by name.</param>
/// <param name="Rows">Total rows carrying the SHA.</param>
public sealed record ProvenanceOrphan(
    int UserId,
    string Repo,
    string SourceSha,
    IReadOnlyList<string> Tables,
    int Rows);

/// <summary>
/// What the provenance audit found — the whole answer, including "this store cannot be audited".
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsSupported"/> is carried rather than assumed because the audit is a property of the
/// <b>real</b> store: it compares stream rows against a ledger, a sync state and a raw archive. A store
/// that holds none of those answers <see cref="Unsupported"/>.
/// </para>
/// <para>
/// <b>Three answers, never two</b> (widened 2026-08-30, REQ-NFR-019 gap b). The report deliberately
/// distinguishes <i>audited and clean</i> (<see cref="Clean"/>), <i>audited and polluted</i>
/// (<see cref="HasOrphans"/>) and <i>could not audit</i> (<see cref="Unsupported"/>), and every consumer
/// has to keep them apart. <see cref="Unsupported"/> used to be described as making "no claim in either
/// direction", and every consumer that had nothing to do with a no-claim answer therefore did nothing —
/// which is indistinguishable, downstream, from a clean bill of health. It is not a neutral answer: a
/// store that cannot be audited cannot support a published figure, so
/// <c>ParityRecord.EvaluateWithProvenance</c> refuses <c>QUOTABLE</c> on it with its own reason, and
/// <c>provenance-check</c> exits non-zero on it. BRD-89 permits integrity rules no relaxation, and
/// "we never evaluated the rule" is the quietest relaxation there is.
/// </para>
/// <para>
/// There is deliberately no "ignore" list, no severity threshold and no configuration: one orphan row
/// is the whole finding, exactly as one changed byte of the reference script invalidates a parity stamp.
/// </para>
/// </remarks>
/// <param name="Orphans">Every unaccounted SHA, ordinal by user, repository and SHA.</param>
/// <param name="RowsAudited">Stream rows the audit read.</param>
/// <param name="SourcesAudited">Distinct <c>(user, repo, SHA)</c> triples the audit read.</param>
/// <param name="IsSupported">Whether the store this came from can answer the question at all.</param>
public sealed record ProvenanceAuditReport(
    IReadOnlyList<ProvenanceOrphan> Orphans,
    int RowsAudited,
    int SourcesAudited,
    bool IsSupported)
{
    /// <summary>
    /// The answer a store that cannot audit gives — and, since 2026-08-30, a refusal rather than a shrug.
    /// </summary>
    /// <remarks>
    /// Its <see cref="Orphans"/> list is empty because no finding was made, <b>not</b> because the store
    /// is clean, and the two must never be conflated: read <see cref="IsSupported"/> before reading
    /// <see cref="HasOrphans"/>. A consumer that only asks <see cref="HasOrphans"/> is asking whether the
    /// audit found anything, which an audit that never ran never does.
    /// </remarks>
    public static readonly ProvenanceAuditReport Unsupported = new([], 0, 0, false);

    /// <summary>An audited store with nothing to report.</summary>
    public static readonly ProvenanceAuditReport Clean = new([], 0, 0, true);

    /// <summary>True when at least one stored SHA has no ingest path behind it.</summary>
    public bool HasOrphans => Orphans.Count > 0;

    /// <summary>Rows sitting on unaccounted provenance — what a purge would remove.</summary>
    public int OrphanRows => Orphans.Sum(aOrphan => aOrphan.Rows);
}

/// <summary>
/// Thrown when a write path is handed provenance nobody obtained (REQ-NFR-019 clause 1).
/// </summary>
/// <remarks>
/// It is an exception rather than a logged warning on purpose: ADR-007 puts integrity rules in the shape
/// of the result, not in configuration, and a parse that cannot say where its bytes came from has no
/// safe partial answer to return. Nothing catches this to carry on with a blank SHA.
/// </remarks>
public sealed class ProvenanceException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="aMessage">What was missing, naming the user and repository but never a record body.</param>
    public ProvenanceException(string aMessage)
        : base(aMessage)
    {
    }
}
