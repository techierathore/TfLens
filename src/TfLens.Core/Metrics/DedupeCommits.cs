using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// The port of <c>dedupe_commits()</c> — collapse commit records that share a SHA, keeping the first.
/// </summary>
/// <remarks>
/// Duplicates are expected, not corruption: a commit recorded by the hook on one machine can be
/// reconstructed from the log on another, and <c>merge=union</c> keeps both lines so no record is
/// ever lost. De-duplicating on read is the other half of that trade. The key is scoped per
/// repository, because two repositories may legitimately share a short SHA. Only commits need this —
/// the other streams record events that happen on one machine and cannot be reconstructed.
/// </remarks>
public static class DedupeCommits
{
    /// <summary>
    /// Collapses duplicate commit records per repository.
    /// </summary>
    /// <param name="aCommits">The commit records as read from the store.</param>
    /// <returns>The surviving records in input order, and how many duplicates were collapsed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aCommits"/> is <c>null</c>.</exception>
    public static (IReadOnlyList<CommitRecord> Records, int Duplicates) PerRepo(IEnumerable<CommitRecord> aCommits)
    {
        ArgumentNullException.ThrowIfNull(aCommits);

        var vSeen = new HashSet<(string Repo, string Sha)>();
        var vKept = new List<CommitRecord>();
        var vDuplicates = 0;

        foreach (var vCommit in aCommits)
        {
            if (!vSeen.Add((vCommit.Repo, vCommit.Sha)))
            {
                vDuplicates++;
                continue;
            }

            vKept.Add(vCommit);
        }

        return (vKept, vDuplicates);
    }
}
