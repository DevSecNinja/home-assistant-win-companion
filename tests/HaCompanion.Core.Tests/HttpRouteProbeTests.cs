using System.Net;
using System.Text;
using HaCompanion.Core.App;
using HaCompanion.Core.Models;
using Xunit;

namespace HaCompanion.Core.Tests;

public class HttpRouteProbeTests
{
    private const string Manifest = """{"name":"Home Assistant","short_name":"Assistant"}""";

    private class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public readonly List<HttpRequestMessage> Requests = new();
        public readonly List<string> Bodies = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            var response = _responder(request);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Fails the transport itself, the way a non-HTTP listener does.</summary>
    private sealed class ThrowingHandler(Exception failure) : RecordingHandler(_ => throw failure);

    private static HttpRouteProbe Probe(RecordingHandler handler, string? refreshToken = "refresh-me") =>
        new(new HttpClient(handler), () => refreshToken, "https://example.invalid/app");

    /// <summary>Answers the whole happy path: manifest, token, API, webhook.</summary>
    private static HttpResponseMessage HealthyInstance(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("manifest.json", StringComparison.Ordinal)) return Json(Manifest);
        if (path.EndsWith("/auth/token", StringComparison.Ordinal))
            return Json("""{"access_token":"at","expires_in":1800,"token_type":"Bearer"}""");
        if (path.EndsWith("/api/webhook/wh-1", StringComparison.Ordinal))
            return Json("""{"hass_device_id":"dev-9","version":"2025.1.0"}""");
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_healthy_instance_reports_its_device_id()
    {
        var handler = new RecordingHandler(HealthyInstance);

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.Ok, result.Status);
        Assert.Equal("dev-9", result.InstanceDeviceId);
        Assert.Equal("https://ha.example.com/", result.ResolvedUrl);
        Assert.False(result.InsecureTransport);
    }

    [Fact]
    public async Task An_external_http_address_is_blocked_without_any_request_at_all()
    {
        var handler = new RecordingHandler(HealthyInstance);

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "http://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.Blocked, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_host_that_is_not_home_assistant_never_sees_the_token_or_the_webhook()
    {
        var handler = new RecordingHandler(_ => Json("""{"name":"Guest WiFi Portal"}"""));

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.NotHomeAssistant, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("manifest.json", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Bodies, b => b.Contains("refresh-me", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("wh-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_captive_portal_redirect_to_another_host_is_blocked_before_credentials()
    {
        var handler = new RecordingHandler(request =>
        {
            var response = Json(Manifest);
            response.RequestMessage = new HttpRequestMessage(
                request.Method, "https://login.hotel-wifi.example/manifest.json");
            return response;
        });

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.Blocked, result.Status);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(handler.Bodies, b => b.Contains("refresh-me", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_https_address_that_redirects_down_to_http_is_blocked()
    {
        var handler = new RecordingHandler(request =>
        {
            var response = Json(Manifest);
            response.RequestMessage = new HttpRequestMessage(
                request.Method, "http://ha.example.com/manifest.json");
            return response;
        });

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.Blocked, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_home_assistant_that_does_not_know_this_registration_is_a_different_instance()
    {
        var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/api/webhook/wh-1", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) }
                : HealthyInstance(request));

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.DifferentInstance, result.Status);
    }

    [Fact]
    public async Task A_deleted_registration_answering_410_is_a_different_instance()
    {
        var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/api/webhook/wh-1", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Gone)
                : HealthyInstance(request));

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.DifferentInstance, result.Status);
    }

    [Fact]
    public async Task A_rejected_refresh_token_means_a_different_instance_not_a_network_problem()
    {
        var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/auth/token", StringComparison.Ordinal)
                ? Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest)
                : HealthyInstance(request));

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.CredentialsRejected, result.Status);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("webhook", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreachable_address_is_reported_as_transient()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("no route to host"));

        var result = await Probe(handler).ProbeAsync(RouteKind.Internal, "http://ha.local:8123", "wh-1");

        Assert.Equal(RouteProbeStatus.Unreachable, result.Status);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public async Task An_internal_http_address_is_accepted_and_flagged_insecure()
    {
        var handler = new RecordingHandler(HealthyInstance);

        var result = await Probe(handler).ProbeAsync(RouteKind.Internal, "http://ha.local:8123", "wh-1");

        Assert.Equal(RouteProbeStatus.Ok, result.Status);
        Assert.True(result.InsecureTransport);
    }

    [Fact]
    public async Task Without_a_webhook_the_api_check_is_the_whole_proof()
    {
        var handler = new RecordingHandler(HealthyInstance);

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", null);

        Assert.Equal(RouteProbeStatus.Ok, result.Status);
        Assert.Null(result.InstanceDeviceId);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("webhook", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_saved_credentials_the_address_cannot_be_proven()
    {
        var handler = new RecordingHandler(HealthyInstance);

        var result = await Probe(handler, refreshToken: null)
            .ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.CredentialsRejected, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_frontend_that_answers_an_error_is_not_home_assistant()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.NotHomeAssistant, result.Status);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Carries forward the protocol hardening from the old single-URL change
    /// path: an endpoint that accepts a socket but does not speak HTTP must fail
    /// as a plain, non-usable address instead of throwing a transport error.
    /// </summary>
    [Fact]
    public async Task An_endpoint_that_does_not_speak_http_is_not_usable()
    {
        var handler = new ThrowingHandler(new IOException("The response ended prematurely."));

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.NotHomeAssistant, result.Status);
        Assert.False(result.Ok);
        Assert.False(result.IsTransient);
        Assert.Contains("HTTP", result.Message);
        Assert.DoesNotContain("prematurely", result.Message);
    }

    [Fact]
    public async Task An_unreachable_endpoint_stays_transient()
    {
        var handler = new ThrowingHandler(new HttpRequestException("No such host is known."));

        var result = await Probe(handler).ProbeAsync(RouteKind.External, "https://ha.example.com", "wh-1");

        Assert.Equal(RouteProbeStatus.Unreachable, result.Status);
        Assert.True(result.IsTransient);
        Assert.DoesNotContain("No such host", result.Message);
    }

    [Theory]
    [InlineData("""{"name":"Home Assistant"}""", true)]
    [InlineData("""{"short_name":"Assistant"}""", true)]
    [InlineData("""{"name":"home assistant"}""", true)]
    [InlineData("""{"name":"Router Login"}""", false)]
    [InlineData("<html>hello</html>", false)]
    [InlineData("[]", false)]
    [InlineData("", false)]
    public void Manifest_recognition_only_accepts_a_home_assistant_frontend(string body, bool expected) =>
        Assert.Equal(expected, HttpRouteProbe.LooksLikeHomeAssistant(body));
}
