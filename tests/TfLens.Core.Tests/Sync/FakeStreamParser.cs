using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Sync;

/// <summary>
/// A deterministic stand-in for the real parser: one record per non-blank line, typed by stream.
/// </summary>
/// <remarks>
/// The real parser is the storage area's; these tests are about the sync path around it, so the
/// double stays trivially predictable — line <c>n</c> always yields the same natural key, which is
/// what makes the replay-identity assertion meaningful. It also records what it was handed and when,
/// so a test can prove the raw archive existed before the parse (REQ-FN-027).
/// </remarks>
public sealed class FakeStreamParser : IStreamParser
{
    /// <summary>Every parse call, in order.</summary>
    public List<FakeParseCall> Calls { get; } = [];

    /// <summary>When set, the parser throws for this stream — proving the archive survives a parse failure.</summary>
    public StreamKind? ThrowOnStream { get; set; }

    /// <summary>Called before each parse; used to record the state of the disk at parse time.</summary>
    public Action<StreamKind>? OnParse { get; set; }

    /// <inheritdoc />
    public ParseResult Parse(int aUserId, string aRepo, string aSourceSha, StreamKind aStream, string aText)
    {
        OnParse?.Invoke(aStream);
        Calls.Add(new FakeParseCall(aUserId, aRepo, aSourceSha, aStream, aText));

        if (ThrowOnStream == aStream)
        {
            throw new InvalidOperationException("Parser blew up.");
        }

        var vLines = aText
            .Split('\n')
            .Select(aL => aL.Trim())
            .Where(aL => aL.Length > 0)
            .ToList();

        return new ParseResult
        {
            UserId = aUserId,
            Repo = aRepo,
            SourceSha = aSourceSha,
            Stream = aStream,
            Runs = aStream == StreamKind.Runs
                ? vLines.Select((_, aI) => new RunRecord
                {
                    UserId = aUserId, Repo = aRepo, SourceSha = aSourceSha, Ts = Stamp(aI)
                }).ToList()
                : [],
            Gates = aStream == StreamKind.Gates
                ? vLines.Select((_, aI) => new GateRecord
                {
                    UserId = aUserId, Repo = aRepo, SourceSha = aSourceSha, Ts = Stamp(aI)
                }).ToList()
                : [],
            Sessions = aStream == StreamKind.Sessions
                ? vLines.Select((_, aI) => new SessionRecord
                {
                    UserId = aUserId,
                    Repo = aRepo,
                    SourceSha = aSourceSha,
                    Ts = Stamp(aI),
                    SessionId = $"session-{aI}"
                }).ToList()
                : [],
            Commits = aStream == StreamKind.Commits
                ? vLines.Select((_, aI) => new CommitRecord
                {
                    UserId = aUserId,
                    Repo = aRepo,
                    SourceSha = aSourceSha,
                    Ts = Stamp(aI),
                    Sha = $"commit-{aI}"
                }).ToList()
                : [],
            PbEvents = aStream == StreamKind.Events
                ? vLines.Select((_, aI) => new PbEventRecord
                {
                    UserId = aUserId, Repo = aRepo, SourceSha = aSourceSha, Ts = Stamp(aI)
                }).ToList()
                : []
        };
    }

    /// <summary>Builds the deterministic timestamp line <paramref name="aIndex"/> always gets.</summary>
    /// <param name="aIndex">The zero-based line index.</param>
    /// <returns>An ISO-8601 timestamp unique to that line.</returns>
    private static string Stamp(int aIndex) => $"2026-01-01T00:00:{aIndex:00}Z";
}

/// <summary>One recorded call into <see cref="FakeStreamParser"/>.</summary>
/// <param name="UserId">The user the records belong to.</param>
/// <param name="Repo"><c>owner/name</c> of the source repository.</param>
/// <param name="SourceSha">The SHA the file was fetched at.</param>
/// <param name="Stream">The stream parsed.</param>
/// <param name="Text">The text handed to the parser.</param>
public sealed record FakeParseCall(int UserId, string Repo, string SourceSha, StreamKind Stream, string Text);
