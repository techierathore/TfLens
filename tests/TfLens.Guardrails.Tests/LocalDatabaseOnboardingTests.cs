using System.Text.RegularExpressions;
using TfLens.Core;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// Pins the one value a new developer cannot discover for themselves: the local PostgreSQL password
/// has to be the same in <c>.env.example</c> and in <see cref="TfLensOptions.LocalDevelopmentConnection"/>.
/// </summary>
/// <remarks>
/// <para>
/// Found at handoff on 2026-08-28. <c>.env.example</c> shipped <c>TfLensDbPassword=change-me</c> while the
/// code's local fallback dialled <c>Password=tflensdev</c>. Following the documented setup —
/// <c>cp .env.example .env</c>, <c>docker compose up -d postgres</c>, <c>dotnet run</c> — therefore started a
/// container with one password and an app expecting another.
/// </para>
/// <para>
/// What makes it worth a test rather than a one-line fix is the shape of the failure: the app aborts
/// pointing at the <b>database</b>, so the reader looks at Docker, at ports, at their connection string —
/// anywhere but at the example file that caused it. It is the same defect class as
/// <c>MISS-TfLens-20260828-01</c>, where the DevGuide named <c>.env</c> as the local secrets file and nothing
/// read it: the mechanism was fine and the onboarding document was wrong, so every gate stayed green while
/// a developer's first hour was wasted.
/// </para>
/// </remarks>
public class LocalDatabaseOnboardingTests
{
    private static string RepoRoot()
    {
        var vDir = new DirectoryInfo(AppContext.BaseDirectory);

        while (vDir is not null && !File.Exists(Path.Combine(vDir.FullName, "TfLens.slnx")))
        {
            vDir = vDir.Parent;
        }

        Assert.NotNull(vDir);
        return vDir!.FullName;
    }

    /// <summary>
    /// Reads <c>TfLensDbPassword</c> out of <c>.env.example</c> the way Compose does.
    /// </summary>
    /// <remarks>
    /// An inline <c>#</c> comment is stripped, because Compose strips it too — the line carries an
    /// <c>NFR-003-OK</c> waiver explaining why a real (if throwaway) value is committed there, and a test
    /// that compared the raw remainder of the line would fail on the explanation rather than on the value.
    /// </remarks>
    /// <returns>The configured password.</returns>
    private static string EnvPassword()
    {
        var vLine = File.ReadAllLines(Path.Combine(RepoRoot(), ".env.example"))
            .FirstOrDefault(aLine => aLine.TrimStart().StartsWith("TfLensDbPassword=", StringComparison.Ordinal));

        Assert.NotNull(vLine);

        var vValue = vLine!.Split('=', 2)[1];
        var vHash = vValue.IndexOf('#');

        return (vHash >= 0 ? vValue[..vHash] : vValue).Trim();
    }

    /// <summary>The compose template's password is the one the app falls back to for local development.</summary>
    [Fact]
    public void EnvExampleDatabasePasswordMatchesTheLocalFallback()
    {
        Assert.True(
            File.Exists(Path.Combine(RepoRoot(), ".env.example")),
            "Expected the compose template at .env.example.");

        var vEnvPassword = EnvPassword();

        var vFallback = Regex.Match(TfLensOptions.LocalDevelopmentConnection, @"Password=([^;]+)");
        Assert.True(vFallback.Success, "LocalDevelopmentConnection no longer carries a Password= segment.");

        Assert.Equal(vFallback.Groups[1].Value, vEnvPassword);
    }

    /// <summary>The template carries a usable default rather than a placeholder nobody can act on.</summary>
    /// <remarks>
    /// A placeholder here is not a neutral choice: Compose interpolates this file into the postgres
    /// container before anything else runs, so an unreplaced value produces a running database with the
    /// wrong credential rather than an obvious "you forgot to configure me" error.
    /// </remarks>
    [Fact]
    public void EnvExampleDatabasePasswordIsNotAPlaceholder()
    {
        var vValue = EnvPassword();

        Assert.False(string.IsNullOrWhiteSpace(vValue), "TfLensDbPassword must carry a value.");

        foreach (var vPlaceholder in new[] { "change-me", "changeme", "todo", "xxx", "<password>", "your-password" })
        {
            Assert.False(
                vValue.Contains(vPlaceholder, StringComparison.OrdinalIgnoreCase),
                $"TfLensDbPassword reads '{vValue}' — a placeholder here starts a container the app cannot "
                + "authenticate against, and the failure points at the database rather than at this file.");
        }
    }
}
