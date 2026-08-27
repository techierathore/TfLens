using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// A parser that always throws, so a test can prove the raw archive survives a parse failure.
/// </summary>
/// <remarks>REQ-FN-027: the archived bytes must be replayable by <c>rebuild</c> even when parsing blew up.</remarks>
public sealed class ThrowingParser : IStreamParser
{
    /// <inheritdoc />
    public ParseResult Parse(int aUserId, string aRepo, string aSourceSha, StreamKind aStream, string aText) =>
        throw new InvalidOperationException("Parser failed on purpose.");
}
