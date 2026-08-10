using System.Net;
using System.Text;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public class GitHubReleaseClientTests
{
    [Fact]
    public async Task The_public_api_request_is_identified_and_parsed()
    {
        HttpRequestMessage? captured = null;
        var handler = new DelegateHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(Json(
                """
                {"tag_name":"v0.4.0","draft":false,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v0.4.0"}
                """));
        });
        var client = new GitHubReleaseClient(new HttpClient(handler), "0.3.0");

        var releases = await client.GetReleasesAsync(CancellationToken.None);

        var release = Assert.Single(releases);
        Assert.Equal("v0.4.0", release.TagName);
        Assert.Equal(GitHubReleaseClient.ReleasesEndpoint, captured!.RequestUri);
        Assert.Contains("application/vnd.github+json", captured.Headers.Accept.ToString());
        Assert.Contains("WindowsCompanion/0.3.0", captured.Headers.UserAgent.ToString());
        Assert.Equal("2026-03-10", Assert.Single(captured.Headers.GetValues("X-GitHub-Api-Version")));
    }

    [Fact]
    public async Task A_malformed_response_does_not_become_release_metadata()
    {
        var client = Client((_, _) => Task.FromResult(Json("""{"message":"not an array"}""")));

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => client.GetReleasesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_oversized_response_is_rejected_before_parsing()
    {
        var response = Json("[]");
        response.Content.Headers.ContentLength = 1_048_577;
        var client = Client((_, _) => Task.FromResult(response));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetReleasesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_request_has_a_bounded_timeout()
    {
        var client = Client(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Json("[]");
            },
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.GetReleasesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_misreported_as_a_timeout()
    {
        var client = Client(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Json("[]");
            },
            TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetReleasesAsync(cancellation.Token));
    }

    [Fact]
    public async Task Redirects_are_not_accepted_as_release_responses()
    {
        var client = Client((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.invalid/releases") }
            }));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetReleasesAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("0.3.0+abc123", true, true, "0.3.0")]
    [InlineData("0.3.0", false, false, null)]
    [InlineData("not-a-version", true, true, null)]
    public void Installed_version_resolution_is_truthful_for_release_and_source_builds(
        string informationalVersion,
        bool officialRelease,
        bool expectedOfficial,
        string? expectedVersion)
    {
        var installed = InstalledBuildResolver.Resolve(informationalVersion, officialRelease);

        Assert.Equal(expectedOfficial, installed.IsOfficialRelease);
        Assert.Equal(expectedVersion, installed.Version?.ToString());
    }

    private static GitHubReleaseClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        TimeSpan? timeout = null) =>
        new(new HttpClient(new DelegateHandler(responder)), "0.3.0", timeout);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
