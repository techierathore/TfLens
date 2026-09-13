using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Takes a wrongly-recorded run out of every figure without deleting it (SCHEMA.md §2.7, REQ-FN-135).
/// </summary>
/// <remarks>
/// <para>
/// <b>A correction is another record, never an edit.</b> The runs stream is append-only, so a run
/// written with a wrong figure can be neither changed nor removed — and nothing inside the record says
/// it is wrong, so every figure over it is quietly polluted. The producer therefore writes a second
/// record, <c>kind: "run-void"</c>, naming the bad one by the pair every run carries (<c>cmd</c> plus
/// <c>started</c>) and saying why.
/// </para>
/// <para>
/// <b>Two rules, both the ones this codebase applies everywhere else.</b> The voided record leaves every
/// figure — it is never clamped, halved or guessed at. And the count of what left travels with the
/// figures, because a total offered without its exclusions is just a different wrong number. A void that
/// names no run on the stream is an <b>orphan</b>: counted and reported, never silently dropped, exactly
/// as an orphaned <c>miss-amend</c> is.
/// </para>
/// <para>
/// <b>Why this is not optional.</b> Before it existed, TfLens counted the void record itself as a run and
/// kept the run it voided, so TechieFlow read <b>81 live runs against the reference's 75</b> — three
/// corrections counted as work, and the three wrong runs they corrected still inside every duration and
/// token total (MISS-TfLens-20260910-01).
/// </para>
/// <para>
/// Voids are matched <b>within one repository</b>, never across the estate: two repositories can each
/// hold a <c>build-phase</c> that started at the same minute, and a void written in one must not silence
/// the other's run.
/// </para>
/// </remarks>
public static class RunVoid
{
    /// <summary>The <c>kind</c> value that marks a correction rather than a run.</summary>
    public const string Kind = "run-void";

    /// <summary>What a void says when it names no reason; the reference prints the same words.</summary>
    private const string NoReason = "no reason recorded";

    /// <summary>
    /// Removes every voided run and the void records themselves, and reports what left.
    /// </summary>
    /// <param name="aRuns">Every stored run record, of every kind, for every repository.</param>
    /// <returns>The runs that survive, and the counts and reasons to publish beside them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRuns"/> is <c>null</c>.</exception>
    public static VoidedRuns Apply(IReadOnlyList<RunRecord> aRuns)
    {
        ArgumentNullException.ThrowIfNull(aRuns);

        var vKept = new List<RunRecord>(aRuns.Count);
        var vReasons = new List<string>();
        var vVoided = 0;
        var vOrphans = 0;

        foreach (var vGroup in aRuns.GroupBy(aRun => aRun.Repo, StringComparer.Ordinal))
        {
            var vRepoRuns = vGroup.ToList();

            // A void with no `started` names nothing, so it can match nothing. It is not an orphan
            // either — an orphan is a void that names a run this stream does not hold; one that names no
            // run at all is a malformed record and is simply not a void.
            var vVoids = new Dictionary<(string?, string?), RunRecord>();

            foreach (var vRun in vRepoRuns)
            {
                if (IsVoid(vRun) && !string.IsNullOrWhiteSpace(vRun.Started))
                {
                    vVoids[KeyOf(vRun)] = vRun;
                }
            }

            var vMatched = new HashSet<(string?, string?)>();

            foreach (var vRun in vRepoRuns)
            {
                if (IsVoid(vRun))
                {
                    continue;
                }

                var vKey = KeyOf(vRun);

                if (!vVoids.TryGetValue(vKey, out var vVoidRecord))
                {
                    vKept.Add(vRun);
                    continue;
                }

                vMatched.Add(vKey);
                vVoided++;
                vReasons.Add(ReasonLine(vGroup.Key, vRun, vVoidRecord));
            }

            vOrphans += vVoids.Keys.Count(aKey => !vMatched.Contains(aKey));
        }

        return new VoidedRuns(vKept, vVoided, vReasons, vOrphans);
    }

    /// <summary>Whether a record is a correction rather than a run.</summary>
    /// <param name="aRun">The record.</param>
    /// <returns><c>true</c> when its <c>kind</c> is <c>run-void</c>.</returns>
    public static bool IsVoid(RunRecord aRun)
    {
        ArgumentNullException.ThrowIfNull(aRun);

        return string.Equals(aRun.Kind, Kind, StringComparison.Ordinal);
    }

    /// <summary>The pair every run carries, which a void names it by.</summary>
    /// <param name="aRun">The record.</param>
    /// <returns>Its <c>(cmd, started)</c> key.</returns>
    private static (string?, string?) KeyOf(RunRecord aRun) => (aRun.Cmd, aRun.Started);

    /// <summary>
    /// One human-readable line naming what was removed and why.
    /// </summary>
    /// <param name="aRepo">The <c>owner/name</c> the records came from.</param>
    /// <param name="aRun">The run that was voided.</param>
    /// <param name="aVoid">The void record naming it.</param>
    /// <returns>The line, in the reference's own wording.</returns>
    private static string ReasonLine(string aRepo, RunRecord aRun, RunRecord aVoid)
    {
        var vApp = ShortNameOf(aRepo);
        var vReason = string.IsNullOrWhiteSpace(aVoid.VoidReason) ? NoReason : aVoid.VoidReason;

        return $"{vApp} {aRun.Cmd} {aRun.Started} — {vReason}";
    }

    /// <summary>
    /// The repository's own name, which the reference prints as the app label.
    /// </summary>
    /// <param name="aRepo">The <c>owner/name</c>.</param>
    /// <returns>The segment after the slash.</returns>
    private static string ShortNameOf(string aRepo)
    {
        var vSlash = aRepo.LastIndexOf('/');

        return vSlash >= 0 && vSlash < aRepo.Length - 1 ? aRepo[(vSlash + 1)..] : aRepo;
    }
}

/// <summary>
/// Runs with their corrections applied, and what that removed (SCHEMA.md §2.7, REQ-FN-135).
/// </summary>
/// <param name="Kept">Every record that is still a run: voids removed, and the runs they named removed.</param>
/// <param name="VoidedN">Runs a void took out of every figure.</param>
/// <param name="Reasons">One line per voided run, saying which run and why, for the reader to check.</param>
/// <param name="OrphanedN">
/// Voids naming a run this stream does not hold. Counted and reported rather than dropped: a void about
/// a run nobody has is either a merge that lost a record or a mistake, and both are worth seeing.
/// </param>
public sealed record VoidedRuns(
    IReadOnlyList<RunRecord> Kept,
    int VoidedN,
    IReadOnlyList<string> Reasons,
    int OrphanedN);
