using System.Text.RegularExpressions;

namespace TfLens.Core.AppManager;

/// <summary>
/// One row of the Usage Guide's Test-users table that can actually sign in.
/// </summary>
/// <param name="Row">The row number printed in the guide's <c>#</c> column, for reporting.</param>
/// <param name="Email">The account's email address.</param>
/// <param name="Password">The password the guide records for it.</param>
public sealed record TestAccount(int Row, string Email, string Password)
{
    /// <summary>
    /// The given name used when the account has to be re-created.
    /// </summary>
    /// <remarks>
    /// Every documented account belongs to the same fictional tester, so the given name is constant and
    /// only the family name varies. It is used on registration only — an account that already exists is
    /// never renamed, which is what keeps provisioning idempotent.
    /// </remarks>
    public string FirstName => "TfLens";

    /// <summary>
    /// The family name used when the account has to be re-created.
    /// </summary>
    /// <remarks>
    /// Derived from the email's local part with the shared <c>tflens</c> prefix removed and the
    /// remainder title-cased, so <c>tflensdemo@…</c> re-registers as "TfLens Demo" — the display name
    /// the live account already carries, and the one <c>AppManagerLiveTests</c> asserts on.
    /// </remarks>
    public string LastName
    {
        get
        {
            var vLocal = Email.Split('@')[0];

            if (vLocal.StartsWith("tflens", StringComparison.OrdinalIgnoreCase))
            {
                vLocal = vLocal[6..];
            }

            return vLocal.Length == 0
                ? "Test"
                : char.ToUpperInvariant(vLocal[0]) + vLocal[1..];
        }
    }
}

/// <summary>
/// Reads the canonical test accounts out of <c>docs/TfLens-UsageGuide.md</c> (REQ-NFR-012).
/// </summary>
/// <remarks>
/// <para>
/// The Usage Guide's Test-users table is the <b>single source</b> for the accounts the suite signs in
/// with. Nothing else may carry a credential: the accounts live on a shared external service
/// (AppManager, Application 1), so a password that exists only in someone's shell can be rotated away
/// with no way to restore it. That is exactly how the authenticated half of the suite came to be
/// blocked on 2026-08-28 (<c>MISS-TfLens-20260828-02</c>).
/// </para>
/// <para>
/// This parser is what makes the table load-bearing rather than decorative. The
/// <c>provision-test-accounts</c> verb reads it to restore the accounts, and a guardrail test reads it
/// to fail the build when the suite names an account the guide does not list.
/// </para>
/// </remarks>
public static class TestAccountRegistry
{
    /// <summary>The guide's path, relative to the repository root.</summary>
    public const string GuideRelativePath = "docs/TfLens-UsageGuide.md";

    /// <summary>The file that marks the repository root.</summary>
    private const string SolutionFileName = "TfLens.slnx";

    /// <summary>Column headings that identify the Test-users table among the guide's other tables.</summary>
    private static readonly string[] TableHeadings = ["Username / Email", "Password"];

    /// <summary>Matches an email address inside a table cell.</summary>
    private static readonly Regex EmailPattern = new(
        @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Finds the Usage Guide by walking up from a starting directory to the repository root.
    /// </summary>
    /// <param name="aStartDirectory">Where to start looking; normally the running binary's folder.</param>
    /// <returns>The absolute path of the guide.</returns>
    /// <exception cref="FileNotFoundException">No ancestor directory holds the solution file.</exception>
    public static string LocateGuide(string aStartDirectory)
    {
        var vDirectory = new DirectoryInfo(aStartDirectory);

        while (vDirectory is not null)
        {
            if (File.Exists(Path.Combine(vDirectory.FullName, SolutionFileName)))
            {
                return Path.Combine(vDirectory.FullName, "docs", "TfLens-UsageGuide.md");
            }

            vDirectory = vDirectory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {SolutionFileName} above '{aStartDirectory}', so {GuideRelativePath} " +
            "could not be located. Pass --guide <path> to name it explicitly.");
    }

    /// <summary>
    /// Reads every account the guide lists with both an email address and a password.
    /// </summary>
    /// <remarks>
    /// Rows without a usable credential — the anonymous-visitor row, for instance — are skipped rather
    /// than reported as broken: they are legitimate entries in the table that simply cannot sign in.
    /// </remarks>
    /// <param name="aGuidePath">The absolute path of <c>TfLens-UsageGuide.md</c>.</param>
    /// <returns>The accounts, in table order.</returns>
    /// <exception cref="FileNotFoundException">The guide is not at that path.</exception>
    /// <exception cref="InvalidOperationException">The guide holds no Test-users table.</exception>
    public static IReadOnlyList<TestAccount> Read(string aGuidePath)
    {
        if (!File.Exists(aGuidePath))
        {
            throw new FileNotFoundException(
                $"The Usage Guide is the single source for the test accounts and it is not at " +
                $"'{aGuidePath}'.",
                aGuidePath);
        }

        var vLines = File.ReadAllLines(aGuidePath);
        var vHeaderIndex = Array.FindIndex(
            vLines,
            aLine => TableHeadings.All(aHeading => aLine.Contains(aHeading, StringComparison.Ordinal)));

        if (vHeaderIndex < 0)
        {
            throw new InvalidOperationException(
                $"'{aGuidePath}' holds no Test-users table — no row carries the headings " +
                $"{string.Join(" and ", TableHeadings)}.");
        }

        var vAccounts = new List<TestAccount>();

        foreach (var vRow in TableRows(vLines, vHeaderIndex))
        {
            if (TryReadAccount(vRow, out var vAccount))
            {
                vAccounts.Add(vAccount!);
            }
        }

        return vAccounts;
    }

    /// <summary>
    /// Reads every email address the Test-users <b>table</b> lists, rows without a password included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guardrail that pins REQ-NFR-012 asks "is this account documented?", not "can it sign in?",
    /// so it needs the addresses rather than the credentials.
    /// </para>
    /// <para>
    /// Scoped to the table on purpose, not to the whole file. The guide's prose mentions accounts that
    /// have been <i>retired</i> — the AM-001 reproduction account, for one — and a test still signing in
    /// with a retired account must fail, not be excused by the sentence that explains why it was
    /// removed.
    /// </para>
    /// </remarks>
    /// <param name="aGuidePath">The absolute path of <c>TfLens-UsageGuide.md</c>.</param>
    /// <returns>The addresses, lower-cased, without duplicates.</returns>
    public static IReadOnlySet<string> ReadDocumentedEmails(string aGuidePath)
    {
        var vLines = File.ReadAllLines(aGuidePath);
        var vHeaderIndex = Array.FindIndex(
            vLines,
            aLine => TableHeadings.All(aHeading => aLine.Contains(aHeading, StringComparison.Ordinal)));

        if (vHeaderIndex < 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return TableRows(vLines, vHeaderIndex)
            .SelectMany(aRow => EmailPattern.Matches(aRow).Select(aMatch => aMatch.Value.ToLowerInvariant()))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Yields the data rows of the table whose header sits at a known line.
    /// </summary>
    /// <remarks>
    /// The header row is followed by the markdown separator, then the data rows; the table ends at the
    /// first line that is not a table row.
    /// </remarks>
    /// <param name="aLines">Every line of the guide.</param>
    /// <param name="aHeaderIndex">The index of the header row.</param>
    /// <returns>Each trimmed data row, pipes included.</returns>
    private static IEnumerable<string> TableRows(string[] aLines, int aHeaderIndex)
    {
        for (var vIndex = aHeaderIndex + 2; vIndex < aLines.Length; vIndex++)
        {
            var vLine = aLines[vIndex].Trim();

            if (!vLine.StartsWith('|'))
            {
                yield break;
            }

            yield return vLine;
        }
    }

    /// <summary>
    /// Turns one table row into an account, when it carries both an address and a password.
    /// </summary>
    /// <param name="aLine">The trimmed table row, pipes included.</param>
    /// <param name="aAccount">The account read, or <c>null</c>.</param>
    /// <returns><c>true</c> when the row describes an account that can sign in.</returns>
    private static bool TryReadAccount(string aLine, out TestAccount? aAccount)
    {
        aAccount = null;

        var vCells = aLine
            .Trim('|')
            .Split('|')
            .Select(aCell => aCell.Trim())
            .ToArray();

        if (vCells.Length < 3)
        {
            return false;
        }

        var vEmail = EmailPattern.Match(vCells[1]);
        if (!vEmail.Success)
        {
            return false;
        }

        var vPassword = vCells[2].Trim('`', '*', ' ');
        if (vPassword.Length == 0 || vPassword is "—" or "-" or "n/a")
        {
            return false;
        }

        _ = int.TryParse(vCells[0], out var vRow);

        aAccount = new TestAccount(vRow, vEmail.Value.ToLowerInvariant(), vPassword);
        return true;
    }
}
