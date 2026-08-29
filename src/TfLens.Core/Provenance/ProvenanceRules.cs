namespace TfLens.Core.Provenance;

/// <summary>
/// The write-path rule: a stream row cannot be produced without provenance (REQ-NFR-019 clause 1).
/// </summary>
/// <remarks>
/// <para>
/// Every path that produces stream rows — the sync, the import, the rebuild replay and the Playbook
/// adapter — funnels through <c>IStreamParser.Parse</c>, which takes the SHA the bytes were obtained at.
/// Calling <see cref="RequireObtained"/> there makes a provenance-less write impossible to express
/// rather than merely discouraged: there is no overload that omits the SHA, and no configuration that
/// downgrades the failure to a warning (BRD-89 / REQ-NFR-009).
/// </para>
/// <para>
/// The database carries the same rule a second time, as a <c>CHECK</c> constraint on every stream
/// table's <c>"SourceSha"</c>, so a hand-written <c>INSERT</c> that bypasses this code is refused by
/// PostgreSQL. Two layers, because the 155 rows found on 2026-08-29 arrived through exactly the layer
/// the application does not control.
/// </para>
/// </remarks>
public static class ProvenanceRules
{
    /// <summary>
    /// Refuses a parse that cannot say where its bytes came from.
    /// </summary>
    /// <remarks>
    /// The rule is deliberately about presence, not shape. A commit SHA is 40 hex characters, a bundle
    /// identity is 64, an abbreviated SHA in a fixture is 7 — a shape test would have to admit all
    /// three and would still have admitted <c>a91f3c2e4b7d9018f5c6a2b3d4e5f60718293a4b</c>, which is
    /// well-formed hex and was invented. What actually distinguishes a real SHA from a fabricated one is
    /// whether an ingest path recorded obtaining it, and that is
    /// <see cref="ProvenanceAudit.Compare"/>'s job, not this one's. This closes the other half: a row
    /// with no provenance at all.
    /// </remarks>
    /// <param name="aUserId">The user the records would belong to.</param>
    /// <param name="aRepo"><c>owner/name</c> of the source repository.</param>
    /// <param name="aSourceSha">The SHA or bundle identity the bytes were obtained at.</param>
    /// <exception cref="ProvenanceException">The SHA is absent or blank.</exception>
    public static void RequireObtained(int aUserId, string? aRepo, string? aSourceSha)
    {
        if (!string.IsNullOrWhiteSpace(aSourceSha))
        {
            return;
        }

        throw new ProvenanceException(
            $"A stream row cannot be produced without provenance (REQ-NFR-019): user {aUserId}, "
            + $"repository '{aRepo ?? "(none)"}' was parsed with no source SHA. Every row carries the "
            + "commit SHA a sync fetched at or the sha256 of the bundle an import committed.");
    }

    /// <summary>
    /// Refuses to publish a snapshot for a harness user id.
    /// </summary>
    /// <remarks>
    /// The export is the publication surface — the file whose numbers reach the Numbers row, B1 and B3 —
    /// so it is where the reserved band has to bite for clause 2 to mean anything. Seeded data stays
    /// visible to the smokes that wrote it and can never become a published figure.
    /// </remarks>
    /// <param name="aUserId">The user id an export was asked for.</param>
    /// <exception cref="ProvenanceException">The id is inside <see cref="ReservedUserIds"/>.</exception>
    public static void RefuseReservedUser(int aUserId)
    {
        if (!ReservedUserIds.IsReserved(aUserId))
        {
            return;
        }

        throw new ProvenanceException(
            $"User {aUserId} is in the harness band reserved at and above {ReservedUserIds.Floor} "
            + "(REQ-NFR-019): seeded data is never exported, because an exported figure is a published "
            + "figure.");
    }
}
