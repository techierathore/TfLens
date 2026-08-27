using System.Net;
using FluentAssertions;
using TfLens.Core.GitHub;
using TfLens.Services.Sync;

namespace TfLens.Core.Tests.Sync;

/// <summary>Covers the redaction that stands between a raw failure and a displayed <c>LastError</c>.</summary>
public sealed class SyncErrorRedactorTests
{
    /// <summary>Each of the statuses BRD-15 names becomes a status-code-plus-short-reason.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "HTTP 401")]
    [InlineData(HttpStatusCode.Forbidden, "HTTP 403")]
    [InlineData(HttpStatusCode.NotFound, "HTTP 404")]
    public void HttpStatusBecomesAShortReason(HttpStatusCode aStatusCode, string aExpectedPrefix)
    {
        var vMessage = SyncErrorRedactor.Redact(new HttpRequestException("raw", null, aStatusCode));

        vMessage.Should().StartWith(aExpectedPrefix);
    }

    /// <summary>A network failure with no status still reads as a network failure.</summary>
    [Fact]
    public void NetworkFailureReadsAsNetworkFailure()
    {
        SyncErrorRedactor.Redact(new HttpRequestException("connection refused"))
            .Should().Be("Network error reaching GitHub.");
    }

    /// <summary>The rate-limit message passes through unchanged — it is already the user-facing sentence.</summary>
    [Fact]
    public void RateLimitMessagePassesThrough()
    {
        SyncErrorRedactor.Redact(new GitHubRateLimitException(403, DateTimeOffset.UtcNow, 12))
            .Should().Be("GitHub rate limit reached — try again in 12 minutes");
    }

    /// <summary>A PAT in any documented prefix is replaced before the message is stored.</summary>
    [Theory]
    [InlineData("ghp_SuperSecretTokenValue1234567890")]
    [InlineData("gho_SuperSecretTokenValue1234567890")]
    [InlineData("github_pat_11ABCDEFG0abcdefghijKLmnopQRstuv")]
    public void TokenIsReplaced(string aToken)
    {
        var vMessage = SyncErrorRedactor.Scrub($"auth failed for {aToken}");

        vMessage.Should().NotContain(aToken);
        vMessage.Should().Contain(SyncErrorRedactor.Placeholder);
    }

    /// <summary>A URL is replaced wholesale, because it can carry a credential in its userinfo or query.</summary>
    [Fact]
    public void UrlIsReplaced()
    {
        var vMessage = SyncErrorRedactor.Scrub("GET https://user:secret@api.github.com/repos/a/b?token=abc failed");

        vMessage.Should().NotContain("api.github.com");
        vMessage.Should().NotContain("secret");
    }

    /// <summary>A bearer credential is replaced even when the token itself is unrecognisably shaped.</summary>
    [Fact]
    public void BearerCredentialIsReplaced()
    {
        SyncErrorRedactor.Scrub("sent Authorization: Bearer zzzzzzzzz").Should().NotContain("zzzzzzzzz");
    }

    /// <summary>A long message is truncated so one failure cannot flood the Coverage page.</summary>
    [Fact]
    public void LongMessageIsTruncated()
    {
        SyncErrorRedactor.Scrub(new string('x', 5000)).Length.Should().BeLessThanOrEqualTo(SyncErrorRedactor.MaxLength + 1);
    }
}
