using TfLens.Core.Contracts;

namespace TfLens.Core.Import;

/// <summary>
/// The file names an uploaded bundle is recognised by, and the stream each one is (REQ-FN-082).
/// </summary>
/// <remarks>
/// <para>
/// Recognition is by <b>file name only</b>. The frameworks already write these files to disk and
/// TfLens is never allowed to ask them to change (BRD §1), so the file name is the whole contract —
/// there is no manifest, no export command and no header to read.
/// </para>
/// <para>
/// A stream's <see cref="StreamKind"/> is resolved from the wire name by <see cref="Enum.TryParse{T}(string, bool, out T)"/>
/// rather than from a hard-coded switch. That is deliberate: the catalogue can name a stream this
/// build does not yet have a kind for, report it honestly in a preview, and start parsing it with no
/// code change at all the moment the enum gains the member.
/// </para>
/// </remarks>
public static class ImportStreamCatalog
{
    /// <summary>File name to wire stream name, in the order a preview lists them.</summary>
    private static readonly (string FileName, string Stream)[] Entries =
    [
        ("runs.jsonl", StreamNames.Runs),
        ("gates.jsonl", StreamNames.Gates),
        ("sessions.jsonl", StreamNames.Sessions),
        ("commits.jsonl", StreamNames.Commits),
        ("misses.jsonl", MissesStream),
        ("events.ndjson", StreamNames.Events),
        // REQ-FN-094 / BRD-153 / ADR-023 — the Playbook's normalized schema-2 phase output. This ONE
        // LINE is the whole of the ingest wiring: the exporter's stdout is uploaded like any other
        // bundle and travels the same recognise → parse → upsert path, so there is no second ingest
        // code path for the record type (BRD-132). The file name is TfLens's own choice, because the
        // producer writes to stdout and names nothing.
        ("phase-metrics.ndjson", StreamNames.PhaseMetrics),
        // REQ-FN-103 / BRD-164 / ADR-024 — the Playbook's normalized MISS export. Same one-line wiring
        // as the phase stream above and for the same reason: the exporter writes to stdout and names
        // nothing, so the file name is TfLens's own. The rows land in the three EXISTING miss tables;
        // what differs is the key (an immutable source-line hash, not the natural miss_id), and that
        // difference is carried by the stream kind, not by a second ingest path (BRD-132).
        ("playbook-misses.ndjson", StreamNames.PlaybookMisses)
    ];

    /// <summary>
    /// The wire name of the miss stream.
    /// </summary>
    /// <remarks>
    /// Spelled here rather than read from <c>StreamNames</c> because the miss stream is being added by
    /// another cluster; the constant keeps this file compiling either way, and the value is the one
    /// SCHEMA.md and <c>misses.jsonl</c> already use.
    /// </remarks>
    public const string MissesStream = "misses";

    /// <summary>
    /// The streams that belong to the Playbook axis, for <see cref="TryResolveFramework"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not read from <c>StreamNames.Playbook</c>: that list drives the <b>fetcher</b> and
    /// the raw-archive rebuild, and the phase stream belongs to neither — it has no repository file to
    /// fetch and nothing a truncate-and-replay could restore it from (ADR-023). This list answers a
    /// different question: which axis does a recognised bundle describe.
    /// </remarks>
    private static readonly string[] PlaybookStreams =
        [StreamNames.Events, StreamNames.PhaseMetrics, StreamNames.PlaybookMisses];

    /// <summary>Every file name a bundle may carry a stream in.</summary>
    public static IReadOnlyList<string> FileNames { get; } = Entries.Select(aE => aE.FileName).ToArray();

    /// <summary>Every stream wire name, in preview order.</summary>
    public static IReadOnlyList<string> Streams { get; } = Entries.Select(aE => aE.Stream).ToArray();

    /// <summary>
    /// Recognises one entry by its file name.
    /// </summary>
    /// <param name="aEntryName">A bundle entry name, which may carry directories.</param>
    /// <param name="aStream">The stream's wire name when the method returns <c>true</c>.</param>
    /// <returns><c>true</c> when the entry's file name names a stream.</returns>
    public static bool TryRecognise(string? aEntryName, out string aStream)
    {
        aStream = string.Empty;

        if (string.IsNullOrWhiteSpace(aEntryName))
        {
            return false;
        }

        var vFileName = FileNameOf(aEntryName);

        foreach (var vEntry in Entries)
        {
            if (string.Equals(vEntry.FileName, vFileName, StringComparison.OrdinalIgnoreCase))
            {
                aStream = vEntry.Stream;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a wire stream name onto the parser's <see cref="StreamKind"/>.
    /// </summary>
    /// <param name="aStream">A wire stream name from this catalogue.</param>
    /// <param name="aKind">The kind when the method returns <c>true</c>.</param>
    /// <returns><c>false</c> when this build has no kind for the stream yet.</returns>
    public static bool TryResolveKind(string? aStream, out StreamKind aKind)
    {
        aKind = default;

        // A numeric string would parse onto an arbitrary member, so the name must be a real one.
        return !string.IsNullOrWhiteSpace(aStream)
               && !char.IsDigit(aStream[0])
               && Enum.TryParse(aStream, ignoreCase: true, out aKind)
               && Enum.IsDefined(aKind);
    }

    /// <summary>
    /// Orders recognised streams the way a preview lists them.
    /// </summary>
    /// <param name="aStream">A wire stream name.</param>
    /// <returns>Its position in the catalogue, or <see cref="int.MaxValue"/> when it is not one.</returns>
    public static int OrderOf(string aStream)
    {
        for (var vIndex = 0; vIndex < Entries.Length; vIndex++)
        {
            if (string.Equals(Entries[vIndex].Stream, aStream, StringComparison.Ordinal))
            {
                return vIndex;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Names the framework a set of recognised streams belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>events</c> and <c>phasemetrics</c> are the Playbook's streams and no TechieFlow bundle carries
    /// either, so the two sets are disjoint and a bundle carrying both describes two sources rather than
    /// one (ADR-016).
    /// </para>
    /// <para>
    /// The Playbook side is a <b>named set</b> rather than "anything that is not <c>events</c>", which is
    /// what it was before 2026-09-01. Left as it stood, the phase stream would have been resolved as a
    /// TechieFlow bundle and stamped the wrong framework onto the source (REQ-FN-094).
    /// </para>
    /// </remarks>
    /// <param name="aStreams">The recognised stream wire names.</param>
    /// <param name="aFramework">The framework when the method returns <c>true</c>.</param>
    /// <returns><c>false</c> when the bundle mixed the two frameworks' streams.</returns>
    public static bool TryResolveFramework(IEnumerable<string> aStreams, out string aFramework)
    {
        ArgumentNullException.ThrowIfNull(aStreams);

        aFramework = string.Empty;

        var vList = aStreams.ToArray();
        var vHasPlaybook = vList.Any(aS => PlaybookStreams.Contains(aS, StringComparer.Ordinal));
        var vHasTechieFlow = vList.Any(aS => !PlaybookStreams.Contains(aS, StringComparer.Ordinal));

        if (vHasPlaybook && vHasTechieFlow)
        {
            return false;
        }

        if (vHasPlaybook)
        {
            aFramework = FrameworkNames.Playbook;
            return true;
        }

        if (vHasTechieFlow)
        {
            aFramework = FrameworkNames.TechieFlow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Takes the file-name portion of a bundle entry, whichever separator it was zipped with.
    /// </summary>
    /// <param name="aEntryName">The entry name.</param>
    /// <returns>The last path segment.</returns>
    public static string FileNameOf(string aEntryName)
    {
        ArgumentNullException.ThrowIfNull(aEntryName);

        var vLast = aEntryName.LastIndexOfAny(['/', '\\']);
        return vLast < 0 ? aEntryName : aEntryName[(vLast + 1)..];
    }
}
