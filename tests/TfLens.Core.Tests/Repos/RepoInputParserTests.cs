using FluentAssertions;
using TfLens.Core.Repos;

namespace TfLens.Core.Tests.Repos;

/// <summary>
/// Covers <see cref="RepoInputParser"/> — what the Connect dialog is allowed to accept and what it
/// must refuse before a single GitHub call is made.
/// </summary>
public sealed class RepoInputParserTests
{
    /// <summary>
    /// A URL or an <c>owner/name</c> pair in any of the accepted spellings — with or without a scheme,
    /// a <c>www.</c> host, a <c>.git</c> suffix, a trailing slash, a query string or surrounding
    /// whitespace — parses to the same owner and name.
    /// </summary>
    /// <param name="aInput">The input as the dialog would receive it.</param>
    /// <param name="aOwner">The owner it must yield.</param>
    /// <param name="aName">The repository name it must yield.</param>
    [Theory]
    [InlineData("owner/name", "owner", "name")]
    [InlineData("  techierathore/TrBlazeUI  ", "techierathore", "TrBlazeUI")]
    [InlineData("https://github.com/owner/name", "owner", "name")]
    [InlineData("https://github.com/owner/name/", "owner", "name")]
    [InlineData("https://github.com/owner/name.git", "owner", "name")]
    [InlineData("https://github.com/owner/name.git/", "owner", "name")]
    [InlineData("http://github.com/owner/name", "owner", "name")]
    [InlineData("https://www.github.com/owner/name", "owner", "name")]
    [InlineData("https://GitHub.com/Owner/Name", "Owner", "Name")]
    [InlineData("github.com/owner/name", "owner", "name")]
    [InlineData("https://github.com/owner/name?tab=readme-ov-file", "owner", "name")]
    [InlineData("owner/name.with.dots", "owner", "name.with.dots")]
    [InlineData("owner-with-dash/name-with-dash", "owner-with-dash", "name-with-dash")]
    public void ParsesAcceptedForms(string aInput, string aOwner, string aName)
    {
        var vParsed = RepoInputParser.TryParse(aInput, out var vRepo, out var vError);

        vParsed.Should().BeTrue(vError);
        vRepo.Should().Be(new RepoRef(aOwner, aName));
    }

    /// <summary>
    /// Anything that is not exactly one github.com repository — empty input, a bare owner, a deep link
    /// into a tree, another host, an SSH remote, or a name carrying characters GitHub does not allow —
    /// is refused with a message, and never silently reinterpreted.
    /// </summary>
    /// <param name="aInput">The input the dialog must refuse.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("owner")]
    [InlineData("owner/")]
    [InlineData("owner/name/extra")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/name/tree/main")]
    [InlineData("https://github.com/owner/name/blob/main/README.md")]
    [InlineData("https://gitlab.com/owner/name")]
    [InlineData("https://github.example.com/owner/name")]
    [InlineData("ftp://github.com/owner/name")]
    [InlineData("git@github.com:owner/name.git")]
    [InlineData("owner//name")]
    [InlineData("owner/na me")]
    [InlineData("owner/-name")]
    [InlineData("owner/name;DROP TABLE \"UserRepo\"")]
    [InlineData("../../etc/passwd")]
    public void RefusesEverythingElse(string aInput)
    {
        var vParsed = RepoInputParser.TryParse(aInput, out var vRepo, out var vError);

        vParsed.Should().BeFalse();
        vRepo.Should().BeNull();
        vError.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>A null input is refused exactly as an empty one is, with a message and no throw.</summary>
    [Fact]
    public void RefusesNullInput()
    {
        var vParsed = RepoInputParser.TryParse(null, out var vRepo, out var vError);

        vParsed.Should().BeFalse();
        vRepo.Should().BeNull();
        vError.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The archive folder name is the <c>owner__name</c> form the raw archive uses, so a repository
    /// reference is the only thing that ever builds that path.
    /// </summary>
    [Fact]
    public void ExposesArchiveFolderName()
    {
        RepoInputParser.TryParse("techierathore/TrBlazeUI", out var vRepo, out _);

        vRepo!.ArchiveFolder.Should().Be("techierathore__TrBlazeUI");
        vRepo.Repo.Should().Be("techierathore/TrBlazeUI");
    }
}
