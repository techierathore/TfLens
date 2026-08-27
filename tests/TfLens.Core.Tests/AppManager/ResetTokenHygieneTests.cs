using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// REQ-FN-003 / BRD-92, proved against the source rather than against a run.
/// </summary>
/// <remarks>
/// <para>
/// Two of this requirement's clauses are statements about every code path, including the ones no test
/// reaches: "the reset token is never logged", and "the forgot response is identical for known and
/// unknown addresses". A behavioural test can only ever say <i>the paths I exercised</i> behaved — so
/// these are checked the way <c>ForbiddenServiceTests</c> checks its claim, by reading the code that
/// ships.
/// </para>
/// <para>
/// The scan reads whole call expressions rather than single lines. A logging call wrapped across four
/// lines is the normal shape in this codebase, and a line-at-a-time scan silently misses every
/// argument after the first — which is exactly where a token would sit.
/// </para>
/// </remarks>
public sealed class ResetTokenHygieneTests
{
    /// <summary>Call shapes that put text somewhere a human, a file or a sink can read it.</summary>
    private static readonly Regex EmittingCall = new(
        @"\bLog(?:Trace|Debug|Information|Warning|Error|Critical)\s*\(" +
        @"|\b(?:Log|Logger|objLogger|vLogger)\s*\.\s*(?:Verbose|Debug|Information|Warning|Error|Fatal)\s*\(" +
        @"|\bConsole\s*\.\s*(?:Write|WriteLine|Error)\s*\(",
        RegexOptions.Compiled);

    /// <summary>Any identifier whose value could be a token.</summary>
    private static readonly Regex TokenIdentifier = new(@"\b\w*Token\w*\b", RegexOptions.Compiled);

    /// <summary>
    /// Token-shaped names that demonstrably do not hold a credential.
    /// </summary>
    /// <remarks>
    /// A cancellation token is a signal, an antiforgery token is public by design and already in the
    /// page, and <c>TokenExpiresAt</c> is a timestamp. Without these the scan reports noise, and a
    /// check nobody trusts is a check nobody reads.
    /// </remarks>
    private static readonly string[] BenignTokenNames =
    [
        "CancellationToken",
        "aCancellationToken",
        "vCancellationToken",
        "objCancellationToken",
        "AntiforgeryToken",
        "RequestVerificationToken",
        "TokenExpiresAt",
        "TokenExpiry"
    ];

    /// <summary>The file that owns the reset call to AppManager.</summary>
    private static readonly string[] ClientFile = ["src", "TfLens.Core", "AppManager", "AppManagerClient.cs"];

    /// <summary>The file that owns the two reset endpoints.</summary>
    private static readonly string[] EndpointsFile = ["src", "TfLens", "Services", "Auth", "AuthEndpoints.cs"];

    /// <summary>The page the emailed link lands on.</summary>
    private static readonly string[] ResetPageFile =
        ["src", "TfLens", "Components", "Pages", "Auth", "ResetPassword.razor"];

    /// <summary>No emitting call anywhere in the product can carry a token value.</summary>
    [Fact]
    public void NoEmittingCallInTheProductCanCarryAToken()
    {
        var vOffenders = new List<string>();

        foreach (var vFile in SourceFiles())
        {
            var vText = File.ReadAllText(vFile);

            foreach (var vCall in EmittingCalls(vText))
            {
                var vResidue = StripLiterals(vCall);

                vOffenders.AddRange(
                    TokenIdentifier.Matches(vResidue)
                        .Select(aMatch => aMatch.Value)
                        .Where(aName => !BenignTokenNames.Contains(aName, StringComparer.Ordinal))
                        .Select(aName => $"{Relative(vFile)} emits '{aName}' in: {Flatten(vCall)}"));
            }
        }

        vOffenders.Should().BeEmpty(
            "a reset token in a log line is a working password-reset link sitting in a log file");
    }

    /// <summary>The reset page never renders the token it was handed.</summary>
    /// <remarks>
    /// The token arrives in the query string, which is already the weakest link in the flow. Echoing it
    /// into the DOM — a value, a hidden input, a message — puts it into the page source, into the
    /// browser's view-source cache and into any screenshot of the page.
    /// </remarks>
    [Fact]
    public void TheResetPageNeverRendersTheToken()
    {
        var vText = File.ReadAllText(PathOf(ResetPageFile));
        var vMarkup = vText[..MarkupLength(vText)];

        Regex.Matches(vMarkup, @"@\(?\w*Token\w*")
            .Select(aMatch => aMatch.Value)
            .Should().BeEmpty("the token must never reach the DOM");

        vMarkup.Should().NotContain("value=\"@Token\"");
        vMarkup.Should().NotContain("type=\"hidden\"");
    }

    /// <summary>The reset form declares its own action, so the framework cannot stamp the link into it.</summary>
    /// <remarks>
    /// This encodes a bug that shipped. An interactive Blazor form with no <c>action</c> is rendered
    /// carrying the current request URL — on this page the emailed link, token and all — so the token
    /// reached the page source even though no expression in the file ever rendered it. Nothing in the
    /// component reads the attribute, which is exactly why it is easy to delete as noise; this is the
    /// note that says it must stay.
    /// </remarks>
    [Fact]
    public void TheResetFormDeclaresAStaticActionSoTheLinkIsNotStampedIntoIt()
    {
        var vText = File.ReadAllText(PathOf(ResetPageFile));
        var vForm = vText[vText.IndexOf("<form", StringComparison.Ordinal)..];

        Regex.Match(vForm[..vForm.IndexOf('>')], @"action\s*=\s*""(?<target>[^""]*)""")
            .Groups["target"].Value
            .Should().Be("/reset-password", "an action-less interactive form is rendered with the request URL");
    }

    /// <summary>The token never becomes part of a URL TfLens builds.</summary>
    /// <remarks>
    /// A token in a redirect target is recorded by every proxy, browser history and access log on the
    /// way — a leak that no care inside the process can undo. The endpoints redirect with an opaque
    /// reason word and nothing else.
    /// </remarks>
    [Fact]
    public void TheResetTokenNeverReachesARedirectTarget()
    {
        var vOffenders = new List<string>();

        foreach (var vFile in new[] { PathOf(EndpointsFile), PathOf(ResetPageFile), PathOf(ClientFile) })
        {
            var vText = File.ReadAllText(vFile);

            foreach (var vCall in CallsTo(vText, @"\bResults\s*\.\s*Redirect\s*\(|\bNavigateTo\s*\(|\bBack\s*\("))
            {
                vOffenders.AddRange(
                    TokenIdentifier.Matches(StripLiterals(vCall))
                        .Select(aMatch => aMatch.Value)
                        .Where(aName => !BenignTokenNames.Contains(aName, StringComparer.Ordinal))
                        .Select(aName => $"{Relative(vFile)} navigates with '{aName}': {Flatten(vCall)}"));
            }
        }

        vOffenders.Should().BeEmpty();
    }

    /// <summary>
    /// The forgot-password endpoint cannot branch on whether the address exists.
    /// </summary>
    /// <remarks>
    /// This is enumeration safety stated structurally. Once the antiforgery check has passed the handler
    /// makes one call and performs one redirect: there is no <c>try</c>, no <c>catch</c>, no <c>if</c>
    /// and no second <c>return</c> after the call, so no answer AppManager could give — and no time it
    /// could take to give it — can steer the response down a different path.
    /// </remarks>
    [Fact]
    public void TheForgotPasswordEndpointHasNoBranchAfterTheAppManagerCall()
    {
        var vBody = MethodBody(File.ReadAllText(PathOf(EndpointsFile)), "ForgotPasswordAsync");
        var vAfterCall = vBody[(vBody.IndexOf("ForgotPasswordAsync(", StringComparison.Ordinal) + 1)..];

        vAfterCall.Should().NotContain("if (", "a branch after the call is a channel the answer can travel down");
        vAfterCall.Should().NotContain("catch", "catching here would let the failure shape the response");
        vAfterCall.Should().NotContain("switch");

        Regex.Matches(vAfterCall, @"\breturn\b").Count
            .Should().Be(1, "exactly one response leaves this handler once the address has been submitted");

        Regex.Matches(vBody, @"Results\s*\.\s*Redirect\s*\(\s*""/forgot-password\?sent=1""").Count
            .Should().Be(1, "the one outcome is the neutral one");
    }

    /// <summary>
    /// The client's forgot-password call cannot fail outwards.
    /// </summary>
    /// <remarks>
    /// The endpoint's single path is only enumeration-safe if the call it makes cannot throw. The
    /// client therefore catches the typed exception itself and returns nothing at all, so there is no
    /// value and no exception type in which "this address exists" could be encoded.
    /// </remarks>
    [Fact]
    public void TheClientForgotPasswordCallSwallowsItsFailure()
    {
        var vBody = MethodBody(File.ReadAllText(PathOf(ClientFile)), "ForgotPasswordAsync");

        vBody.Should().Contain("catch (AppManagerException");
        vBody.Should().NotContain("throw");
        vBody.Should().NotContain("return ");
    }

    /// <summary>
    /// Both dead-link codes are recognised together wherever a reset failure is turned into an outcome.
    /// </summary>
    /// <remarks>
    /// The endpoint maps them onto one reason word and the page maps them onto one sentence. Either one
    /// drifting apart from the other would reintroduce the distinction the requirement removes, so both
    /// files are required to name both codes.
    /// </remarks>
    [Fact]
    public void BothDeadLinkCodesAreHandledTogetherEverywhereAResetCanFail()
    {
        foreach (var vFile in new[] { PathOf(EndpointsFile), PathOf(ResetPageFile) })
        {
            var vText = File.ReadAllText(vFile);

            vText.Should().Contain("InvalidResetToken", $"{Relative(vFile)} must recognise a stale link");
            vText.Should().Contain("AppIdMismatch", $"{Relative(vFile)} must recognise a wrong-tenant link");
        }
    }

    /// <summary>
    /// Extracts every emitting call expression from a source file.
    /// </summary>
    /// <param name="aText">The file text.</param>
    /// <returns>Each call, from the method name to its matching close parenthesis.</returns>
    private static IEnumerable<string> EmittingCalls(string aText) => CallsTo(aText, EmittingCall.ToString());

    /// <summary>
    /// Extracts every call matching a pattern, balanced across lines.
    /// </summary>
    /// <param name="aText">The file text.</param>
    /// <param name="aPattern">A pattern whose match ends at the call's opening parenthesis.</param>
    /// <returns>Each whole call expression.</returns>
    private static IEnumerable<string> CallsTo(string aText, string aPattern)
    {
        foreach (Match vMatch in Regex.Matches(aText, aPattern))
        {
            var vOpen = aText.IndexOf('(', vMatch.Index);
            if (vOpen < 0)
            {
                continue;
            }

            var vClose = MatchingParenthesis(aText, vOpen);
            if (vClose > vOpen)
            {
                yield return aText[vMatch.Index..(vClose + 1)];
            }
        }
    }

    /// <summary>
    /// Finds the parenthesis that closes the one at a position, ignoring those inside literals.
    /// </summary>
    /// <param name="aText">The file text.</param>
    /// <param name="aOpen">Index of the opening parenthesis.</param>
    /// <returns>Index of the matching close, or -1 when the file ends first.</returns>
    private static int MatchingParenthesis(string aText, int aOpen)
    {
        var vDepth = 0;
        var vIsInString = false;
        var vIsInChar = false;

        for (var vIndex = aOpen; vIndex < aText.Length; vIndex++)
        {
            var vCharacter = aText[vIndex];

            if (vCharacter == '\\' && (vIsInString || vIsInChar))
            {
                vIndex++;
                continue;
            }

            if (vCharacter == '"' && !vIsInChar)
            {
                vIsInString = !vIsInString;
                continue;
            }

            if (vCharacter == '\'' && !vIsInString)
            {
                vIsInChar = !vIsInChar;
                continue;
            }

            if (vIsInString || vIsInChar)
            {
                continue;
            }

            if (vCharacter == '(')
            {
                vDepth++;
            }
            else if (vCharacter == ')' && --vDepth == 0)
            {
                return vIndex;
            }
        }

        return -1;
    }

    /// <summary>
    /// Returns the body of a named method, braces included.
    /// </summary>
    /// <param name="aText">The file text.</param>
    /// <param name="aName">The method name.</param>
    /// <returns>The method body.</returns>
    /// <exception cref="InvalidOperationException">The method was renamed or removed.</exception>
    /// <remarks>
    /// The declaration is located by its signature rather than by any call to it, so a rename breaks
    /// this test loudly instead of letting it quietly scan nothing.
    /// </remarks>
    private static string MethodBody(string aText, string aName)
    {
        var vDeclaration = Regex.Match(aText, $@"(?:private|public|internal|protected)[^\n]*\b{aName}\s*\(");

        if (!vDeclaration.Success)
        {
            throw new InvalidOperationException($"No declaration of {aName} was found; the scan would prove nothing.");
        }

        var vOpen = aText.IndexOf('{', MatchingParenthesis(aText, aText.IndexOf('(', vDeclaration.Index)));
        var vDepth = 0;

        for (var vIndex = vOpen; vIndex < aText.Length; vIndex++)
        {
            if (aText[vIndex] == '{')
            {
                vDepth++;
            }
            else if (aText[vIndex] == '}' && --vDepth == 0)
            {
                return aText[vOpen..(vIndex + 1)];
            }
        }

        throw new InvalidOperationException($"The body of {aName} is not balanced.");
    }

    /// <summary>
    /// Replaces the contents of every string literal with a placeholder.
    /// </summary>
    /// <param name="aText">The text to strip.</param>
    /// <returns>The same text with literal contents removed, so prose cannot match an identifier.</returns>
    private static string StripLiterals(string aText)
    {
        var vBuilder = new StringBuilder(aText.Length);
        var vIsInString = false;

        for (var vIndex = 0; vIndex < aText.Length; vIndex++)
        {
            var vCharacter = aText[vIndex];

            if (vCharacter == '\\' && vIsInString)
            {
                vIndex++;
                continue;
            }

            if (vCharacter == '"')
            {
                vIsInString = !vIsInString;
                vBuilder.Append('"');
                continue;
            }

            if (!vIsInString)
            {
                vBuilder.Append(vCharacter);
            }
        }

        return vBuilder.ToString();
    }

    /// <summary>Puts a multi-line call on one line, so a failure message stays readable.</summary>
    /// <param name="aCall">The captured call.</param>
    /// <returns>The call as a single line.</returns>
    private static string Flatten(string aCall) =>
        Regex.Replace(aCall, @"\s+", " ").Trim();

    /// <summary>Length of a Razor file's markup, which ends where its code block begins.</summary>
    /// <param name="aText">The Razor file text.</param>
    /// <returns>The number of leading characters that are markup.</returns>
    private static int MarkupLength(string aText)
    {
        var vCodeBlock = aText.IndexOf("@code", StringComparison.Ordinal);
        return vCodeBlock < 0 ? aText.Length : vCodeBlock;
    }

    /// <summary>
    /// Enumerates every source file the product ships, skipping build output.
    /// </summary>
    /// <returns>Absolute paths of the files to scan.</returns>
    private static IEnumerable<string> SourceFiles()
    {
        var vSource = Path.Combine(RepositoryRoot(), "src");

        return Directory.EnumerateFiles(vSource, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(vSource, "*.razor", SearchOption.AllDirectories))
            .Where(aFile => !aFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !aFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    /// <summary>Builds an absolute path from repository-relative segments.</summary>
    /// <param name="aSegments">The path segments below the repository root.</param>
    /// <returns>The absolute path.</returns>
    private static string PathOf(string[] aSegments) =>
        Path.Combine([RepositoryRoot(), .. aSegments]);

    /// <summary>Renders a path relative to the repository, for a readable failure message.</summary>
    /// <param name="aPath">The absolute path.</param>
    /// <returns>The repository-relative path.</returns>
    private static string Relative(string aPath) =>
        Path.GetRelativePath(RepositoryRoot(), aPath).Replace('\\', '/');

    /// <summary>
    /// Locates the repository root, which is what this test actually scans.
    /// </summary>
    /// <returns>The absolute repository root.</returns>
    /// <exception cref="InvalidOperationException">The repository could not be located.</exception>
    private static string RepositoryRoot() =>
        FindUpwards(Environment.GetEnvironmentVariable("TfLensRepoRoot"))
        ?? FindUpwards(AppContext.BaseDirectory)
        ?? FindUpwards(Directory.GetCurrentDirectory())
        ?? throw new InvalidOperationException("The repository root could not be located from the test assembly.");

    /// <summary>
    /// Walks up from a directory looking for the solution file.
    /// </summary>
    /// <param name="aStart">Where to start, or <c>null</c> to skip.</param>
    /// <returns>The repository root, or <c>null</c> when this branch does not reach it.</returns>
    private static string? FindUpwards(string? aStart)
    {
        if (string.IsNullOrWhiteSpace(aStart) || !Directory.Exists(aStart))
        {
            return null;
        }

        var vDirectory = new DirectoryInfo(aStart);

        while (vDirectory is not null)
        {
            if (File.Exists(Path.Combine(vDirectory.FullName, "TfLens.slnx")))
            {
                return vDirectory.FullName;
            }

            vDirectory = vDirectory.Parent;
        }

        return null;
    }
}
