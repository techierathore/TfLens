using FluentAssertions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// Every non-paged <c>DataTable</c> declares an explicit <c>InitialPageSize</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ShowPagination="false"</c> does <b>not</b> disable paging in TrBlazeUI 2.0.0 — it only hides the
/// pager. The table still renders <c>InitialPageSize</c> rows, default <b>5</b>, and the remainder are
/// simply absent from the DOM with no error and no visual hint (logged as TR-009).
/// </para>
/// <para>
/// This shipped twice during the build: an eight-row gate-distribution table rendered five rows and
/// looked entirely correct, and the profile table had exactly five rows — one field away from silently
/// losing data. For a product whose whole purpose is to not show a plausible wrong number, a table that
/// quietly drops rows is the worst available defect, so it is pinned here rather than left to review.
/// </para>
/// </remarks>
public sealed class DataTablePagingTests
{
    /// <summary>
    /// No <c>DataTable</c> in the app relies on the default page size.
    /// </summary>
    [Fact]
    public void EveryDataTableDeclaresAnExplicitPageSize()
    {
        var vOffenders = new List<string>();

        foreach (var vFile in Directory.EnumerateFiles(ComponentsRoot(), "*.razor", SearchOption.AllDirectories))
        {
            var vText = File.ReadAllText(vFile);
            var vName = Path.GetRelativePath(ComponentsRoot(), vFile);

            var vIndex = 0;
            while ((vIndex = vText.IndexOf("<DataTable", vIndex, StringComparison.Ordinal)) >= 0)
            {
                var vClose = vText.IndexOf('>', vIndex);
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

                if (vIsTableElement && !vTag.Contains("InitialPageSize", StringComparison.Ordinal))
                {
                    var vLine = vText[..vIndex].Count(aC => aC == '\n') + 1;
                    vOffenders.Add($"{vName}:{vLine}");
                }

                vIndex = vClose;
            }
        }

        vOffenders.Should().BeEmpty(
            "a DataTable without an explicit InitialPageSize silently renders only 5 rows — " +
            "ShowPagination=\"false\" hides the pager but does not disable paging (TR-009). " +
            "Offenders: " + string.Join(", ", vOffenders));
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
