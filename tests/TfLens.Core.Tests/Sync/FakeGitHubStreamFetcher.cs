using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Sync;

/// <summary>
/// A scriptable stand-in for the GitHub client that records exactly which calls the runner made.
/// </summary>
/// <remarks>
/// The SHA-skip requirement (REQ-FN-021) is an assertion about calls that must <b>not</b> happen, so
/// this double keeps two separate logs: one of commit lookups and one of file fetches.
/// </remarks>
public sealed class FakeGitHubStreamFetcher : IGitHubStreamFetcher
{
    /// <summary>The SHA answered per <c>owner/name</c>; a missing entry answers null.</summary>
    public Dictionary<string, string?> Shas { get; } = [];

    /// <summary>The file text answered per <c>owner/name:path</c>; a missing entry answers null (stream absent).</summary>
    public Dictionary<string, string> Files { get; } = [];

    /// <summary>Repositories whose commit lookup throws, keyed by <c>owner/name</c>.</summary>
    public Dictionary<string, Exception> Failures { get; } = [];

    /// <summary>Every commit lookup, as <c>owner/name</c>.</summary>
    public List<string> ShaCalls { get; } = [];

    /// <summary>Every file fetch, as <c>owner/name:path@sha</c>.</summary>
    public List<string> FileCalls { get; } = [];

    /// <inheritdoc />
    public Task<string?> LatestShaAsync(
        string aOwner,
        string aName,
        string aBranch,
        string aPath,
        CancellationToken aCancellationToken = default)
    {
        var vRepo = $"{aOwner}/{aName}";
        ShaCalls.Add(vRepo);

        if (Failures.TryGetValue(vRepo, out var vFailure))
        {
            throw vFailure;
        }

        return Task.FromResult(Shas.TryGetValue(vRepo, out var vSha) ? vSha : null);
    }

    /// <inheritdoc />
    public Task<string?> FetchFileAsync(
        string aOwner,
        string aName,
        string aPath,
        string aSha,
        CancellationToken aCancellationToken = default)
    {
        FileCalls.Add($"{aOwner}/{aName}:{aPath}@{aSha}");

        return Task.FromResult(Files.TryGetValue($"{aOwner}/{aName}:{aPath}", out var vText) ? vText : null);
    }

    /// <inheritdoc />
    public Task<GitHubRepoInfo?> GetRepoAsync(string aOwner, string aName, CancellationToken aCancellationToken = default) =>
        Task.FromResult<GitHubRepoInfo?>(new GitHubRepoInfo(aOwner, aName, false, "main"));

    /// <inheritdoc />
    public Task<bool> PathExistsAsync(
        string aOwner,
        string aName,
        string aPath,
        string aRef,
        CancellationToken aCancellationToken = default) => Task.FromResult(true);
}
