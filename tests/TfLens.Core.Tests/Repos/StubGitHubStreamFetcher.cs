using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.Repos;

/// <summary>
/// A hand-controlled <see cref="IGitHubStreamFetcher"/> so the registry's connect-time behaviour can
/// be tested without GitHub.
/// </summary>
/// <remarks>
/// The stub answers only the two calls the registry makes at connect time — repository metadata and a
/// directory probe — and records what it was asked, so a test can assert that a refusal happened
/// before any further call was made. The file-fetch members belong to the sync path and throw here.
/// </remarks>
public sealed class StubGitHubStreamFetcher : IGitHubStreamFetcher
{
    private readonly Dictionary<string, GitHubRepoInfo> objRepos = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> objPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every <c>owner/name</c> the stub was asked for metadata about, in order.</summary>
    public List<string> RequestedRepos { get; } = [];

    /// <summary>Every <c>owner/name@ref:path</c> the stub was asked to probe, in order.</summary>
    public List<string> ProbedPaths { get; } = [];

    /// <summary>
    /// Registers a repository the stub will report as existing.
    /// </summary>
    /// <param name="aOwner">GitHub owner.</param>
    /// <param name="aName">GitHub repository name.</param>
    /// <param name="aIsPrivate">Whether GitHub reports it private.</param>
    /// <param name="aDefaultBranch">The repository's default branch.</param>
    /// <returns>The same stub, for chaining.</returns>
    public StubGitHubStreamFetcher WithRepo(
        string aOwner,
        string aName,
        bool aIsPrivate = false,
        string aDefaultBranch = "main")
    {
        objRepos[$"{aOwner}/{aName}"] = new GitHubRepoInfo(aOwner, aName, aIsPrivate, aDefaultBranch);
        return this;
    }

    /// <summary>
    /// Registers a directory the stub will report as resolving.
    /// </summary>
    /// <param name="aOwner">GitHub owner.</param>
    /// <param name="aName">GitHub repository name.</param>
    /// <param name="aPath">Repository-relative directory path.</param>
    /// <param name="aRef">The branch or SHA the directory resolves at.</param>
    /// <returns>The same stub, for chaining.</returns>
    public StubGitHubStreamFetcher WithPath(string aOwner, string aName, string aPath, string aRef = "main")
    {
        objPaths.Add($"{aOwner}/{aName}@{aRef}:{aPath}");
        return this;
    }

    /// <inheritdoc />
    public Task<GitHubRepoInfo?> GetRepoAsync(string aOwner, string aName, CancellationToken aCancellationToken = default)
    {
        var vKey = $"{aOwner}/{aName}";
        RequestedRepos.Add(vKey);
        return Task.FromResult(objRepos.TryGetValue(vKey, out var vInfo) ? vInfo : null);
    }

    /// <inheritdoc />
    public Task<bool> PathExistsAsync(
        string aOwner,
        string aName,
        string aPath,
        string aRef,
        CancellationToken aCancellationToken = default)
    {
        var vKey = $"{aOwner}/{aName}@{aRef}:{aPath}";
        ProbedPaths.Add(vKey);
        return Task.FromResult(objPaths.Contains(vKey));
    }

    /// <inheritdoc />
    public Task<string?> LatestShaAsync(
        string aOwner,
        string aName,
        string aBranch,
        string aPath,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The registry does not sync; this belongs to the sync path.");

    /// <inheritdoc />
    public Task<string?> FetchFileAsync(
        string aOwner,
        string aName,
        string aPath,
        string aSha,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException("The registry does not fetch files; this belongs to the sync path.");
}
