using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Takes a withdrawn miss out of every figure without deleting it (SCHEMA.md §5.5.11, REQ-FN-143).
/// </summary>
/// <remarks>
/// <para>
/// <b>A correction is another record, never an edit.</b> The misses stream is append-only, so a miss
/// written in error can be neither changed nor removed. The producer writes a <c>kind: "miss-void"</c>
/// record naming the miss by its <c>miss_id</c> and saying why; this is the misses-stream sibling of
/// <see cref="RunVoid"/>.
/// </para>
/// <para>
/// <b>What leaves, and what does not.</b> The named miss leaves every figure together with every
/// <c>miss-fix</c> and <c>miss-amend</c> naming the same <c>miss_id</c>. Review records are a different
/// kind about a phase, not about a miss, and are untouched — exactly as the reference's
/// <c>analyse_misses</c> keeps <c>kind == "review"</c>. The count of what left travels with the figures
/// (<c>misses_voided_n</c>, <c>misses_voided</c>), and a void naming no miss on its repository's stream
/// is an orphan (<c>miss_voids_orphaned_n</c>): counted and reported, never silently dropped.
/// </para>
/// <para>
/// Voids are matched <b>within one repository</b>, keyed on <c>(Repo, MissId)</c>, so a withdrawal in
/// one repository can never silence a miss in another. This is applied by every reader of the three
/// miss reads, before any period filter, so a narrowed window can never resurrect a withdrawn miss.
/// </para>
/// </remarks>
public static class MissVoid
{
    /// <summary>What a void says when it names no reason; the reference prints the same words.</summary>
    public const string NoReason = "no reason recorded";

    /// <summary>
    /// Removes every withdrawn miss with its fixes and amends, and reports what left.
    /// </summary>
    /// <param name="aMisses">Every stored <c>miss</c> record, for every repository.</param>
    /// <param name="aFixes">Every stored <c>miss-fix</c> record.</param>
    /// <param name="aAmends">Every stored <c>miss-amend</c> record.</param>
    /// <param name="aVoids">Every stored <c>miss-void</c> record.</param>
    /// <returns>The records that survive, and the counts and reasons to publish beside them.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static VoidedMisses Apply(
        IReadOnlyList<MissRecord> aMisses,
        IReadOnlyList<MissFixRecord> aFixes,
        IReadOnlyList<MissAmendRecord> aAmends,
        IReadOnlyList<MissVoidRecord> aVoids)
    {
        ArgumentNullException.ThrowIfNull(aMisses);
        ArgumentNullException.ThrowIfNull(aFixes);
        ArgumentNullException.ThrowIfNull(aAmends);
        ArgumentNullException.ThrowIfNull(aVoids);

        if (aVoids.Count == 0)
        {
            return new VoidedMisses(aMisses, aFixes, aAmends, [], 0);
        }

        var vVoids = VoidsByKey(aVoids);
        var vMissKeys = aMisses.Select(aMiss => (aMiss.Repo, aMiss.MissId)).ToHashSet();

        var vVoided = new List<string>();
        var vOrphans = 0;

        foreach (var vPair in vVoids)
        {
            if (vMissKeys.Contains(vPair.Key))
            {
                vVoided.Add(ReasonLine(vPair.Value));
            }
            else
            {
                vOrphans++;
            }
        }

        var vKeys = vVoids.Select(aPair => aPair.Key).ToHashSet();

        return new VoidedMisses(
            aMisses.Where(aMiss => !vKeys.Contains((aMiss.Repo, aMiss.MissId))).ToList(),
            aFixes.Where(aFix => !vKeys.Contains((aFix.Repo, aFix.MissId))).ToList(),
            aAmends.Where(aAmend => !vKeys.Contains((aAmend.Repo, aAmend.MissId))).ToList(),
            vVoided,
            vOrphans);
    }

    /// <summary>
    /// One human-readable line naming the withdrawn miss and why, in the reference's own shape.
    /// </summary>
    /// <param name="aVoid">The void record.</param>
    /// <returns><c>"&lt;miss_id&gt; — &lt;reason&gt;"</c>, or the no-reason wording when it gave none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aVoid"/> is <c>null</c>.</exception>
    public static string ReasonLine(MissVoidRecord aVoid)
    {
        ArgumentNullException.ThrowIfNull(aVoid);

        // Python's `reason or "..."` treats only None and "" as absent, so whitespace is a reason.
        var vReason = string.IsNullOrEmpty(aVoid.Reason) ? NoReason : aVoid.Reason;

        return $"{aVoid.MissId} — {vReason}";
    }

    /// <summary>
    /// One void per <c>(Repo, MissId)</c>, in repository order then write order — the order the
    /// reference walks its repositories and their streams in.
    /// </summary>
    /// <param name="aVoids">Every stored void.</param>
    /// <returns>The voids keyed and ordered; the earliest wins a duplicate key.</returns>
    private static List<KeyValuePair<(string Repo, string MissId), MissVoidRecord>> VoidsByKey(
        IReadOnlyList<MissVoidRecord> aVoids)
    {
        var vSeen = new HashSet<(string, string)>();
        var vOrdered = new List<KeyValuePair<(string Repo, string MissId), MissVoidRecord>>();

        foreach (var vVoid in aVoids
                     .OrderBy(aVoid => aVoid.Repo, StringComparer.Ordinal)
                     .ThenBy(aVoid => aVoid.Ts, StringComparer.Ordinal))
        {
            var vKey = (vVoid.Repo, vVoid.MissId);

            if (vSeen.Add(vKey))
            {
                vOrdered.Add(new KeyValuePair<(string Repo, string MissId), MissVoidRecord>(vKey, vVoid));
            }
        }

        return vOrdered;
    }
}
