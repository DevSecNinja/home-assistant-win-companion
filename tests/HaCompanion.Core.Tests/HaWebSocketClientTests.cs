using System.Text.Json;
using System.Threading.Channels;
using HaCompanion.Core.HomeAssistant;
using HaCompanion.Core.Models;
using Xunit;

namespace HaCompanion.Core.Tests;

public class HaWebSocketClientTests
{
    /// <summary>A scripted in-memory socket that feeds queued server frames.</summary>
    private sealed class FakeSocket : IHaSocket
    {
        private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
        public readonly List<string> Sent = new();

        public void Enqueue(string message) => _incoming.Writer.TryWrite(message);
        public void Close() => _incoming.Writer.TryWrite(null);

        public Task ConnectAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;

        public Task SendAsync(string json, CancellationToken ct)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken ct)
            => await _incoming.Reader.ReadAsync(ct);

        public void Dispose() { }
    }

    [Fact]
    public void BuildWebSocketUri_maps_https_to_wss_and_appends_path()
    {
        Assert.Equal("wss://ha.local:8123/api/websocket",
            HaWebSocketClient.BuildWebSocketUri("https://ha.local:8123").ToString());
        Assert.Equal("ws://ha.local:8123/api/websocket",
            HaWebSocketClient.BuildWebSocketUri("http://ha.local:8123/").ToString());
    }

    [Fact]
    public async Task Authenticates_opens_push_channel_and_raises_notification()
    {
        var socket = new FakeSocket();
        var client = new HaWebSocketClient(
            () => socket, "https://ha.local:8123", new StaticTokenProvider("tok-abc"), "hook-123");

        NotificationMessage? received = null;
        client.NotificationReceived += n => received = n;

        var run = client.RunAsync(CancellationToken.None);

        socket.Enqueue("""{"type":"auth_required","ha_version":"2024.1"}""");
        socket.Enqueue("""{"type":"auth_ok"}""");
        socket.Enqueue("""{"id":1,"type":"result","success":true}""");
        socket.Enqueue("""{"id":1,"type":"event","event":{"title":"Door","message":"Front door open"}}""");
        socket.Close();

        await run;

        // First frame is the auth response carrying the token.
        using (var authDoc = JsonDocument.Parse(socket.Sent[0]))
        {
            Assert.Equal("auth", authDoc.RootElement.GetProperty("type").GetString());
            Assert.Equal("tok-abc", authDoc.RootElement.GetProperty("access_token").GetString());
        }
        // Second frame opens the mobile_app local push channel.
        using (var subDoc = JsonDocument.Parse(socket.Sent[1]))
        {
            Assert.Equal("mobile_app/push_notification_channel", subDoc.RootElement.GetProperty("type").GetString());
            Assert.Equal("hook-123", subDoc.RootElement.GetProperty("webhook_id").GetString());
            Assert.True(subDoc.RootElement.GetProperty("support_confirm").GetBoolean());
        }

        Assert.NotNull(received);
        Assert.Equal("Door", received!.Title);
        Assert.Equal("Front door open", received.Message);
    }

    [Fact]
    public async Task Confirms_notifications_that_request_confirmation()
    {
        var socket = new FakeSocket();
        var client = new HaWebSocketClient(
            () => socket, "https://ha.local:8123", new StaticTokenProvider("tok"), "hook-xyz");

        var run = client.RunAsync(CancellationToken.None);
        socket.Enqueue("""{"type":"auth_required"}""");
        socket.Enqueue("""{"type":"auth_ok"}""");
        socket.Enqueue("""{"id":1,"type":"event","event":{"message":"Hi","hass_confirm_id":"abc123"}}""");
        socket.Close();

        await run;

        var confirm = socket.Sent
            .Select(s => JsonDocument.Parse(s))
            .FirstOrDefault(d => d.RootElement.GetProperty("type").GetString()
                                 == "mobile_app/push_notification_confirm");

        Assert.NotNull(confirm);
        Assert.Equal("abc123", confirm!.RootElement.GetProperty("confirm_id").GetString());
        Assert.Equal("hook-xyz", confirm.RootElement.GetProperty("webhook_id").GetString());
    }

    [Fact]
    public async Task Auth_invalid_throws_auth_exception()
    {
        var socket = new FakeSocket();
        var client = new HaWebSocketClient(
            () => socket, "https://ha.local:8123", new StaticTokenProvider("bad"), "hook");

        var run = client.RunAsync(CancellationToken.None);
        socket.Enqueue("""{"type":"auth_required"}""");
        socket.Enqueue("""{"type":"auth_invalid","message":"Invalid password"}""");

        await Assert.ThrowsAsync<HomeAssistantAuthException>(() => run);
    }
}
