using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Parsing;
using TfLens.Core.Playbook;
using TfLens.Core.Tests.TestSupport;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// REQ-FN-065 / BRD-73 — the adapter fetches <c>events.ndjson</c>, archives it raw before parsing, and
/// stores the rows in the separate <c>"PbEvent"</c> table; REQ-FN-068 — it probes the field names first.
/// </summary>
public sealed class PlaybookAdapterTests : IDisposable
{
    private readonly string objDataRoot =
        Path.Combine(Path.GetTempPath(), "tflens-pb-" + Guid.NewGuid().ToString("N"));

    /// <summary>The synthetic fixture text, faithful to the emitter shape (see Fixtures/Playbook/README.md).</summary>
    private static readonly string FixtureText = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Playbook", "events-synthetic.ndjson"));

    /// <summary>Removes the temporary data root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(objDataRoot))
        {
            Directory.Delete(objDataRoot, true);
        }
    }

    /// <summary>The fetched bytes are archived verbatim, before the parser is ever called.</summary>
    [Fact]
    public async Task RawFileIsArchivedBeforeItIsParsed()
    {
        var vStore = new CapturingStore();
        var vParser = new ThrowingParser();
        var vAdapter = Build(vStore, vParser);

        var vAct = async () => await vAdapter.IngestAsync(Repo(), "abc1234");

        await vAct.Should().ThrowAsync<InvalidOperationException>();

        var vArchive = Directory
            .EnumerateFiles(objDataRoot, "events-abc1234.ndjson", SearchOption.AllDirectories)
            .Single();
        File.ReadAllText(vArchive).Should().Be(FixtureText, "the archive must survive a parser failure intact");
    }

    /// <summary>The archive path is user-scoped and named for the stream and the SHA.</summary>
    [Fact]
    public async Task ArchiveIsUserScopedAndShaNamed()
    {
        var vResult = await Build(new CapturingStore(), new StreamParser()).IngestAsync(Repo(), "abc1234");

        var vPath = vResult.RawArchivePaths.Single();
        vPath.Should().Contain(Path.Combine("raw", Fixtures.DemoUserId.ToString()));
        vPath.Should().EndWith(Path.Combine("techierathore__AI-First-Playbook", "events-abc1234.ndjson"));
    }

    /// <summary>The rows reach the store as Playbook events and nothing else.</summary>
    [Fact]
    public async Task ParsedRowsReachTheStoreAsPlaybookEvents()
    {
        var vStore = new CapturingStore();

        var vResult = await Build(vStore, new StreamParser()).IngestAsync(Repo(), "abc1234");

        vStore.Parsed.Should().ContainSingle();
        vStore.Parsed[0].Stream.Should().Be(StreamKind.Events);
        vStore.Parsed[0].PbEvents.Should().HaveCount(7);
        vStore.Parsed[0].Runs.Should().BeEmpty();
        vStore.Parsed[0].Gates.Should().BeEmpty();
        vResult.RecordsWritten.Should().Be(7);
    }

    /// <summary>The schema probe runs on the raw text and reports what the file actually carries.</summary>
    [Fact]
    public async Task SchemaProbeReportsTheObservedFieldNames()
    {
        var vResult = await Build(new CapturingStore(), new StreamParser()).IngestAsync(Repo(), "abc1234");

        vResult.Observation.Should().NotBeNull();
        vResult.Observation!.FieldNames.Should().Contain(["kind", "sessionID", "parentID", "messageID", "tokens", "cost", "ts"]);
        vResult.Observation.InvalidLines.Should().Be(1);
    }

    /// <summary>The probe's field table is written beside the archive, ready for the DECISIONS.md entry.</summary>
    [Fact]
    public async Task ProbeWritesItsFieldTableBesideTheArchive()
    {
        await Build(new CapturingStore(), new StreamParser()).IngestAsync(Repo(), "abc1234");

        var vFields = Directory
            .EnumerateFiles(objDataRoot, "events-abc1234.fields.md", SearchOption.AllDirectories)
            .Single();
        File.ReadAllText(vFields).Should().Contain("`sessionID`");
    }

    /// <summary>An absent stream file is a legitimate "no telemetry", not a failure.</summary>
    [Fact]
    public async Task AbsentStreamFileIsNotAnError()
    {
        var vAdapter = Build(new CapturingStore(), new StreamParser(), aText: null);

        var vResult = await vAdapter.IngestAsync(Repo(), "abc1234");

        vResult.FilesFetched.Should().Be(0);
        vResult.FilesAbsent.Should().Be(1);
        vResult.RecordsWritten.Should().Be(0);
    }

    /// <summary>A repository that routes to the schema-v1 path is refused, so it cannot be double-read.</summary>
    [Fact]
    public async Task ConvergedRepoIsRefusedByTheAdapter()
    {
        var vRepo = Repo() with { Kind = FrameworkNames.TechieFlow };

        var vAct = async () =>
            await Build(new CapturingStore(), new StreamParser()).IngestAsync(vRepo, "abc1234");

        await vAct.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>The adapter fetches only from the Playbook telemetry path.</summary>
    [Fact]
    public async Task AdapterFetchesFromTheVerificationTelemetryPath()
    {
        var vFetcher = new RecordingFetcher(FixtureText);
        var vAdapter = new PlaybookAdapter(
            vFetcher, new StreamParser(), new CapturingStore(), Options(), NullLogger<PlaybookAdapter>.Instance);

        await vAdapter.IngestAsync(Repo(), "abc1234");

        vFetcher.Paths.Should().Equal("verification/telemetry/events.ndjson");
    }

    /// <summary>
    /// Builds an adapter over a recording fetcher and the supplied store and parser.
    /// </summary>
    /// <param name="aStore">The store to write through.</param>
    /// <param name="aParser">The parser to use.</param>
    /// <param name="aText">The text the fetcher answers with; <c>null</c> means a 404.</param>
    /// <returns>The adapter.</returns>
    private PlaybookAdapter Build(ITelemetryStore aStore, IStreamParser aParser, string? aText = "")
        => new(
            new RecordingFetcher(aText == string.Empty ? FixtureText : aText),
            aParser,
            aStore,
            Options(),
            NullLogger<PlaybookAdapter>.Instance);

    /// <summary>Options pointing at this test's temporary data root.</summary>
    /// <returns>The options wrapper.</returns>
    private IOptions<TfLensOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new TfLensOptions { DataRoot = objDataRoot });

    /// <summary>A connected Playbook repository on the <c>events.ndjson</c> layout.</summary>
    /// <returns>The row.</returns>
    private static UserRepo Repo() => new()
    {
        UserId = Fixtures.DemoUserId,
        Repo = "techierathore/AI-First-Playbook",
        Owner = "techierathore",
        Name = "AI-First-Playbook",
        Branch = "main",
        Kind = FrameworkNames.Playbook,
        Framework = FrameworkNames.Playbook,
        ConnectedTs = "2026-08-26T09:00:00.000Z"
    };
}
