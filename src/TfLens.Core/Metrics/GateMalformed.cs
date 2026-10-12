using System.Text.Json;
using TfLens.Core.Contracts;

namespace TfLens.Core.Metrics;

/// <summary>
/// Takes a gate record that is not one requirement's verdict out of every gate figure, and lists it
/// (SCHEMA.md §3, REQ-FN-145).
/// </summary>
/// <remarks>
/// <para>
/// <b>A gate record is one REQ verdict.</b> A suite-level note such as
/// <c>{"gate":"verify-suite","result":"pass"}</c> carries no <c>req_id</c> or no <c>verdict</c>; scored, it
/// read as a failure — passes included (Lekhak TF-020). The reference removes such records before anything
/// is scored and lists each one under <c>gates_malformed</c>; this is the same rule, applied at read time.
/// </para>
/// <para>
/// <b>Nothing is dropped at parse time.</b> The record stays in the <c>"Gate"</c> table and in the raw
/// archive exactly as written; it simply never reaches a figure. The test is the reference's own Python
/// truthiness — an absent, <c>null</c> or empty <c>req_id</c> or <c>verdict</c> is malformed. A record
/// above schema v1 keeps its fields in <c>Overflow</c>, so a missing column is looked up there before the
/// record is judged.
/// </para>
/// </remarks>
public static class GateMalformed
{
    /// <summary>
    /// Splits gate records into the ones that may be scored and the malformed ones to be listed.
    /// </summary>
    /// <param name="aGates">Every stored gate record, for every repository.</param>
    /// <returns>The scorable records, in their original order, and the malformed list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="aGates"/> is <c>null</c>.</exception>
    public static MalformedGates Apply(IReadOnlyList<GateRecord> aGates)
    {
        ArgumentNullException.ThrowIfNull(aGates);

        var vKept = new List<GateRecord>(aGates.Count);
        var vMalformed = new List<MalformedGate>();

        foreach (var vGate in aGates)
        {
            if (IsMalformed(vGate))
            {
                vMalformed.Add(new MalformedGate(
                    vGate.Repo,
                    string.IsNullOrEmpty(vGate.App) ? RepoName(vGate.Repo) : vGate.App,
                    vGate.Ts,
                    vGate.Gate ?? OverflowText(vGate.Overflow, "gate"),
                    OverflowText(vGate.Overflow, "result")));
                continue;
            }

            vKept.Add(vGate);
        }

        return new MalformedGates(vKept, vMalformed);
    }

    /// <summary>Whether a gate record names no requirement or carries no verdict.</summary>
    /// <param name="aGate">The record.</param>
    /// <returns><c>true</c> when the record is not one REQ's verdict.</returns>
    public static bool IsMalformed(GateRecord aGate)
    {
        ArgumentNullException.ThrowIfNull(aGate);

        var vReqId = string.IsNullOrEmpty(aGate.ReqId) ? OverflowText(aGate.Overflow, "req_id") : aGate.ReqId;
        var vVerdict = string.IsNullOrEmpty(aGate.Verdict) ? OverflowText(aGate.Overflow, "verdict") : aGate.Verdict;

        return string.IsNullOrEmpty(vReqId) || string.IsNullOrEmpty(vVerdict);
    }

    /// <summary>The <c>name</c> half of <c>owner/name</c> — the reference's fallback app name.</summary>
    /// <param name="aRepo">The repository.</param>
    /// <returns>The last path segment.</returns>
    private static string RepoName(string aRepo)
    {
        var vSlash = aRepo.LastIndexOf('/');
        return vSlash < 0 ? aRepo : aRepo[(vSlash + 1)..];
    }

    /// <summary>Reads one string key out of a record's <c>Overflow</c> JSON object.</summary>
    /// <param name="aOverflow">The overflow text, or <c>null</c>.</param>
    /// <param name="aKey">The wire key.</param>
    /// <returns>The string value, or <c>null</c> when absent, not a string, or the text is not an object.</returns>
    private static string? OverflowText(string? aOverflow, string aKey)
    {
        if (string.IsNullOrEmpty(aOverflow))
        {
            return null;
        }

        try
        {
            using var vDocument = JsonDocument.Parse(aOverflow);
            return vDocument.RootElement.ValueKind == JsonValueKind.Object
                   && vDocument.RootElement.TryGetProperty(aKey, out var vValue)
                   && vValue.ValueKind == JsonValueKind.String
                ? vValue.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The outcome of <see cref="GateMalformed.Apply"/>.</summary>
/// <param name="Kept">The gate records every figure may score.</param>
/// <param name="Malformed">The records left out, one line each, in stored order.</param>
public sealed record MalformedGates(IReadOnlyList<GateRecord> Kept, IReadOnlyList<MalformedGate> Malformed)
{
    /// <summary>How many malformed records the given repository holds.</summary>
    /// <param name="aRepo"><c>owner/name</c>.</param>
    /// <returns>The count.</returns>
    public int CountFor(string aRepo) =>
        Malformed.Count(aM => string.Equals(aM.Repo, aRepo, StringComparison.Ordinal));
}
