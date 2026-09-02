using System.Text.RegularExpressions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-021 — a scoped-CSS rule that can never match is a build failure, not a silent no-op.
/// </summary>
/// <remarks>
/// <para>
/// Blazor rewrites every selector in <c>X.razor.css</c> to carry <c>X</c>'s own <c>b-…</c> scope
/// attribute, and that attribute is stamped ONLY on elements authored in <c>X.razor</c>. A rule aimed at
/// markup some other component renders therefore compiles to a selector matching nothing: no build error,
/// no console warning, no failing test, and the styling simply is not there. The element still exists,
/// still carries text and still does not overlap anything, so acceptance, the data-render gate and the
/// visual-truth gate all pass on it.
/// </para>
/// <para>
/// On 2026-08-30 that cost three separate fixes in one day, each found only by eye:
/// <c>.tflens-measured</c> and <c>.tflens-estimate</c> in <c>Misses.razor.css</c> aimed at
/// <c>StatTile</c>'s card root (BRD-123's dashed estimate border had never once painted, so an estimate
/// tile was styled identically to a measured one), and <c>.tflens-stat-chip</c> / <c>.tflens-accent-N</c>
/// used from <c>Harness.razor</c> while declared in <c>StatTile.razor.css</c> — the missing coloured
/// chips the owner reported. A fourth was found on 2026-09-01 by the parse check below.
/// </para>
/// <para>
/// These are static checks rather than runtime ones for the reason every guardrail in this project is:
/// "no rule in this stylesheet is dead" cannot be shown by exercising the rules that do work.
/// </para>
/// </remarks>
public sealed class ScopedCssTests
{
    /// <summary>At-rules whose block holds ordinary rules, so the check descends into them.</summary>
    private static readonly string[] TransparentAtRules =
        ["@media", "@supports", "@container", "@layer", "@scope"];

    /// <summary>
    /// At-rules whose block is not a selector context at all — the check steps over them whole.
    /// </summary>
    /// <remarks>
    /// A <c>@keyframes</c> block's preludes are percentages, and <c>@font-face</c> / <c>@property</c>
    /// carry only declarations. Blazor does not scope any of them, so there is nothing here to be dead.
    /// </remarks>
    private static readonly string[] OpaqueAtRules =
        ["@keyframes", "@font-face", "@property", "@counter-style", "@page", "@import", "@charset"];

    /// <summary>Class tokens in a selector.</summary>
    private static readonly Regex ClassToken = new(@"\.(-?[_a-zA-Z][\w-]*)", RegexOptions.Compiled);

    /// <summary>
    /// Every scoped stylesheet is well-formed CSS — balanced comments and balanced braces.
    /// </summary>
    /// <remarks>
    /// This is the cheapest of the three checks and it caught a live defect the moment it was written.
    /// <c>Harness.razor.css</c> carried a ten-line note about the label column that had been pasted
    /// AFTER the <c>*/</c> closing its comment and before a second, unopened <c>*/</c>. The browser read
    /// that English prose as the beginning of a selector and swallowed
    /// <c>.tflens-kv ::deep td:first-child</c> into a compound that matched nothing — so the column
    /// widening the note describes had never been applied in any browser, in any environment, while the
    /// checklist recorded it as fixed. Exactly the REQ-NFR-021 shape, arriving through the parser rather
    /// than through scoping.
    /// </remarks>
    [Fact]
    public void EveryScopedStylesheetParses()
    {
        var vFindings = new List<string>();

        foreach (var vPath in RepoTree.Files("*.razor.css", "src"))
        {
            var vText = File.ReadAllText(vPath);
            var vRelative = RepoTree.Relative(vPath);

            vFindings.AddRange(CommentFaults(vText, vRelative));

            var vStripped = StripComments(vText);
            var vDepth = 0;
            var vDeepest = 0;

            foreach (var vChar in vStripped)
            {
                if (vChar == '{')
                {
                    vDepth++;
                    vDeepest = Math.Max(vDeepest, vDepth);
                }
                else if (vChar == '}')
                {
                    vDepth--;

                    if (vDepth < 0)
                    {
                        vFindings.Add($"{vRelative} — a `}}` closes a block that was never opened.");
                        break;
                    }
                }
            }

            if (vDepth > 0)
            {
                vFindings.Add($"{vRelative} — {vDepth} block(s) left open at end of file.");
            }

            _ = vDeepest;
        }

        Assert.True(
            vFindings.Count == 0,
            $"REQ-NFR-021 — {vFindings.Count} malformed scoped stylesheet(s). Everything after the fault "
            + $"is parsed as part of one broken selector and silently never applies:{Environment.NewLine}"
            + string.Join(Environment.NewLine, vFindings));
    }

    /// <summary>
    /// Every class a scoped rule targets on its own component's markup is actually written in that
    /// component's <c>.razor</c>.
    /// </summary>
    /// <remarks>
    /// Only the part of a selector BEFORE <c>::deep</c> is judged: that is the part Blazor stamps with
    /// this component's scope attribute, so that is the part that can be dead. Anything after
    /// <c>::deep</c> is matched inside a child's markup, which this file cannot see and this test does
    /// not pretend to.
    /// </remarks>
    [Fact]
    public void EveryScopedRuleCanMatchItsOwnComponent()
    {
        var vFindings = new List<string>();
        var vRazorByClass = ClassOwners();

        foreach (var vCssPath in RepoTree.Files("*.razor.css", "src"))
        {
            var vRazorPath = vCssPath[..^4];

            if (!File.Exists(vRazorPath))
            {
                continue;
            }

            var vCss = File.ReadAllText(vCssPath);
            var vRazor = File.ReadAllText(vRazorPath);
            var vExternal = DeclaredExternalClasses(vCss);
            var vCssRelative = RepoTree.Relative(vCssPath);
            var vRazorRelative = RepoTree.Relative(vRazorPath);

            foreach (var (vSelector, vLine) in Selectors(vCss))
            {
                foreach (var vClass in ScopedClassesOf(vSelector))
                {
                    if (MentionsClass(vRazor, vClass) || vExternal.Contains(vClass))
                    {
                        continue;
                    }

                    var vOwners = vRazorByClass.TryGetValue(vClass, out var vFound)
                        ? string.Join(", ", vFound)
                        : null;

                    var vWhy = vOwners is null
                        ? "no .razor in the tree writes it — the rule is dead everywhere"
                        : $"it is written in {vOwners}, whose markup carries a DIFFERENT scope attribute, "
                          + "so this rule can never match it — declare the rule in that component's own "
                          + ".razor.css";

                    vFindings.Add(
                        $"{vCssRelative}:{vLine} — `{vSelector.Trim()}` targets `.{vClass}`, which "
                        + $"{vRazorRelative} never writes: {vWhy}.");
                }
            }
        }

        Assert.True(
            vFindings.Count == 0,
            $"REQ-NFR-021 — {vFindings.Count} scoped rule(s) that can never match. Each one compiles "
            + $"cleanly, renders no warning, and simply does not style anything:{Environment.NewLine}"
            + string.Join(Environment.NewLine, vFindings));
    }

    /// <summary>
    /// Maps each class name to the <c>.razor</c> files that write it, for the failure message.
    /// </summary>
    /// <returns>Class name to owning component paths.</returns>
    private static Dictionary<string, List<string>> ClassOwners()
    {
        var vOwners = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var vCssPath in RepoTree.Files("*.razor.css", "src"))
        {
            var vRazorPath = vCssPath[..^4];

            if (!File.Exists(vRazorPath))
            {
                continue;
            }

            var vRazor = File.ReadAllText(vRazorPath);

            foreach (var (vSelector, _) in Selectors(File.ReadAllText(vCssPath)))

            {
                foreach (var vClass in ScopedClassesOf(vSelector))
                {
                    if (!MentionsClass(vRazor, vClass))
                    {
                        continue;
                    }

                    if (!vOwners.TryGetValue(vClass, out var vList))
                    {
                        vOwners[vClass] = vList = [];
                    }

                    vList.Add(RepoTree.Relative(vRazorPath));
                }
            }
        }

        return vOwners;
    }

    /// <summary>
    /// The classes in the part of a selector Blazor stamps with the authoring component's scope.
    /// </summary>
    /// <param name="aSelector">One selector from a comma-separated list.</param>
    /// <returns>The class names, without their leading dot.</returns>
    private static IEnumerable<string> ScopedClassesOf(string aSelector)
    {
        var vDeep = aSelector.IndexOf("::deep", StringComparison.Ordinal);
        var vScoped = vDeep < 0 ? aSelector : aSelector[..vDeep];

        return vScoped.Trim().Length == 0
            ? []
            : ClassToken.Matches(vScoped).Select(aMatch => aMatch.Groups[1].Value).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// Tells whether a component's markup names a class anywhere — attribute, helper string or all.
    /// </summary>
    /// <remarks>
    /// A whole-file substring match on purpose. A class reaches an element by several routes in Razor —
    /// a literal <c>class="…"</c>, a <c>Class</c> parameter, an interpolated expression, or a C# helper
    /// in <c>@code</c> that returns the name (<c>AccentClassFor</c> is one) — and a check that understood
    /// only the first would fail honest markup, which is the fastest way to get a guardrail deleted. The
    /// boundary test keeps <c>tflens-accent-1</c> from being satisfied by <c>tflens-accent-10</c>.
    /// </remarks>
    /// <param name="aRazor">The component's markup.</param>
    /// <param name="aClass">The class name, without its dot.</param>
    /// <returns><c>true</c> when the markup mentions it.</returns>
    private static bool MentionsClass(string aRazor, string aClass) =>
        WritesClassLiterally(aRazor, aClass) || BuildsClassByInterpolation(aRazor, aClass);

    /// <summary>
    /// Tells whether the markup builds this class name by interpolating a suffix onto a prefix.
    /// </summary>
    /// <remarks>
    /// The idiom this exists for is a per-series accent: <c>$"tflens-bar-{vIndex % 5 + 1}"</c> in
    /// <c>Routing.razor</c> against <c>.tflens-bar-1</c>..<c>.tflens-bar-5</c> in its stylesheet. The
    /// literal <c>tflens-bar-1</c> is nowhere in the markup, and the rules are entirely alive. Requiring
    /// the interpolation to open at a hyphen boundary keeps this from waving through a genuinely dead
    /// <c>.tflens-bar-9</c>: it cannot distinguish 9 from 1, but it can insist the family is constructed
    /// at all, which is the difference between a name a component builds and a name it never mentions.
    /// </remarks>
    /// <param name="aRazor">The component's markup.</param>
    /// <param name="aClass">The class name, without its dot.</param>
    /// <returns><c>true</c> when some prefix of the name is followed by an interpolation hole.</returns>
    private static bool BuildsClassByInterpolation(string aRazor, string aClass)
    {
        for (var vCut = aClass.LastIndexOf('-'); vCut > 0; vCut = aClass.LastIndexOf('-', vCut - 1))
        {
            if (aRazor.Contains(aClass[..(vCut + 1)] + "{", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells whether the markup writes the class name out in full, on a name boundary.</summary>
    /// <param name="aRazor">The component's markup.</param>
    /// <param name="aClass">The class name, without its dot.</param>
    /// <returns><c>true</c> when the markup contains it as a whole name.</returns>
    private static bool WritesClassLiterally(string aRazor, string aClass)
    {
        var vAt = aRazor.IndexOf(aClass, StringComparison.Ordinal);

        while (vAt >= 0)
        {
            var vBefore = vAt == 0 ? ' ' : aRazor[vAt - 1];
            var vAfterIndex = vAt + aClass.Length;
            var vAfter = vAfterIndex >= aRazor.Length ? ' ' : aRazor[vAfterIndex];

            if (!IsClassChar(vBefore) && !IsClassChar(vAfter))
            {
                return true;
            }

            vAt = aRazor.IndexOf(aClass, vAt + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Tells whether a character could continue a CSS identifier.</summary>
    /// <param name="aChar">The neighbouring character.</param>
    /// <returns><c>true</c> when it would make the match a fragment of a longer name.</returns>
    private static bool IsClassChar(char aChar) => char.IsLetterOrDigit(aChar) || aChar is '-' or '_';

    /// <summary>
    /// Yields every selector in a stylesheet, descending through conditional at-rules.
    /// </summary>
    /// <param name="aCss">The stylesheet source.</param>
    /// <returns>Each selector with the 1-based line its rule opened on.</returns>
    private static IEnumerable<(string Selector, int Line)> Selectors(string aCss)
    {
        var vText = StripComments(aCss);
        var vPrelude = new System.Text.StringBuilder();
        var vLine = 1;
        var vPreludeLine = 1;
        var vSkipDepth = 0;
        var vDepth = 0;

        for (var vIndex = 0; vIndex < vText.Length; vIndex++)
        {
            var vChar = vText[vIndex];

            if (vChar == '\n')
            {
                vLine++;
            }

            if (vSkipDepth > 0)
            {
                if (vChar == '{')
                {
                    vSkipDepth++;
                }
                else if (vChar == '}')
                {
                    vSkipDepth--;
                }

                continue;
            }

            switch (vChar)
            {
                case '{':
                {
                    var vText2 = vPrelude.ToString().Trim();
                    vPrelude.Clear();

                    if (vText2.StartsWith('@'))
                    {
                        if (OpaqueAtRules.Any(aRule =>
                                vText2.StartsWith(aRule, StringComparison.OrdinalIgnoreCase)))
                        {
                            vSkipDepth = 1;
                            continue;
                        }

                        if (TransparentAtRules.Any(aRule =>
                                vText2.StartsWith(aRule, StringComparison.OrdinalIgnoreCase)))
                        {
                            // Descend: the rules inside are scoped exactly like the ones outside.
                            vDepth++;
                            continue;
                        }

                        vSkipDepth = 1;
                        continue;
                    }

                    if (vText2.Length > 0)
                    {
                        foreach (var vSelector in vText2.Split(','))
                        {
                            if (vSelector.Trim().Length > 0)
                            {
                                yield return (vSelector, vPreludeLine);
                            }
                        }
                    }

                    // A rule's own block holds declarations, and Blazor supports no nesting, so step over it.
                    vSkipDepth = 1;
                    continue;
                }

                case '}':
                    vDepth = Math.Max(0, vDepth - 1);
                    vPrelude.Clear();
                    continue;

                default:
                    if (vPrelude.Length == 0)
                    {
                        // Leading whitespace is not part of the prelude; counting it would pin every
                        // rule's reported line to the first non-blank character in the file.
                        if (char.IsWhiteSpace(vChar))
                        {
                            continue;
                        }

                        vPreludeLine = vLine;
                    }

                    vPrelude.Append(vChar);
                    continue;
            }
        }
    }

    /// <summary>Reports an unbalanced comment, which turns prose into a selector.</summary>
    /// <param name="aCss">The stylesheet source.</param>
    /// <param name="aRelative">The repository-relative path, for the message.</param>
    /// <returns>One finding per fault.</returns>
    private static IEnumerable<string> CommentFaults(string aCss, string aRelative)
    {
        var vLine = 1;
        var vOpenLine = 0;
        var vInComment = false;

        for (var vIndex = 0; vIndex < aCss.Length; vIndex++)
        {
            if (aCss[vIndex] == '\n')
            {
                vLine++;
                continue;
            }

            if (vIndex + 1 >= aCss.Length)
            {
                continue;
            }

            if (!vInComment && aCss[vIndex] == '/' && aCss[vIndex + 1] == '*')
            {
                vInComment = true;
                vOpenLine = vLine;
                vIndex++;
            }
            else if (vInComment && aCss[vIndex] == '*' && aCss[vIndex + 1] == '/')
            {
                vInComment = false;
                vIndex++;
            }
            else if (!vInComment && aCss[vIndex] == '*' && aCss[vIndex + 1] == '/')
            {
                yield return
                    $"{aRelative}:{vLine} — `*/` closes a comment that was never opened. Everything "
                    + "between the previous `*/` and this one is being parsed as CSS.";

                vIndex++;
            }
        }

        if (vInComment)
        {
            yield return $"{aRelative}:{vOpenLine} — `/*` is never closed; the rest of the file is a comment.";
        }
    }

    /// <summary>
    /// Reads the class names a stylesheet declares are applied by something other than its own markup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One escape hatch, and it costs a sentence of justification in the file itself:
    /// <c>REQ-NFR-021 external: name-one, name-two</c> inside a comment. The case it exists for is real —
    /// <c>ReconnectModal.razor.css</c> styles the reconnection states that <c>blazor.web.js</c> toggles on
    /// the framework's own dialog, so those classes correctly appear in no markup anywhere.
    /// </para>
    /// <para>
    /// It is deliberately explicit rather than inferred. A check that quietly forgave any class it could
    /// not find would forgive the three defects this test was written for, and every future one.
    /// </para>
    /// </remarks>
    /// <param name="aCss">The stylesheet source.</param>
    /// <returns>The declared class names.</returns>
    private static HashSet<string> DeclaredExternalClasses(string aCss)
    {
        var vDeclared = new HashSet<string>(StringComparer.Ordinal);

        // The list may wrap across lines — five state classes do not fit one — so it runs to the end of
        // its comment and stops at the first entry that is not a bare class name.
        foreach (Match vMatch in Regex.Matches(aCss, @"REQ-NFR-021\s+external:\s*([^*]+)"))
        {
            foreach (var vName in vMatch.Groups[1].Value.Split(','))
            {
                var vTrimmed = vName.Trim().TrimStart('.');

                if (!Regex.IsMatch(vTrimmed, @"^-?[_a-zA-Z][\w-]*$"))
                {
                    break;
                }

                vDeclared.Add(vTrimmed);
            }
        }

        return vDeclared;
    }

    /// <summary>Blanks out every comment so the parser sees only CSS.</summary>
    /// <param name="aCss">The stylesheet source.</param>
    /// <returns>The source with comment bodies replaced by spaces and newlines preserved.</returns>
    private static string StripComments(string aCss)
    {
        var vOut = new System.Text.StringBuilder(aCss.Length);
        var vInComment = false;

        for (var vIndex = 0; vIndex < aCss.Length; vIndex++)
        {
            if (!vInComment && vIndex + 1 < aCss.Length && aCss[vIndex] == '/' && aCss[vIndex + 1] == '*')
            {
                vInComment = true;
                vOut.Append("  ");
                vIndex++;
                continue;
            }

            if (vInComment && vIndex + 1 < aCss.Length && aCss[vIndex] == '*' && aCss[vIndex + 1] == '/')
            {
                vInComment = false;
                vOut.Append("  ");
                vIndex++;
                continue;
            }

            vOut.Append(vInComment && aCss[vIndex] != '\n' ? ' ' : aCss[vIndex]);
        }

        return vOut.ToString();
    }
}
