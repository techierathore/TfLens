using TfLens.Core.Contracts;

namespace TfLens.Services.Ui;

/// <summary>
/// The words the Repos grid shows for a source's framework and for its kind of codebase.
/// </summary>
/// <remarks>
/// <para>
/// REQ-UI-076 (owner UAT, 2026-11-01). The grid had one "Kind" column holding the stored
/// <see cref="UserRepo.Kind"/>, which is the telemetry layout and therefore always the framework name. The
/// owner reads "Kind" as the kind of codebase — an application, a library, a framework — so the framework
/// now has its own column under its own name and "Kind" holds the project type the repository declares.
/// </para>
/// <para>
/// The project type is the <em>declared</em> one the metrics engine already reports per repository
/// (<see cref="PerRepoFacts.ProjectType"/>): the newest declaration across the repository's records. It is a
/// label here, never a segment key; the report pages still segment by each record's own type.
/// </para>
/// </remarks>
public static class RepoLabels
{
    /// <summary>The text shown when a repository has no records that declare a project type.</summary>
    public const string NotDeclared = "—";

    /// <summary>
    /// The display name of a provenance framework.
    /// </summary>
    /// <param name="aFramework">The stored framework value, <c>techieflow</c> or <c>playbook</c>.</param>
    /// <returns><c>TechieFlow</c>, <c>Playbook</c>, or the stored value unchanged when it is neither.</returns>
    public static string FrameworkName(string? aFramework) => aFramework switch
    {
        FrameworkNames.TechieFlow => "TechieFlow",
        FrameworkNames.Playbook => "Playbook",
        null or "" => NotDeclared,
        _ => aFramework,
    };

    /// <summary>
    /// The display name of a declared project type — the kind of codebase.
    /// </summary>
    /// <param name="aProjectType">The declared <c>project_type</c>, or <c>null</c> when none is declared.</param>
    /// <returns>
    /// <c>Application</c>, <c>Library</c>, <c>Framework</c> or <c>Docs</c> for the known values; an unknown
    /// value is shown as it was written rather than guessed at; <see cref="NotDeclared"/> when there is none.
    /// </returns>
    public static string KindName(string? aProjectType) => aProjectType?.Trim().ToLowerInvariant() switch
    {
        null or "" => NotDeclared,
        "app" or "application" => "Application",
        "library" or "lib" => "Library",
        "framework" => "Framework",
        "docs" => "Docs",
        _ => aProjectType!.Trim(),
    };
}
