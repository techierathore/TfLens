namespace TfLens.Core.Repos;

/// <summary>
/// A parsed GitHub repository reference — the owner and the name, and nothing else.
/// </summary>
/// <remarks>
/// The type exists so that a repository is never carried around as a free-form string that might
/// still be a URL, a <c>owner/name</c> pair, or something the user mistyped. Everything downstream —
/// the store key, the raw-archive folder, the GitHub calls — is built from an instance of this
/// record, which can only be produced by <see cref="RepoInputParser"/>.
/// </remarks>
/// <param name="Owner">The GitHub owner (user or organisation) as validated.</param>
/// <param name="Name">The GitHub repository name as validated.</param>
public sealed record RepoRef(string Owner, string Name)
{
    /// <summary>The <c>owner/name</c> key used as the repository identity within a user.</summary>
    public string Repo => $"{Owner}/{Name}";

    /// <summary>The raw-archive folder name for this repository, <c>owner__name</c> (BRD-19).</summary>
    public string ArchiveFolder => $"{Owner}__{Name}";
}
