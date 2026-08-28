using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;
using TfLens.Core.Import;
using TfLens.Core.Parsing;

namespace TfLens.Core.Tests.Import;

/// <summary>
/// An <see cref="ITelemetryStore"/> that records every upsert and refuses every other write.
/// </summary>
/// <remarks>
/// The point is the refusals. A preview must call none of these, and an import must call exactly
/// <see cref="UpsertAsync"/> — so a store that throws on everything else turns "the import path uses
/// only the shared write" into a test failure rather than an assertion nobody wrote.
/// </remarks>
public sealed class RecordingImportStore : ITelemetryStore
{
    /// <summary>Every parse result the subject handed to the store, in call order.</summary>
    public List<ParseResult> Upserts { get; } = [];

    /// <summary>Rows the next upsert reports as newly written; the default writes everything presented.</summary>
    public int? InsertedOverride { get; set; }

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken aCancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken aCancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<int> UpsertAsync(ParseResult aParsed, CancellationToken aCancellationToken = default)
    {
        Upserts.Add(aParsed);
        return Task.FromResult(InsertedOverride ?? aParsed.RecordCount);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunRecord>> ReadRunsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RunRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<GateRecord>> ReadGatesAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GateRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> ReadSessionsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SessionRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<CommitRecord>> ReadCommitsAsync(
        int aUserId, string aFramework, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CommitRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<PbEventRecord>> ReadPbEventsAsync(
        int aUserId, string? aRepo = null, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PbEventRecord>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncState>> ReadSyncStateAsync(
        int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SyncState>>([]);

    /// <summary>Sync-state rows the import path wrote, newest last.</summary>
    /// <remarks>
    /// This used to throw. It was changed on 2026-08-28 when the row registration moved out of the
    /// Repos page and into <c>CommitAsync</c>: an imported source has to get its counts from
    /// somewhere, and having the page write them meant a page carried store logic and a CLI import
    /// would have left the source reading <i>pending · 0 records</i> for ever. Writing
    /// <c>"SyncState"</c> is the same thing a fetched sync does, so it is recorded and asserted
    /// rather than forbidden — what must stay forbidden is a second parse/dedupe/upsert path
    /// (REQ-FN-083), which the other guards below still pin.
    /// </remarks>
    public List<SyncState> SyncStatesWritten { get; } = [];

    /// <inheritdoc />
    public Task WriteSyncStateAsync(SyncState aState, CancellationToken aCancellationToken = default)
    {
        SyncStatesWritten.Add(aState);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadUserReposAsync(
        int aUserId, CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserRepo>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRepo>> ReadAllUserReposAsync(CancellationToken aCancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserRepo>>([]);

    /// <summary>Source rows the import path wrote, newest last.</summary>
    public List<UserRepo> UserReposWritten { get; } = [];

    /// <inheritdoc />
    public Task WriteUserRepoAsync(UserRepo aRepo, CancellationToken aCancellationToken = default)
    {
        UserReposWritten.Add(aRepo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteRepoDataAsync(int aUserId, string aRepo, CancellationToken aCancellationToken = default) =>
        throw new InvalidOperationException("The import path must not delete anything.");

    /// <inheritdoc />
    public Task<RebuildReport> RebuildAsync(int? aUserId = null, CancellationToken aCancellationToken = default) =>
        throw new InvalidOperationException("The import path must not rebuild.");
}

/// <summary>
/// A <see cref="Stream"/> that throws the moment anything reads it.
/// </summary>
/// <remarks>
/// This is how "the size cap is enforced before the body is read" becomes a test rather than a
/// comment: hand the service an over-sized upload whose body cannot be read at all, and a refusal
/// proves nothing touched it.
/// </remarks>
public sealed class UnreadableStream : Stream
{
    /// <summary>True once anything attempted a read.</summary>
    public bool WasRead { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] aBuffer, int aOffset, int aCount)
    {
        WasRead = true;
        throw new InvalidOperationException("The upload body was read before the bounds were applied.");
    }

    /// <inheritdoc />
    public override long Seek(long aOffset, SeekOrigin aOrigin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long aValue) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] aBuffer, int aOffset, int aCount) => throw new NotSupportedException();
}

/// <summary>Shared helpers for the import tests — bundles, temporary data roots and the subject.</summary>
public static class ImportTestSupport
{
    /// <summary>The user id every import test writes as.</summary>
    public const int UserId = 90082;

    /// <summary>Two syntactically valid gate records, in the wire format the parser reads.</summary>
    public const string GateLines = """
        {"v":1,"ts":"2026-08-01T10:00:00Z","kind":"gate","app":"TfLens","project_type":"app","run_id":"r1","req_id":"REQ-FN-082","verdict":"pass","gate":"build"}
        {"v":1,"ts":"2026-08-03T11:30:00Z","kind":"gate","app":"TfLens","project_type":"app","run_id":"r2","req_id":"REQ-FN-083","verdict":"fail","gate":"acceptance"}
        """;

    /// <summary>One valid run record plus one line that is not JSON at all.</summary>
    public const string RunLinesWithOneInvalid = """
        {"v":1,"ts":"2026-08-02T09:00:00Z","kind":"run","app":"TfLens","cmd":"build-phase","mode":"yolo","duration_s":42}
        this line is not json
        """;

    /// <summary>
    /// Builds the subject over a throwaway data root.
    /// </summary>
    /// <param name="aDataRoot">The directory <c>data/raw</c> is created under.</param>
    /// <param name="aStore">The store to record upserts on.</param>
    /// <returns>The service, wired to the real <see cref="StreamParser"/>.</returns>
    public static TelemetryImportService Subject(string aDataRoot, ITelemetryStore aStore) =>
        new(
            new StreamParser(),
            aStore,
            Options.Create(new TfLensOptions { DataRoot = aDataRoot }),
            NullLogger<TelemetryImportService>.Instance);

    /// <summary>
    /// Creates an empty throwaway directory under the test artifacts folder.
    /// </summary>
    /// <param name="aName">A short name for the case, so a leftover directory says which test made it.</param>
    /// <returns>The directory's absolute path.</returns>
    public static string TempRoot(string aName)
    {
        var vPath = Path.Combine(
            Path.GetTempPath(), "tflens-import-tests", $"{aName}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(vPath);
        return vPath;
    }

    /// <summary>
    /// Zips a set of entries into memory.
    /// </summary>
    /// <param name="aEntries">Entry name to entry text.</param>
    /// <returns>The archive's bytes.</returns>
    public static byte[] Zip(params (string Name, string Text)[] aEntries)
    {
        using var vBuffer = new MemoryStream();

        using (var vArchive = new ZipArchive(vBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var vEntry in aEntries)
            {
                var vArchiveEntry = vArchive.CreateEntry(vEntry.Name);
                using var vStream = vArchiveEntry.Open();
                var vBytes = Encoding.UTF8.GetBytes(vEntry.Text);
                vStream.Write(vBytes, 0, vBytes.Length);
            }
        }

        return vBuffer.ToArray();
    }

    /// <summary>
    /// Counts every file anywhere under a directory, for the "wrote nothing" assertions.
    /// </summary>
    /// <param name="aRoot">The directory to walk.</param>
    /// <returns>How many files exist beneath it, zero when it does not exist.</returns>
    public static int FileCount(string aRoot) =>
        Directory.Exists(aRoot) ? Directory.GetFiles(aRoot, "*", SearchOption.AllDirectories).Length : 0;
}
