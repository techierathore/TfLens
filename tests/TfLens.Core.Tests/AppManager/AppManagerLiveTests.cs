using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core.AppManager;
using TfLens.Core.Contracts;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// Exercises the real AppManager instance with the documented demo account.
/// </summary>
/// <remarks>
/// <para>
/// These are the only tests that prove the client agrees with the server about RSA padding, body shape
/// and error codes — a stub can only ever confirm the client agrees with itself. They are tagged
/// <c>Category=Live</c> so CI can filter them out (<c>--filter Category!=Live</c>) when the network or
/// the AppManager instance is not part of the run.
/// </para>
/// <para>
/// The account is UsageGuide test user #1 (<c>TfLensDemo</c>, AppManager user id 2). Nothing here
/// creates an account, and nothing here changes the demo user's password.
/// </para>
/// </remarks>
[Trait("Category", "Live")]
public sealed class AppManagerLiveTests
{
    private const string DemoEmail = "tflensdemo@techierathore.com";
    private const string DemoPassword = "TfLensDemo!23";
    private const int DemoUserId = 2;

    /// <summary>The live server publishes a usable RSA public key.</summary>
    [Fact]
    public async Task PublicKeyFetches()
    {
        var vAuth = await BuildClient().LoginAsync(DemoEmail, DemoPassword);

        vAuth.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>The documented demo account signs in and is AppManager user 2, a TfLens Manager.</summary>
    [Fact]
    public async Task DemoUserSignsIn()
    {
        var vAuth = await BuildClient().LoginAsync(DemoEmail, DemoPassword);

        vAuth.UserId.Should().Be(DemoUserId);
        vAuth.Email.Should().Be(DemoEmail);
        vAuth.ApplicationRole.Should().Be("Manager");
        vAuth.DisplayName.Should().Be("TfLens Demo");
        vAuth.TokenExpiresAt.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>A wrong password comes back as INVALID_CREDENTIALS and nothing more specific.</summary>
    [Fact]
    public async Task WrongPasswordIsRefused()
    {
        var vAct = async () => await BuildClient().LoginAsync(DemoEmail, "DefinitelyWrong!23");

        (await vAct.Should().ThrowAsync<AppManagerException>())
            .Which.Code.Should().Be(AppManagerException.Codes.InvalidCredentials);
    }

    /// <summary>Refresh rotates the token, validate accepts it, and logout revokes it for good.</summary>
    [Fact]
    public async Task RefreshValidateAndLogoutRoundTrip()
    {
        var vClient = BuildClient();
        var vAuth = await vClient.LoginAsync(DemoEmail, DemoPassword);

        var vRefreshed = await vClient.RefreshAsync(vAuth.RefreshToken);
        vRefreshed.UserId.Should().Be(DemoUserId);
        vRefreshed.RefreshToken.Should().NotBe(vAuth.RefreshToken);

        (await vClient.ValidateAsync(vRefreshed.AccessToken)).Should().BeTrue();

        await vClient.LogoutAsync(vRefreshed.RefreshToken, vRefreshed.AccessToken);

        var vReuse = async () => await vClient.RefreshAsync(vRefreshed.RefreshToken);
        (await vReuse.Should().ThrowAsync<AppManagerException>())
            .Which.Code.Should().Be(AppManagerException.Codes.ExpiredRefreshToken);
    }

    /// <summary>The profile endpoint returns live AppManager data for the signed-in user.</summary>
    [Fact]
    public async Task ProfileReadsLiveData()
    {
        var vClient = BuildClient();
        var vAuth = await vClient.LoginAsync(DemoEmail, DemoPassword);

        var vProfile = await vClient.GetProfileAsync(vAuth.AccessToken);

        vProfile.UserId.Should().Be(DemoUserId);
        vProfile.Email.Should().Be(DemoEmail);
        vProfile.FirstName.Should().Be("TfLens");
        vProfile.MemberSince.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>A wrong current password is refused as a field error and changes nothing.</summary>
    [Fact]
    public async Task ChangePasswordRejectsWrongCurrent()
    {
        var vClient = BuildClient();
        var vAuth = await vClient.LoginAsync(DemoEmail, DemoPassword);

        var vAct = async () => await vClient.ChangePasswordAsync(
            vAuth.AccessToken,
            "NotTheCurrentOne!23",
            "AlsoNotUsed!456");

        (await vAct.Should().ThrowAsync<AppManagerException>())
            .Which.Code.Should().Be(AppManagerException.Codes.InvalidCurrentPassword);

        // The demo password must still work afterwards.
        (await vClient.LoginAsync(DemoEmail, DemoPassword)).UserId.Should().Be(DemoUserId);
    }

    /// <summary>
    /// Builds a client pointed at the live AppManager instance with no API-key pair.
    /// </summary>
    /// <returns>The client under test.</returns>
    private static AppManagerClient BuildClient()
    {
        var vOptions = new TfLensOptions();
        var vHttpClient = new HttpClient { BaseAddress = new Uri(vOptions.AppManagerBaseUrl) };
        return new AppManagerClient(vHttpClient, Options.Create(vOptions), NullLogger<AppManagerClient>.Instance);
    }
}
