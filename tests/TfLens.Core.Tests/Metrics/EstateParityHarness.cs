using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Tests.Export;

namespace TfLens.Core.Tests.Metrics;

/// <summary>
/// Writes a real <c>tflens.json</c> for one repository on this machine, so the §13 parity gate can be
/// run against <c>tf-metrics.sh --rollup --json</c> without a database (REQ-FN-061, BRD §13).
/// </summary>
/// <remarks>
/// <para>
/// It reads the repository's own <c>docs/metrics/</c> through <b><see cref="StreamParser"/></b> — the
/// same parser the sync uses — and serves the resulting records to the same engine and the same
/// exporter the app runs. Only the PostgreSQL round-trip is skipped, and that is covered on its own by
/// the store tests.
/// </para>
/// <para>
/// <b>The parser is the point.</b> An earlier version of this harness mapped the JSON by hand through a
/// test helper that carried fifteen fields and silently dropped every token field, so the documents it
/// wrote reported <c>scope_coverage: {absent: n}</c> and a <c>tokens_out_total</c> of zero — and every
/// token, model and money finding it produced was an artefact of the harness rather than a fact about
/// TfLens. Going through the real parser removes that whole class of false finding by construction: a
/// field this harness loses is a field production loses too.
/// </para>
/// <para>
/// It is driven by environment variables and does nothing without them, so it never runs as part of an
/// ordinary suite: <c>TFLENS_PARITY_REPO</c> is the repository directory and <c>TFLENS_PARITY_OUT</c>
/// the file to write.
/// </para>
/// </remarks>
public sealed class EstateParityHarness
{
    /// <summary>The user id every record is filed under.</summary>
    private const int HarnessUserId = 7;

    /// <summary>The provenance axis; these are all TechieFlow repositories.</summary>
    private const string HarnessFramework = "techieflow";

    /// <summary>The SHA stamped on the rows, standing in for the commit a sync would have fetched at.</summary>
    private const string HarnessSha = "worktree";

    /// <summary>
    /// Exports one repository's snapshot to the path named by <c>TFLENS_PARITY_OUT</c>.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ExportOneRepositoryForTheParityGate()
    {
        var vRepoPath = Environment.GetEnvironmentVariable("TFLENS_PARITY_REPO");
        var vOutPath = Environment.GetEnvironmentVariable("TFLENS_PARITY_OUT");

        if (string.IsNullOrWhiteSpace(vRepoPath) || string.IsNullOrWhiteSpace(vOutPath))
        {
            return;
        }

        // The reference names a repository by the directory it rolled up, so the segment after the slash
        // has to be that directory's name or every per-repo key would read as a different repository.
        var vName = Path.GetFileName(vRepoPath.TrimEnd('/', '\\'));
        var vRepo = "acme/" + vName;
        var vMetrics = Path.Combine(vRepoPath, "docs", "metrics");

        Assert.True(Directory.Exists(vMetrics), $"no docs/metrics under {vRepoPath}");

        var vParser = new StreamParser();
        var vStore = new FixtureTelemetryStore();

        // Sessions are deduped on the way INTO the real store, so the collapse count is bookkeeping the
        // store leaves behind rather than something derivable from the rows it serves. The harness seeds
        // rows directly, so it has to record the same figure the ingest would have (REQ-FN-063).
        var vSessionResults = ParseOf(vParser, vRepo, StreamKind.Sessions, vMetrics).ToList();
        var vSessions = vSessionResults.SelectMany(aR => aR.Sessions).ToList();

        vStore.WithSessionCollapses(
            HarnessUserId,
            vRepo,
            vSessionResults.Sum(aR => aR.SessionDuplicatesCollapsed));

        vStore.Seed(
            HarnessUserId,
            vRepo,
            HarnessFramework,
            ParseOf(vParser, vRepo, StreamKind.Gates, vMetrics).SelectMany(aR => aR.Gates),
            ParseOf(vParser, vRepo, StreamKind.Runs, vMetrics).SelectMany(aR => aR.Runs),
            vSessions,
            ParseOf(vParser, vRepo, StreamKind.Commits, vMetrics).SelectMany(aR => aR.Commits));

        var vMisses = ParseOf(vParser, vRepo, StreamKind.Misses, vMetrics).ToList();

        vStore.SeedMisses(
            HarnessUserId,
            vRepo,
            HarnessFramework,
            vMisses.SelectMany(aR => aR.Misses),
            vMisses.SelectMany(aR => aR.MissFixes),
            vMisses.SelectMany(aR => aR.MissAmends));

        vStore.SeedMissReviews(HarnessUserId, vRepo, HarnessFramework, vMisses.SelectMany(aR => aR.MissReviews));

        var vDataRoot = ExportFixture.TemporaryDataRoot();

        try
        {
            // The project's OWN rate card, so the list prices are the ones TfLens actually publishes.
            // Left to itself the exporter would write a default card into the throwaway root and price
            // the estate against rates nobody chose.
            var vPrices = Path.Combine(ExportFixture.RepositoryRoot(), "data", "prices.json");

            if (File.Exists(vPrices))
            {
                File.Copy(vPrices, Path.Combine(vDataRoot, "prices.json"), overwrite: true);
            }

            var vExporter = ExportFixture.Exporter(vDataRoot, vStore);
            var vResult = await vExporter.ExportAsync(HarnessUserId, HarnessFramework, ExportFixture.Date);

            File.Copy(vResult.JsonPath, vOutPath, overwrite: true);
        }
        finally
        {
            Directory.Delete(vDataRoot, recursive: true);
        }
    }

    /// <summary>
    /// Parses every file of one stream in a metrics directory.
    /// </summary>
    /// <param name="aParser">The production parser.</param>
    /// <param name="aRepo">The <c>owner/name</c> the records belong to.</param>
    /// <param name="aStream">Which stream to read.</param>
    /// <param name="aDirectory">The repository's <c>docs/metrics</c>.</param>
    /// <returns>One result per file that exists.</returns>
    private static IEnumerable<ParseResult> ParseOf(
        StreamParser aParser,
        string aRepo,
        StreamKind aStream,
        string aDirectory)
    {
        var vPath = Path.Combine(aDirectory, NameOf(aStream) + ".jsonl");

        if (!File.Exists(vPath))
        {
            yield break;
        }

        yield return aParser.Parse(HarnessUserId, aRepo, HarnessSha, aStream, File.ReadAllText(vPath));
    }

    /// <summary>The on-disk file name for a stream.</summary>
    /// <param name="aStream">The stream.</param>
    /// <returns>The base file name, without extension.</returns>
    private static string NameOf(StreamKind aStream) => aStream switch
    {
        StreamKind.Gates => StreamNames.Gates,
        StreamKind.Runs => StreamNames.Runs,
        StreamKind.Sessions => StreamNames.Sessions,
        StreamKind.Commits => StreamNames.Commits,
        StreamKind.Misses => StreamNames.Misses,
        _ => throw new ArgumentOutOfRangeException(nameof(aStream), aStream, "Unmapped stream.")
    };
}
