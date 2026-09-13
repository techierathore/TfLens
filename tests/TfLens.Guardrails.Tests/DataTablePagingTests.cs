using FluentAssertions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// Every <c>DataTable</c> states its paging intent: either <c>ShowPagination="false"</c> (render the
/// whole collection) or an explicit <c>InitialPageSize</c> (page it at a size someone chose).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this guards, and why the assertion changed on 2026-09-12.</b> The danger has always been the
/// same: a table that silently renders fewer rows than it was given, with no error and no visual hint.
/// It shipped twice during the build — an eight-row gate-distribution table rendered five rows and
/// looked entirely correct, and the profile table had exactly five rows, one field away from quietly
/// losing data. For a product whose whole purpose is to not show a plausible wrong number, that is the
/// worst available defect, so it is pinned here rather than left to review.
/// </para>
/// <para>
/// <b>The mechanism, however, is gone.</b> Under TrBlazeUI 2.0.0, <c>ShowPagination="false"</c> hid the
/// pager without disabling paging: the table still rendered <c>InitialPageSize</c> rows, default
/// <b>5</b>, and dropped the rest (logged as TR-009). The only defence was to declare an
/// <c>InitialPageSize</c> above the row count on every unpaged table, which is what this test used to
/// require. TR-009 is fixed in <b>2.1.0-ci.10</b>: <c>ShowPagination="false"</c> is now a data switch,
/// and such a table renders every row. Those inflated page sizes were therefore deleted across the app
/// in the same pass, and the old assertion — "every <c>DataTable</c> declares <c>InitialPageSize</c>" —
/// began failing on 29 correct call sites at once.
/// </para>
/// <para>
/// So the assertion now tests the intent rather than the workaround. A table must say which of the two
/// it wants. What it may not do is leave both unstated and inherit a page size of 5 that nobody chose —
/// which is still a silent truncation, and still the thing this test exists to catch.
/// </para>
/// </remarks>
public sealed class DataTablePagingTests
{
    /// <summary>
    /// No <c>DataTable</c> in the app relies on the default page size.
    /// </summary>
    [Fact]
    public void EveryDataTableStatesItsPagingIntent()
    {
        var vOffenders = new List<string>();

        foreach (var vFile in Directory.EnumerateFiles(ComponentsRoot(), "*.razor", SearchOption.AllDirectories))
        {
            var vText = File.ReadAllText(vFile);
            var vName = Path.GetRelativePath(ComponentsRoot(), vFile);

            var vIndex = 0;
            while ((vIndex = vText.IndexOf("<DataTable", vIndex, StringComparison.Ordinal)) >= 0)
            {
                var vClose = EndOfTag(vText, vIndex);
                if (vClose < 0)
                {
                    break;
                }

                var vTag = vText[vIndex..vClose];

                // "<DataTable" is also the prefix of "<DataTableColumn", which takes no page size —
                // only the element whose name ends right there is the table itself.
                var vAfterName = vIndex + "<DataTable".Length;
                var vIsTableElement = vAfterName >= vText.Length
                                      || vText[vAfterName] is ' ' or '\r' or '\n' or '\t' or '>' or '/';

                // Either intent is acceptable; stating neither is not. See the remarks above — under
                // 2.1.0-ci.10 ShowPagination="false" renders the whole collection, so it no longer
                // needs an InitialPageSize propping it up, but a table that declares neither still
                // inherits a page size of 5 that nobody chose.
                var vRendersEveryRow = vTag.Contains("ShowPagination=\"false\"", StringComparison.Ordinal);
                var vDeclaresPageSize = vTag.Contains("InitialPageSize", StringComparison.Ordinal);

                if (vIsTableElement && !vRendersEveryRow && !vDeclaresPageSize)
                {
                    var vLine = vText[..vIndex].Count(aC => aC == '\n') + 1;
                    vOffenders.Add($"{vName}:{vLine}");
                }

                vIndex = vClose;
            }
        }

        vOffenders.Should().BeEmpty(
            "a DataTable that declares neither ShowPagination=\"false\" nor an explicit " +
            "InitialPageSize inherits a page size of 5 that nobody chose, and silently renders only " +
            "the first 5 rows. Say which you mean: ShowPagination=\"false\" to render the whole " +
            "collection, or InitialPageSize to page it. Offenders: " + string.Join(", ", vOffenders));
    }

    /// <summary>
    /// Index of the '&gt;' that closes the element tag opening at <paramref name="aStart"/>, skipping
    /// any '&gt;' inside a quoted attribute value.
    /// </summary>
    /// <remarks>
    /// A naive <c>IndexOf('&gt;')</c> stops at the arrow of a Razor lambda — <c>PreprocessData="@(aRows
    /// =&gt; …)"</c> — and truncates the tag before the attributes that follow it, so a table really
    /// carrying <c>ShowPagination="false"</c> would be reported as an offender. The bug was latent in
    /// the original scanner and no call site happened to trip it; it is closed here rather than left
    /// for the first person who writes that attribute.
    /// </remarks>
    /// <param name="aText">The full file text.</param>
    /// <param name="aStart">Index of the opening '&lt;'.</param>
    /// <returns>The index of the closing '&gt;', or -1 when the tag is unterminated.</returns>
    private static int EndOfTag(string aText, int aStart)
    {
        var vInQuote = '\0';

        for (var vI = aStart; vI < aText.Length; vI++)
        {
            var vC = aText[vI];

            if (vInQuote != '\0')
            {
                if (vC == vInQuote)
                {
                    vInQuote = '\0';
                }
            }
            else if (vC is '"' or '\'')
            {
                vInQuote = vC;
            }
            else if (vC == '>')
            {
                return vI;
            }
        }

        return -1;
    }

    /// <summary>Absolute path of the head's components folder.</summary>
    /// <returns>The directory holding every <c>.razor</c> file in the app.</returns>
    private static string ComponentsRoot() =>
        Path.Combine(RepositoryRoot(), "src", "TfLens", "Components");

    /// <summary>Walks up from the test binary to the repository root.</summary>
    /// <returns>The directory holding the solution file.</returns>
    /// <exception cref="InvalidOperationException">The root could not be located.</exception>
    private static string RepositoryRoot()
    {
        var vDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (vDirectory is not null && vDirectory.GetFiles("TfLens.slnx").Length == 0)
        {
            vDirectory = vDirectory.Parent;
        }

        return vDirectory?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
