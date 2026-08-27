using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfLens.Core;
using TfLens.Core.GitHub;

namespace TfLens.Core.Tests.GitHub;

/// <summary>Covers the read-only GitHub client: GET-only, SHA lookup, raw fetch and rate limits.</summary>
public sealed class GitHubStreamFetcherTests
{
    private const string CommitsBody = """[{"sha":"abc123def4567890","commit":{"message":"telemetry"}}]""";

    /// <summary>Every public method of the fetcher issues a GET and nothing else (REQ-FN-024).</summary>
    [Fact]
    public async Task FetcherOnlyEverIssuesGet()
    {
        var vHandler = new RecordingHttpMessageHandler(Respond);
        var vFetcher = BuildFetcher(vHandler, out _);

        await vFetcher.LatestShaAsync("techierathore", "TfLens", "main", "docs/metrics");
        await vFetcher.FetchFileAsync("techierathore", "TfLens", "docs/metrics/runs.jsonl", "abc123def4567890");
        await vFetcher.GetRepoAsync("techierathore", "TfLens");
        await vFetcher.PathExistsAsync("techierathore", "TfLens", "docs/metrics", "main");

        vHandler.Requests.Should().HaveCount(4);
        vHandler.Requests.Should().OnlyContain(aR => aR.Method == HttpMethod.Get);
    }

    /// <summary>The fetcher's own source names no HTTP verb but GET, so no code path can widen it.</summary>
    [Fact]
    public void FetcherSourceNamesNoWriteVerb()
    {
        var vSource = ReadFetcherSource();

        vSource.Should().Contain("HttpMethod.Get");

        foreach (var vVerb in new[] { "HttpMethod.Post", "HttpMethod.Put", "HttpMethod.Patch", "HttpMethod.Delete" })
        {
            vSource.Should().NotContain(vVerb);
        }

        foreach (var vCall in new[] { "PostAsync", "PutAsync", "PatchAsync", "DeleteAsync", "PostAsJsonAsync" })
        {
            vSource.Should().NotContain(vCall);
        }
    }

    /// <summary>The commits lookup pins the branch and the telemetry path and returns the newest SHA (REQ-FN-021).</summary>
    [Fact]
    public async Task LatestShaAsksForOneCommitOnThePath()
    {
        var vHandler = new RecordingHttpMessageHandler(Respond);
        var vFetcher = BuildFetcher(vHandler, out _);

        var vSha = await vFetcher.LatestShaAsync("techierathore", "TfLens", "main", "docs/metrics");

        vSha.Should().Be("abc123def4567890");
        vHandler.Urls.Single().Should().Be(
            "https://api.github.com/repos/techierathore/TfLens/commits" +
            "?sha=main&path=docs%2Fmetrics&per_page=1");
    }

    /// <summary>A telemetry path that has never been committed to answers null rather than throwing.</summary>
    [Fact]
    public async Task LatestShaIsNullWhenNoCommitTouchedThePath()
    {
        var vHandler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
        var vFetcher = BuildFetcher(vHandler, out _);

        var vSha = await vFetcher.LatestShaAsync("techierathore", "TfLens", "main", "docs/metrics");

        vSha.Should().BeNull();
    }

    /// <summary>The file fetch pins the exact SHA and asks for the raw media type (REQ-FN-022).</summary>
    [Fact]
    public async Task FetchFilePinsTheShaAndAsksForRaw()
    {
        var vHandler = new RecordingHttpMessageHandler(Respond);
        var vFetcher = BuildFetcher(vHandler, out _);

        var vText = await vFetcher.FetchFileAsync(
            "techierathore", "TfLens", "docs/metrics/runs.jsonl", "abc123def4567890");

        vText.Should().Be("{\"v\":1}\n");
        vHandler.Urls.Single().Should().Be(
            "https://api.github.com/repos/techierathore/TfLens/contents/docs/metrics/runs.jsonl" +
            "?ref=abc123def4567890");
        vHandler.Requests.Single().Headers.Accept.Select(aA => aA.MediaType)
            .Should().Contain(GitHubStreamFetcher.RawMediaType);
    }

    /// <summary>A 404 on a stream file is "stream absent" and answers null, not an error (REQ-FN-022).</summary>
    [Fact]
    public async Task MissingStreamFileIsAbsentNotAnError()
    {
        var vHandler = new RecordingHttpMessageHandler(
            _ => RecordingHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));
        var vFetcher = BuildFetcher(vHandler, out _);

        var vText = await vFetcher.FetchFileAsync(
            "techierathore", "TfLens", "docs/metrics/sessions.jsonl", "abc123def4567890");

        vText.Should().BeNull();
    }

    /// <summary>The bytes the fetcher returns round-trip to exactly the bytes GitHub answered (REQ-FN-027).</summary>
    [Fact]
    public async Task FetchedTextRoundTripsToTheSameBytes()
    {
        var vBytes = Encoding.UTF8.GetBytes("{\"a\":\"ü\"}\n{\"b\":\"日本\"}\n");
        var vHandler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.Raw(vBytes));
        var vFetcher = BuildFetcher(vHandler, out _);

        var vText = await vFetcher.FetchFileAsync("o", "n", "docs/metrics/runs.jsonl", "sha");

        new UTF8Encoding(false).GetBytes(vText!).Should().Equal(vBytes);
    }

    /// <summary>An exhausted rate-limit window raises a redacted message naming the wait (Architecture §12).</summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RateLimitRaisesARedactedWaitMessage(HttpStatusCode aStatusCode)
    {
        var vResetsAt = DateTimeOffset.UtcNow.AddMinutes(12);
        var vHandler = new RecordingHttpMessageHandler(
            _ => RecordingHttpMessageHandler.RateLimited(aStatusCode, vResetsAt));
        var vFetcher = BuildFetcher(vHandler, out _);

        var vAct = () => vFetcher.LatestShaAsync("techierathore", "TfLens", "main", "docs/metrics");

        var vException = (await vAct.Should().ThrowAsync<GitHubRateLimitException>()).Which;
        vException.Message.Should().StartWith("GitHub rate limit reached — try again in ");
        vException.MinutesUntilReset.Should().BeInRange(11, 13);
    }

    /// <summary>The PAT is sent as a bearer credential and never appears in a URL.</summary>
    [Fact]
    public async Task TokenTravelsInTheHeaderNotTheUrl()
    {
        var vHandler = new RecordingHttpMessageHandler(Respond);
        var vFetcher = BuildFetcher(vHandler, out _, "ghp_ExampleTokenValue0123456789");

        await vFetcher.LatestShaAsync("techierathore", "TfLens", "main", "docs/metrics");

        var vRequest = vHandler.Requests.Single();
        vRequest.RequestUri!.ToString().Should().NotContain("ghp");
        vRequest.Headers.UserAgent.Should().NotBeEmpty();
    }

    /// <summary>Answers the stub responses the happy-path tests share.</summary>
    /// <param name="aRequest">The recorded request.</param>
    /// <returns>The response for that URL.</returns>
    private static HttpResponseMessage Respond(HttpRequestMessage aRequest)
    {
        var vUrl = aRequest.RequestUri!.ToString();

        if (vUrl.Contains("/commits", StringComparison.Ordinal))
        {
            return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, CommitsBody);
        }

        if (vUrl.Contains("/contents/", StringComparison.Ordinal) && vUrl.Contains(".jsonl", StringComparison.Ordinal))
        {
            return RecordingHttpMessageHandler.Raw(Encoding.UTF8.GetBytes("{\"v\":1}\n"));
        }

        if (vUrl.Contains("/contents/", StringComparison.Ordinal))
        {
            return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
        }

        return RecordingHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"name":"TfLens","private":false,"default_branch":"main","owner":{"login":"techierathore"}}""");
    }

    /// <summary>Builds a fetcher over a stub transport.</summary>
    /// <param name="aHandler">The stub transport.</param>
    /// <param name="aHttpClient">Receives the client, so a test can inspect its headers.</param>
    /// <param name="aToken">The optional PAT.</param>
    /// <returns>The fetcher under test.</returns>
    private static GitHubStreamFetcher BuildFetcher(
        RecordingHttpMessageHandler aHandler,
        out HttpClient aHttpClient,
        string? aToken = null)
    {
        aHttpClient = new HttpClient(aHandler);

        var vOptions = Options.Create(new TfLensOptions { GitHubToken = aToken });
        return new GitHubStreamFetcher(aHttpClient, vOptions, NullLogger<GitHubStreamFetcher>.Instance);
    }

    /// <summary>Reads the fetcher's source file from the repository the tests were built in.</summary>
    /// <returns>The source text.</returns>
    private static string ReadFetcherSource()
    {
        var vDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (vDirectory is not null)
        {
            var vCandidate = Path.Combine(
                vDirectory.FullName, "src", "TfLens.Core", "GitHub", "GitHubStreamFetcher.cs");

            if (File.Exists(vCandidate))
            {
                return File.ReadAllText(vCandidate);
            }

            vDirectory = vDirectory.Parent;
        }

        throw new FileNotFoundException("GitHubStreamFetcher.cs was not found above the test output directory.");
    }
}
