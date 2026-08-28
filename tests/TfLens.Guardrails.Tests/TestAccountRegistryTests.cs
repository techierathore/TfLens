using System.Text.RegularExpressions;
using FluentAssertions;
using TfLens.Core.AppManager;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-012 — the Usage Guide is the single source for the accounts the suite signs in with.
/// </summary>
/// <remarks>
/// <para>
/// These exist because the authenticated half of the suite went dark and nothing in the repository
/// could bring it back (<c>MISS-TfLens-20260828-02</c>). Seven tests failed with
/// <c>INVALID_CREDENTIALS</c> against accounts on a shared external service, and the only record of
/// what the passwords were supposed to be was a markdown table nothing read.
/// </para>
/// <para>
/// The repair has two halves. The <c>provision-test-accounts</c> verb makes the table executable — it
/// restores the accounts from it. These tests make the table authoritative — a credential that lives
/// anywhere else, or an account the guide does not list, fails the build rather than surviving until
/// the day it stops working.
/// </para>
/// </remarks>
public sealed class TestAccountRegistryTests
{
    /// <summary>Matches an email address on the owner's real domain — the ones that can actually sign in.</summary>
    /// <remarks>
    /// Deliberately narrowed to the live domain. Test data uses <c>@example.invalid</c> and
    /// <c>@techierathore.invalid</c>, which are reserved names that resolve nowhere and therefore
    /// authenticate against nothing; requiring those to be documented would be noise, not a guardrail.
    /// </remarks>
    private static readonly Regex LiveAccountPattern = new(
        @"[A-Za-z0-9._%+-]+@techierathore\.com",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Methods that change a password at AppManager rather than merely reading one.</summary>
    private static readonly string[] PasswordMutatingCalls = ["ChangePasswordAsync(", "ResetPasswordAsync("];

    /// <summary>The trait that marks a fixture as talking to the real AppManager instance.</summary>
    private const string LiveTraitMarker = """[Trait("Category", "Live")]""";

    /// <summary>The Usage Guide's absolute path.</summary>
    private static string GuidePath =>
        Path.Combine(RepoTree.Root.FullName, "docs", "TfLens-UsageGuide.md");

    /// <summary>
    /// The Test-users table parses into accounts that carry a real credential.
    /// </summary>
    /// <remarks>
    /// The parser is what the restore verb depends on, so a reformat of the table that quietly stops
    /// yielding rows must fail here rather than turn the verb into a no-op that reports success.
    /// </remarks>
    [Fact]
    public void TheUsageGuideTestUsersTableParsesIntoUsableAccounts()
    {
        var vAccounts = TestAccountRegistry.Read(GuidePath);

        vAccounts.Should().HaveCountGreaterThanOrEqualTo(
            2,
            "cross-user isolation (REQ-NFR-010) needs two documented accounts that can both sign in");

        vAccounts.Should().OnlyContain(aAccount => aAccount.Email.Contains('@'));
        vAccounts.Should().OnlyContain(aAccount => aAccount.Password.Length > 0);
        vAccounts.Select(aAccount => aAccount.Email).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Every live account the test suite names is listed in the Usage Guide.
    /// </summary>
    /// <remarks>
    /// The acceptance clause this pins, verbatim: "a guardrail test fails when the suite names an
    /// account the guide does not list". An account hard-coded into a test and nowhere else has no
    /// restore path — when its password is rotated, the only record of what it used to be is gone, and
    /// the failure surfaces as an unexplained <c>401</c> in a test that looks unrelated to identity.
    /// </remarks>
    [Fact]
    public void EveryLiveAccountTheSuiteSignsInWithIsListedInTheUsageGuide()
    {
        var vDocumented = TestAccountRegistry.ReadDocumentedEmails(GuidePath);

        var vUndocumented = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var vFile in RepoTree.Files("*.cs", "tests"))
        {
            foreach (Match vMatch in LiveAccountPattern.Matches(File.ReadAllText(vFile)))
            {
                var vEmail = vMatch.Value.ToLowerInvariant();

                if (!vDocumented.Contains(vEmail))
                {
                    vUndocumented.Add($"{vEmail}  ({Path.GetFileName(vFile)})");
                }
            }
        }

        vUndocumented.Should().BeEmpty(
            "REQ-NFR-012 — docs/TfLens-UsageGuide.md is the single source for the accounts the suite " +
            "signs in with. Add each of these to its Test-users table, or point the test at an account " +
            "that is already there. Found: {0}",
            string.Join("; ", vUndocumented));
    }

    /// <summary>
    /// No test changes a shared account's password without proving the documented one still works.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The acceptance clause: "any test that mutates an account's password restores it, or provisions
    /// its own throwaway account instead of touching a shared one". A successful change-password against
    /// a documented account silently invalidates the Usage Guide for every later run and every other
    /// agent — the failure appears somewhere else, later, with no trace back to the run that caused it.
    /// </para>
    /// <para>
    /// Scoped to files carrying <c>[Trait("Category", "Live")]</c>, because only those reach the real
    /// AppManager. The much larger body of tests that names the demo address against a scripted
    /// transport cannot rotate anything — <c>AppManagerClientTests</c> and <c>PasswordResetTests</c>
    /// change passwords all day long on a stub, and flagging them would train everyone to ignore this
    /// test.
    /// </para>
    /// <para>
    /// Enforced mechanically: in a live file that names a documented account, a password-mutating call
    /// must be followed by a <c>LoginAsync</c> — the restore proof. Today only
    /// <c>AppManagerLiveTests.ChangePasswordRejectsWrongCurrent</c> touches the API this way, and it
    /// passes the <i>wrong</i> current password on purpose, then signs in again with the documented one
    /// to show nothing moved.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoLiveTestMutatesASharedAccountPasswordWithoutProvingTheDocumentedOneStillWorks()
    {
        var vFindings = new List<string>();

        foreach (var vFile in RepoTree.Files("*.cs", "tests"))
        {
            var vText = File.ReadAllText(vFile);

            if (!vText.Contains(LiveTraitMarker, StringComparison.Ordinal) || !LiveAccountPattern.IsMatch(vText))
            {
                continue;
            }

            var vLastMutation = PasswordMutatingCalls
                .Select(aCall => vText.LastIndexOf(aCall, StringComparison.Ordinal))
                .Max();

            if (vLastMutation < 0)
            {
                continue;
            }

            var vRestoreProof = vText.IndexOf("LoginAsync(", vLastMutation, StringComparison.Ordinal);

            if (vRestoreProof < 0)
            {
                vFindings.Add(Path.GetFileName(vFile));
            }
        }

        vFindings.Should().BeEmpty(
            "REQ-NFR-012 — these files change a password on an account the Usage Guide documents and " +
            "never sign in again afterwards, so a rotation would go unnoticed until it blocked the " +
            "whole authenticated suite. Restore the documented password and prove it with a LoginAsync, " +
            "or register a throwaway account instead of touching a shared one. Found: {0}",
            string.Join(", ", vFindings));
    }

    /// <summary>
    /// The Usage Guide documents the procedure that restores the accounts.
    /// </summary>
    /// <remarks>
    /// A restore tool nobody can find is not a restore procedure. The guide must name the verb beneath
    /// the table the verb reads, so the person staring at a wall of <c>INVALID_CREDENTIALS</c> is
    /// already looking at the fix.
    /// </remarks>
    [Fact]
    public void TheUsageGuideDocumentsHowToRestoreTheTestAccounts()
    {
        var vText = File.ReadAllText(GuidePath);

        vText.Should().Contain(
            "provision-test-accounts",
            "the guide must name the verb that restores the accounts it lists");

        vText.Should().Contain(
            "REQ-NFR-012",
            "the procedure must be traceable to the requirement that demanded it");
    }
}
