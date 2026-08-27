namespace TfLens.Core.AppManager;

/// <summary>
/// AppManager's password complexity rules, evaluated locally before a password leaves the process.
/// </summary>
/// <remarks>
/// BRD-91 / REQ-FN-002: a password that cannot possibly pass is rejected before the API call, so a
/// predictable rule violation never costs a round trip and never reaches the server's log. The rules
/// mirror the guide (§3.1 <c>POST /AuthSvc/register</c>): eight characters, one uppercase letter, one
/// digit and one special character.
/// </remarks>
public static class PasswordRules
{
    /// <summary>The shortest password AppManager accepts.</summary>
    public const int MinimumLength = 8;

    /// <summary>
    /// Tests a candidate password against every rule.
    /// </summary>
    /// <param name="aPassword">The candidate password; never logged.</param>
    /// <returns><c>true</c> when the password satisfies every rule.</returns>
    public static bool IsValid(string? aPassword) => Describe(aPassword) is null;

    /// <summary>
    /// Explains why a candidate password is unacceptable.
    /// </summary>
    /// <param name="aPassword">The candidate password; never logged and never echoed into the message.</param>
    /// <returns>A user-facing reason, or <c>null</c> when the password is acceptable.</returns>
    /// <remarks>
    /// The message names the rule that failed, never the value that failed it, so it is safe to render
    /// as a field error next to the password box.
    /// </remarks>
    public static string? Describe(string? aPassword)
    {
        if (string.IsNullOrEmpty(aPassword) || aPassword.Length < MinimumLength)
        {
            return $"Password must be at least {MinimumLength} characters long.";
        }

        if (!aPassword.Any(char.IsUpper))
        {
            return "Password must contain at least one uppercase letter.";
        }

        if (!aPassword.Any(char.IsDigit))
        {
            return "Password must contain at least one number.";
        }

        return aPassword.Any(aCharacter => !char.IsLetterOrDigit(aCharacter))
            ? null
            : "Password must contain at least one special character.";
    }
}
