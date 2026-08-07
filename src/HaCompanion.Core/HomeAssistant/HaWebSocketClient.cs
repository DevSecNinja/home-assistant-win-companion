using System.Text.Json;
using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HaCompanion.Core.HomeAssistant;

/// <summary>
/// Speaks the Home Assistant WebSocket API: authenticates with the access token,
/// subscribes to persistent_notification events, and raises a
/// <see cref="NotificationReceived"/> event for each one. A single call to
/// <see cref="RunAsync"/> lives for the duration of one connection.
/// </summary>
public sealed class HaWebSocketClient
{
    private readonly Func<IHaSocket> _socketFactory;
    private readonly Uri _wsUri;
    private readonly IAccessTokenProvider _tokens;
    private readonly ILogger<HaWebSocketClient> _log;
    private int _messageId;

    public event Action<NotificationMessage>? NotificationReceived;

    public HaWebSocketClient(
        Func<IHaSocket> socketFactory,
        string baseUrl,
        IAccessTokenProvider tokens,
        ILogger<HaWebSocketClient>? log = null)
    {
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _wsUri = BuildWebSocketUri(baseUrl);
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _log = log ?? NullLogger<HaWebSocketClient>.Instance;
    }

    internal static Uri BuildWebSocketUri(string baseUrl)
    {
        var b = new UriBuilder(baseUrl)
        {
            Scheme = baseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws"
        };
        var path = b.Path.TrimEnd('/');
        b.Path = path + "/api/websocket";
        return b.Uri;
    }

    /// <summary>
    /// Connects, authenticates, subscribes, and pumps messages until the socket
    /// closes or cancellation is requested. Throws
    /// <see cref="HomeAssistantAuthException"/> if the token is rejected.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var socket = _socketFactory();
        await socket.ConnectAsync(_wsUri, ct).ConfigureAwait(false);
        _messageId = 0;

        while (!ct.IsCancellationRequested)
        {
            var raw = await socket.ReceiveAsync(ct).ConfigureAwait(false);
            if (raw is null)
            {
                _log.LogDebug("WebSocket closed by server.");
                return;
            }

            using var doc = JsonDocument.Parse(raw);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;

            switch (type)
            {
                case "auth_required":
                    var token = await _tokens.GetAccessTokenAsync(ct).ConfigureAwait(false);
                    await SendAsync(socket, new { type = "auth", access_token = token }, ct).ConfigureAwait(false);
                    break;

                case "auth_ok":
                    _log.LogInformation("WebSocket authenticated.");
                    await SendAsync(socket, new
                    {
                        id = NextId(),
                        type = "subscribe_events",
                        event_type = "persistent_notification"
                    }, ct).ConfigureAwait(false);
                    break;

                case "auth_invalid":
                    throw new HomeAssistantAuthException("Home Assistant rejected the WebSocket access token.");

                case "event":
                    HandleEvent(doc.RootElement);
                    break;

                case "pong":
                case "result":
                    break;
            }
        }
    }

    private void HandleEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var ev)) return;
        if (!ev.TryGetProperty("data", out var data)) return;

        var title = data.TryGetProperty("title", out var tt) ? tt.GetString() : null;
        var message = data.TryGetProperty("message", out var mm) ? mm.GetString() : null;
        if (string.IsNullOrEmpty(message) && string.IsNullOrEmpty(title)) return;

        NotificationReceived?.Invoke(new NotificationMessage(
            title ?? "Home Assistant",
            message ?? string.Empty));
    }

    private int NextId() => ++_messageId;

    private static Task SendAsync(IHaSocket socket, object payload, CancellationToken ct)
        => socket.SendAsync(JsonSerializer.Serialize(payload), ct);
}
