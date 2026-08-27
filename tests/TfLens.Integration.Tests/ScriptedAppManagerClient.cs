using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Integration.Tests;

/// <summary>
/// An <see cref="IAppManagerClient"/> whose password-reset answers the test chooses.
/// </summary>
/// <remarks>
/// <para>
/// Substituting the client is what makes the two clauses of REQ-FN-003 provable without the live
/// service. <c>APP_ID_MISMATCH</c> in particular cannot be produced against the real AppManager at
/// all — it needs a reset token minted for a different tenant, which no client can obtain — so the
/// only honest way to prove that TfLens collapses it onto the same outcome is to have AppManager say
/// it.
/// </para>
/// <para>
/// Everything else on the interface throws. These tests drive the two anonymous reset endpoints and
/// nothing more, and a fake that silently answered a call the test did not intend would make the
/// result meaningless.
/// </para>
/// </remarks>
public sealed class ScriptedAppManagerClient : IAppManagerClient
{
    /// <summary>The code to refuse the next reset with, or <c>null</c> to accept it.</summary>
    public string? ResetFailureCode { get; set; }

    /// <summary>Every address <c>/auth/forgot-password</c> submitted, in order.</summary>
    public List<string> ForgottenAddresses { get; } = [];

    /// <summary>Every token <c>/auth/reset-password</c> submitted, in order.</summary>
    public List<string> SubmittedTokens { get; } = [];

    /// <inheritdoc />
    public Task ForgotPasswordAsync(string aEmail, CancellationToken aCancellationToken = default)
    {
        ForgottenAddresses.Add(aEmail);

        // The real client swallows every failure for exactly this reason (BRD-92); this fake mirrors
        // that contract so the endpoint is tested against the client it actually has.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ResetPasswordAsync(
        string aToken,
        string aNewPassword,
        CancellationToken aCancellationToken = default)
    {
        SubmittedTokens.Add(aToken);

        return ResetFailureCode is null
            ? Task.CompletedTask
            : throw new AppManagerException(ResetFailureCode, "rejected", 400);
    }

    /// <inheritdoc />
    public Task<AuthResponseData> LoginAsync(
        string aEmail,
        string aPassword,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(Unexpected(nameof(LoginAsync)));

    /// <inheritdoc />
    public Task<AuthResponseData> RegisterAsync(
        RegisterRequest aRequest,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(Unexpected(nameof(RegisterAsync)));

    /// <inheritdoc />
    public Task<AuthResponseData> RefreshAsync(
        string aRefreshToken,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(Unexpected(nameof(RefreshAsync)));

    /// <inheritdoc />
    public Task<bool> ValidateAsync(string aAccessToken, CancellationToken aCancellationToken = default) =>
        Task.FromResult(false);

    /// <inheritdoc />
    public Task LogoutAsync(
        string aRefreshToken,
        string? aAccessToken = null,
        CancellationToken aCancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<UserProfile> GetProfileAsync(
        string aAccessToken,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(Unexpected(nameof(GetProfileAsync)));

    /// <inheritdoc />
    public Task ChangePasswordAsync(
        string aAccessToken,
        string aCurrentPassword,
        string aNewPassword,
        CancellationToken aCancellationToken = default) =>
        throw new NotSupportedException(Unexpected(nameof(ChangePasswordAsync)));

    /// <summary>Builds the message for a call these tests never intend to make.</summary>
    /// <param name="aMember">The member that was called.</param>
    /// <returns>The failure message.</returns>
    private static string Unexpected(string aMember) =>
        $"{aMember} was called; the password-reset tests drive only the two anonymous reset endpoints.";
}
