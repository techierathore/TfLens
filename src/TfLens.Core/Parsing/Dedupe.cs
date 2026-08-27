using TfLens.Core.Contracts;

namespace TfLens.Core.Parsing;

/// <summary>What one stream's natural-key dedupe kept, and how many records it collapsed.</summary>
/// <typeparam name="T">The record type the rule applies to.</typeparam>
/// <param name="Records">The surviving records, in first-seen order.</param>
/// <param name="Collapsed">How many records the rule removed.</param>
public sealed record DedupeResult<T>(IReadOnlyList<T> Records, int Collapsed);

/// <summary>
/// The natural-key dedupe rules, one per stream (REQ-FN-033, REQ-FN-034, REQ-FN-035).
/// </summary>
/// <remarks>
/// <para>
/// These rules and the unique indexes in <c>database/001-schema.sql</c> are two spellings of one
/// decision, and they must not drift apart:
/// </para>
/// <list type="table">
///   <item><term><c>commits</c></term><description><c>(UserId, Repo, Sha)</c> — <c>UcCommitUserRepoSha</c>. Per repo, because two repositories may legitimately share a short SHA (BRD-26). First wins, exactly as <c>dedupe_commits</c> in <c>tf-metrics.sh</c>.</description></item>
///   <item><term><c>sessions</c></term><description><c>(UserId, Repo, SessionId)</c> — <c>UcSessionUserRepoId</c>. OpenCode appends a cumulative snapshot per idle, so the <b>largest</b> copy is the complete one: keep the highest <c>output_tokens</c>, tie broken by the latest <c>ts</c> (BRD-27).</description></item>
///   <item><term><c>runs</c></term><description><c>(UserId, Repo, Ts, App, Cmd)</c> — <c>UcRunIdentity</c>. First wins (BRD-28).</description></item>
///   <item><term><c>gates</c></term><description><c>(UserId, Repo, Ts, App, ReqId, RunId)</c> — <c>UcGateIdentity</c>. First wins (BRD-28).</description></item>
///   <item><term><c>events</c></term><description><c>(UserId, Repo, Ts, EventType, SessionId)</c> — <c>UcPbEventIdentity</c>. First wins (provisional, ADR-010).</description></item>
/// </list>
/// <para>
/// A commit record carrying no <c>sha</c>, and a session record carrying no <c>session_id</c>, are
/// <b>kept</b> rather than collapsed — the reference does the same, on the reasoning that a record with
/// no natural key cannot be proven to be a duplicate of anything.
/// </para>
/// </remarks>
public static class Dedupe
{
    /// <summary>
    /// Collapses commit records that share a SHA within one user and repository, keeping the first.
    /// </summary>
    /// <param name="aRecords">The commit records as parsed, in file order.</param>
    /// <returns>The survivors and the collapsed count.</returns>
    /// <remarks>
    /// Duplicates here are expected, not corruption: <c>commits.jsonl</c> is <c>merge=union</c>, so a
    /// commit recorded by the hook on one machine and reconstructed by a reconcile on another produces
    /// two genuine lines. Union merge guarantees nothing is dropped; this guarantees nothing is counted
    /// twice (SCHEMA.md §5).
    /// </remarks>
    public static DedupeResult<CommitRecord> Commits(IReadOnlyList<CommitRecord> aRecords) =>
        KeepFirst(aRecords, aR => string.IsNullOrEmpty(aR.Sha) ? null : Key(aR.UserId, aR.Repo, aR.Sha));

    /// <summary>
    /// Collapses run records sharing <c>ts + app + cmd</c> within one user and repository, keeping the first.
    /// </summary>
    /// <param name="aRecords">The run records as parsed, in file order.</param>
    /// <returns>The survivors and the collapsed count.</returns>
    public static DedupeResult<RunRecord> Runs(IReadOnlyList<RunRecord> aRecords) =>
        KeepFirst(aRecords, aR => Key(aR.UserId, aR.Repo, aR.Ts, aR.App ?? string.Empty, aR.Cmd ?? string.Empty));

    /// <summary>
    /// Collapses gate records sharing <c>ts + app + req_id + run_id</c> within one user and repository.
    /// </summary>
    /// <param name="aRecords">The gate records as parsed, in file order.</param>
    /// <returns>The survivors and the collapsed count.</returns>
    public static DedupeResult<GateRecord> Gates(IReadOnlyList<GateRecord> aRecords) =>
        KeepFirst(aRecords, aR => Key(
            aR.UserId, aR.Repo, aR.Ts, aR.App ?? string.Empty, aR.ReqId ?? string.Empty, aR.RunId ?? string.Empty));

    /// <summary>
    /// Collapses Playbook event records: <c>turn</c> records on <c>messageID</c>, keeping the one with the
    /// highest output tokens and, on a tie, the latest <c>ts</c>; marker records on
    /// <c>kind + ts + sessionID</c>.
    /// </summary>
    /// <remarks>
    /// Amended 2026-08-26 (REQ-FN-068). The Playbook's telemetry plugin appends a fresh <c>turn</c>
    /// record on <b>every</b> <c>message.updated</c> event, so one assistant message produces many rows
    /// as it streams and only the last carries its final token and cost counts — the Playbook's own
    /// joiner keeps "only the LAST turn row per messageID" for exactly this reason. Summing the
    /// uncollapsed rows would multiply the token and cost totals several-fold, so this is a correctness
    /// rule, not an optimisation. It is the same keep-the-largest shape as
    /// <see cref="Sessions(IReadOnlyList{SessionRecord})"/>.
    /// </remarks>
    /// <param name="aRecords">The event records as parsed, in file order.</param>
    /// <returns>The survivors, in the order their key was first seen, and the collapsed count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aRecords"/> is <c>null</c>.</exception>
    public static DedupeResult<PbEventRecord> PbEvents(IReadOnlyList<PbEventRecord> aRecords)
    {
        ArgumentNullException.ThrowIfNull(aRecords);

        var vKept = new List<PbEventRecord>(aRecords.Count);
        var vIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var vCollapsed = 0;

        foreach (var vRecord in aRecords)
        {
            var vKey = string.IsNullOrEmpty(vRecord.MessageId)
                ? Key(vRecord.UserId, vRecord.Repo, vRecord.Kind ?? string.Empty, vRecord.Ts,
                    vRecord.SessionId ?? string.Empty)
                : Key(vRecord.UserId, vRecord.Repo, vRecord.MessageId);

            if (!vIndexByKey.TryGetValue(vKey, out var vIndex))
            {
                vIndexByKey[vKey] = vKept.Count;
                vKept.Add(vRecord);
                continue;
            }

            vCollapsed++;
            if (IsLater(vRecord, vKept[vIndex]))
            {
                vKept[vIndex] = vRecord;
            }
        }

        return new DedupeResult<PbEventRecord>(vKept, vCollapsed);
    }

    /// <summary>
    /// Decides whether a candidate Playbook turn supersedes the one already kept.
    /// </summary>
    /// <param name="aCandidate">The newly read record.</param>
    /// <param name="aKept">The record currently held for the key.</param>
    /// <returns><c>true</c> when the candidate carries more output tokens, or the same and a later timestamp.</returns>
    private static bool IsLater(PbEventRecord aCandidate, PbEventRecord aKept)
    {
        if (aCandidate.TokensOutTotal != aKept.TokensOutTotal)
        {
            return aCandidate.TokensOutTotal > aKept.TokensOutTotal;
        }

        return string.CompareOrdinal(aCandidate.Ts, aKept.Ts) > 0;
    }

    /// <summary>
    /// Collapses session records per <c>session_id</c>, keeping the one with the highest
    /// <c>output_tokens</c> and, on a tie, the latest <c>ts</c>.
    /// </summary>
    /// <param name="aRecords">The session records as parsed, in file order.</param>
    /// <returns>The survivors, in the order their session id was first seen, and the collapsed count.</returns>
    /// <remarks>
    /// The OpenCode plugin appends a <b>cumulative</b> snapshot at every root-session idle, so several
    /// records legitimately share a <c>session_id</c> and only the largest is complete. Replaying an
    /// earlier snapshot therefore never lowers the stored figure (BRD-27), and the tie-break on <c>ts</c>
    /// makes the outcome deterministic regardless of line order.
    /// </remarks>
    public static DedupeResult<SessionRecord> Sessions(IReadOnlyList<SessionRecord> aRecords)
    {
        ArgumentNullException.ThrowIfNull(aRecords);

        var vKept = new List<SessionRecord>(aRecords.Count);
        var vIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var vCollapsed = 0;

        foreach (var vRecord in aRecords)
        {
            if (string.IsNullOrEmpty(vRecord.SessionId))
            {
                vKept.Add(vRecord);
                continue;
            }

            var vKey = Key(vRecord.UserId, vRecord.Repo, vRecord.SessionId);
            if (!vIndexByKey.TryGetValue(vKey, out var vIndex))
            {
                vIndexByKey[vKey] = vKept.Count;
                vKept.Add(vRecord);
                continue;
            }

            vCollapsed++;
            if (IsLarger(vRecord, vKept[vIndex]))
            {
                vKept[vIndex] = vRecord;
            }
        }

        return new DedupeResult<SessionRecord>(vKept, vCollapsed);
    }

    /// <summary>
    /// Decides whether a candidate session snapshot supersedes the one already kept.
    /// </summary>
    /// <param name="aCandidate">The newly read record.</param>
    /// <param name="aKept">The record currently held for this session id.</param>
    /// <returns><c>true</c> when the candidate has more output tokens, or the same and a later timestamp.</returns>
    private static bool IsLarger(SessionRecord aCandidate, SessionRecord aKept)
    {
        var vCandidateTokens = aCandidate.OutputTokens ?? int.MinValue;
        var vKeptTokens = aKept.OutputTokens ?? int.MinValue;

        if (vCandidateTokens != vKeptTokens)
        {
            return vCandidateTokens > vKeptTokens;
        }

        return string.CompareOrdinal(aCandidate.Ts, aKept.Ts) > 0;
    }

    /// <summary>
    /// The shared first-wins collapse used by every stream except <c>sessions</c>.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="aRecords">The records as parsed, in file order.</param>
    /// <param name="aKeyOf">Produces the natural key, or <c>null</c> for a record that has none and is always kept.</param>
    /// <returns>The survivors and the collapsed count.</returns>
    private static DedupeResult<T> KeepFirst<T>(IReadOnlyList<T> aRecords, Func<T, string?> aKeyOf)
    {
        ArgumentNullException.ThrowIfNull(aRecords);

        var vKept = new List<T>(aRecords.Count);
        var vSeen = new HashSet<string>(StringComparer.Ordinal);
        var vCollapsed = 0;

        foreach (var vRecord in aRecords)
        {
            var vKey = aKeyOf(vRecord);
            if (vKey is null)
            {
                vKept.Add(vRecord);
                continue;
            }

            if (!vSeen.Add(vKey))
            {
                vCollapsed++;
                continue;
            }

            vKept.Add(vRecord);
        }

        return new DedupeResult<T>(vKept, vCollapsed);
    }

    /// <summary>
    /// Joins key parts with a separator that cannot occur inside an ISO timestamp, REQ id or SHA.
    /// </summary>
    /// <param name="aUserId">The user the records belong to — always part of the key (ADR-013).</param>
    /// <param name="aParts">The remaining key parts, in the order the unique index declares them.</param>
    /// <returns>The composite key.</returns>
    private static string Key(int aUserId, params string[] aParts) =>
        string.Join(KeySeparator, aParts.Prepend(aUserId.ToString()));

    /// <summary>ASCII unit separator - it cannot occur inside a timestamp, REQ id, SHA or repo name.</summary>
    private const char KeySeparator = '\u001F';
}
