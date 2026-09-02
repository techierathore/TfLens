namespace TfLens.Core.Provenance;

/// <summary>
/// The provenance check — every stored <c>SourceSha</c> against the ingest paths that could have
/// produced it (REQ-NFR-019 clause 3, BRD-143).
/// </summary>
/// <remarks>
/// <para>
/// <b>No network call.</b> The oracle is the store's own record of what it obtained: the
/// <c>"SourceProvenance"</c> ledger a sync or an import writes as it ingests, the <c>"SyncState"</c> row
/// a sync stamps with the SHA it fetched, the <c>"UserRepo"."BundleSha"</c> an import stamps with the
/// bundle's sha256 (BRD-134), and the raw archive file names the sync path wrote (BRD-19). A stream row
/// whose SHA matches none of those claims a provenance nobody ever obtained.
/// </para>
/// <para>
/// <b>An imported source is never flagged for lacking a commit SHA.</b> Its dataset identity <i>is</i>
/// the bundle hash, so the same comparison covers both kinds with no special case and no
/// <c>SourceKind</c> branch — which matters, because <c>SourceKind</c> is displayed and never divided on
/// (ADR-021).
/// </para>
/// <para>
/// <b>Comparison is exact and case-insensitive.</b> Git renders a SHA lower-case and a hand-written one
/// often is not; treating <c>ABC…</c> and <c>abc…</c> as different SHAs would report a false orphan,
/// which trains an operator to ignore the finding — the most expensive failure a gate can have
/// (REQ-NFR-018).
/// </para>
/// </remarks>
public static class ProvenanceAudit
{
    /// <summary>
    /// Compares what the store holds against what its ingest paths recorded obtaining.
    /// </summary>
    /// <remarks>
    /// Pure and side-effect free, so the rule can be pinned by a unit test without a database: the
    /// PostgreSQL store supplies the two lists and this decides. Every unaccounted triple is reported;
    /// nothing is sampled, thresholded or suppressed.
    /// </remarks>
    /// <param name="aStored">Distinct <c>(user, repo, SHA, table)</c> groups the stream tables hold.</param>
    /// <param name="aObtained">Every provenance entry any ingest path recorded, from every oracle.</param>
    /// <returns>The findings, ordinal by user, repository and SHA.</returns>
    /// <exception cref="ArgumentNullException">Either list was <c>null</c>.</exception>
    public static ProvenanceAuditReport Compare(
        IReadOnlyList<StoredProvenance> aStored,
        IReadOnlyList<SourceProvenanceRecord> aObtained)
    {
        ArgumentNullException.ThrowIfNull(aStored);
        ArgumentNullException.ThrowIfNull(aObtained);

        var vAccounted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vEntry in aObtained)
        {
            if (!string.IsNullOrWhiteSpace(vEntry.SourceSha))
            {
                vAccounted.Add(KeyFor(vEntry.UserId, vEntry.Repo, vEntry.SourceSha));
            }
        }

        var vGrouped = new Dictionary<string, OrphanTally>(StringComparer.OrdinalIgnoreCase);
        var vRows = 0;
        var vSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vStored in aStored)
        {
            var vKey = KeyFor(vStored.UserId, vStored.Repo, vStored.SourceSha);
            vRows += vStored.Rows;
            vSources.Add(vKey);

            if (vAccounted.Contains(vKey))
            {
                continue;
            }

            if (!vGrouped.TryGetValue(vKey, out var vTally))
            {
                vTally = new OrphanTally(vStored.UserId, vStored.Repo, vStored.SourceSha);
                vGrouped[vKey] = vTally;
            }

            vTally.Add(vStored.Table, vStored.Rows);
        }

        IReadOnlyList<ProvenanceOrphan> vOrphans = vGrouped.Values
            .Select(aTally => aTally.ToOrphan())
            .OrderBy(aOrphan => aOrphan.UserId)
            .ThenBy(aOrphan => aOrphan.Repo, StringComparer.Ordinal)
            .ThenBy(aOrphan => aOrphan.SourceSha, StringComparer.Ordinal)
            .ToList();

        return new ProvenanceAuditReport(vOrphans, vRows, vSources.Count, true);
    }

    /// <summary>
    /// Renders the audit as the lines the <c>provenance-check</c> verb prints.
    /// </summary>
    /// <remarks>
    /// The report has to be readable by the person deciding whether to purge, so it names the SHA, the
    /// repository, the tables and the row count — the four facts the 2026-08-29 cleanup needed and had
    /// to reconstruct by hand.
    /// </remarks>
    /// <param name="aReport">The audit result.</param>
    /// <returns>One line per finding, plus a summary line.</returns>
    /// <exception cref="ArgumentNullException">The report was <c>null</c>.</exception>
    public static IReadOnlyList<string> Describe(ProvenanceAuditReport aReport)
    {
        ArgumentNullException.ThrowIfNull(aReport);

        if (!aReport.IsSupported)
        {
            return
            [
                "provenance-check: this store cannot be audited, so no figure taken from it may be "
                + "quoted. This is a refusal, not a clean result — an integrity rule that cannot be "
                + "evaluated has not passed (REQ-NFR-019, BRD-89)."
            ];
        }

        var vLines = new List<string>();

        foreach (var vOrphan in aReport.Orphans)
        {
            vLines.Add(
                $"  ORPHAN user {vOrphan.UserId}  {vOrphan.Repo}  {vOrphan.SourceSha}  "
                + $"{vOrphan.Rows} row(s) in {string.Join(", ", vOrphan.Tables)}");
        }

        vLines.Add(
            $"provenance-check: {aReport.SourcesAudited} source(s) over {aReport.RowsAudited} row(s); "
            + $"{aReport.Orphans.Count} unaccounted source(s), {aReport.OrphanRows} row(s).");

        return vLines;
    }

    /// <summary>Builds the comparison key — provenance is per user and per repository, never global.</summary>
    /// <param name="aUserId">The user id.</param>
    /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
    /// <param name="aSourceSha">The SHA.</param>
    /// <returns>The key.</returns>
    private static string KeyFor(int aUserId, string aRepo, string aSourceSha) =>
        $"{aUserId}{aRepo}{aSourceSha.Trim()}";

    /// <summary>Accumulates the tables and rows one unaccounted SHA occupies.</summary>
    private sealed class OrphanTally
    {
        private readonly SortedSet<string> objTables = new(StringComparer.Ordinal);
        private int objRows;

        /// <summary>Starts a tally for one unaccounted triple.</summary>
        /// <param name="aUserId">The user id.</param>
        /// <param name="aRepo"><c>owner/name</c> of the repository.</param>
        /// <param name="aSourceSha">The SHA.</param>
        public OrphanTally(int aUserId, string aRepo, string aSourceSha)
        {
            UserId = aUserId;
            Repo = aRepo;
            SourceSha = aSourceSha;
        }

        /// <summary>The user the rows belong to.</summary>
        public int UserId { get; }

        /// <summary><c>owner/name</c> of the repository.</summary>
        public string Repo { get; }

        /// <summary>The unaccounted SHA.</summary>
        public string SourceSha { get; }

        /// <summary>Adds one table's rows to the tally.</summary>
        /// <param name="aTable">The stream table.</param>
        /// <param name="aRows">Rows in it carrying the SHA.</param>
        public void Add(string aTable, int aRows)
        {
            objTables.Add(aTable);
            objRows += aRows;
        }

        /// <summary>Renders the finished finding.</summary>
        /// <returns>The orphan record.</returns>
        public ProvenanceOrphan ToOrphan() =>
            new(UserId, Repo, SourceSha, objTables.ToList(), objRows);
    }
}
