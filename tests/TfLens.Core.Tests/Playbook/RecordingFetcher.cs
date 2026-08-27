using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Playbook;

/// <summary>
/// An <see cref="IGitHubStreamFetcher"/> that answers one canned body and records the paths asked for.
/// </summary>
/// <param name="aText">The body to answer with, or <c>null</c> to answer 404 — a legitimate absent stream.</param>
public sealed class RecordingFetcher(string? aText) : IGitHubStreamFetcher
{
    /// <summary>Every repository-relative path the adapter asked for, in order.</summary>
    public List<string> Paths { get; } = [];

    /// <inheritdoc />
    public Task<string?> FetchFileAsync(
        string aOwner, string aName, string aPath, string aSha, CancellationToken aCancellationToken = default)
    {
        Paths.Add(aPath);
        return Task.FromResult(aText);
    }

    /// <inheritdoc />
    public Task<string?> LatestShaAsync(
        string aOwner, string aName, string aBranch, string aPath, CancellationToken aCancellationToken = default) =>
        Task.FromResult<string?>("abc1234");

    /// <inheritdoc />
    public Task<GitHubRepoInfo?> GetRepoAsync(
        string aOwner, string aName, CancellationToken aCancellationToken = default) =>
        Task.FromResult<GitHubRepoInfo?>(new GitHubRepoInfo(aOwner, aName, false, "main"));

    /// <inheritdoc />
    public Task<bool> PathExistsAsync(
        string aOwner, string aName, string aPath, string aRef, CancellationToken aCancellationToken = default) =>
        Task.FromResult(true);
}
