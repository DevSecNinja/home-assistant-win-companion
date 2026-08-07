using System.Net;
using System.Text;
using HaCompanion.Core.App;
using HaCompanion.Core.HomeAssistant;
using Xunit;

namespace HaCompanion.Core.Tests;

public class OAuthTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
        public readonly List<string> Bodies = new();
        public readonly List<Uri?> Uris = new();

        public StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            Bodies.Add(body);
            Uris.Add(request.RequestUri);
            return _responder(request, body);
        }
    }

    private sealed class FixedClock : HaCompanion.Core.Abstractions.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Fact]
    public void BuildAuthorizeUrl_uses_loopback_client_and_redirect()
    {
        var url = HaOAuthClient.BuildAuthorizeUrl(
            "https://ha.local:8123", "http://localhost:8123/", "http://localhost:8123/", "xyz");

        Assert.StartsWith("https://ha.local:8123/auth/authorize?", url.ToString());
        Assert.Contains("response_type=code", url.Query);
        Assert.Contains("client_id=http%3A%2F%2Flocalhost%3A8123%2F", url.Query);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A8123%2F", url.Query);
        Assert.Contains("state=xyz", url.Query);
    }

    [Fact]
    public async Task ExchangeCodeAsync_posts_form_and_returns_tokens()
    {
        var handler = new StubHandler((_, _) =>
            Json("""{"access_token":"acc","expires_in":1800,"refresh_token":"ref","token_type":"Bearer"}"""));
        var oauth = new HaOAuthClient(new HttpClient(handler), "https://ha.local:8123");

        var token = await oauth.ExchangeCodeAsync("the-code", "http://localhost:9/");

        Assert.Equal("acc", token.AccessToken);
        Assert.Equal("ref", token.RefreshToken);
        Assert.Equal(1800, token.ExpiresIn);
        Assert.Equal("https://ha.local:8123/auth/token", handler.Uris[0]!.ToString());
        Assert.Contains("grant_type=authorization_code", handler.Bodies[0]);
        Assert.Contains("code=the-code", handler.Bodies[0]);
    }

    [Fact]
    public async Task TokenManager_refreshes_when_no_access_token_seeded()
    {
        var handler = new StubHandler((_, body) =>
        {
            Assert.Contains("grant_type=refresh_token", body);
            return Json("""{"access_token":"fresh","expires_in":1800,"token_type":"Bearer"}""");
        });
        var oauth = new HaOAuthClient(new HttpClient(handler), "https://ha.local:8123");
        var manager = new OAuthTokenManager(oauth, "http://localhost:9/", () => "ref-1", new FixedClock());

        var token = await manager.GetAccessTokenAsync();

        Assert.Equal("fresh", token);
    }

    [Fact]
    public async Task TokenManager_returns_seeded_token_without_network()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var oauth = new HaOAuthClient(new HttpClient(handler), "https://ha.local:8123");
        var clock = new FixedClock();
        var manager = new OAuthTokenManager(oauth, "http://localhost:9/", () => "ref-1", clock);

        manager.Seed("seeded", expiresInSeconds: 1800);
        var token = await manager.GetAccessTokenAsync();

        Assert.Equal("seeded", token);
    }

    [Fact]
    public async Task TokenManager_refreshes_when_seeded_token_expired()
    {
        var handler = new StubHandler((_, _) =>
            Json("""{"access_token":"renewed","expires_in":1800,"token_type":"Bearer"}"""));
        var oauth = new HaOAuthClient(new HttpClient(handler), "https://ha.local:8123");
        var clock = new FixedClock();
        var manager = new OAuthTokenManager(oauth, "http://localhost:9/", () => "ref-1", clock);

        manager.Seed("old", expiresInSeconds: 30);   // within the 60s skew window
        var token = await manager.GetAccessTokenAsync();

        Assert.Equal("renewed", token);
    }

    [Fact]
    public async Task TokenManager_returns_null_when_no_refresh_token()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var oauth = new HaOAuthClient(new HttpClient(handler), "https://ha.local:8123");
        var manager = new OAuthTokenManager(oauth, "http://localhost:9/", () => null, new FixedClock());

        Assert.Null(await manager.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Concurrent_token_callers_share_one_refresh()
    {
        var refreshes = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new AsyncStubHandler(async (_, _, ct) =>
        {
            Interlocked.Increment(ref refreshes);
            await release.Task.WaitAsync(ct);
            return Json("""{"access_token":"shared","expires_in":1800,"token_type":"Bearer"}""");
        });
        var oauth = new HaOAuthClient(new HttpClient(handler), "https://ha.local:8123");
        var manager = new OAuthTokenManager(
            oauth, "http://localhost:9/", () => "ref-1", new FixedClock());

        var callers = Enumerable.Range(0, 8)
            .Select(_ => manager.GetAccessTokenAsync().AsTask())
            .ToArray();
        await WaitUntilAsync(() => Volatile.Read(ref refreshes) == 1);
        release.TrySetResult();

        var tokens = await Task.WhenAll(callers);

        Assert.All(tokens, token => Assert.Equal("shared", token));
        Assert.Equal(1, refreshes);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class AsyncStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> _responder;

        public AsyncStubHandler(
            Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(ct);
            return await _responder(request, body, ct);
        }
    }
}
