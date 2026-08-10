using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WindowsCompanion.Testing;

/// <summary>Describes the protocol state of a fake Home Assistant WebSocket session.</summary>
public enum FakeHaWebSocketState
{
    /// <summary>The socket has connected.</summary>
    Connected,
    /// <summary>The server is awaiting authentication.</summary>
    AuthRequired,
    /// <summary>The client has authenticated.</summary>
    Authenticated,
    /// <summary>The client has subscribed to push notifications.</summary>
    PushSubscribed,
    /// <summary>The socket has disconnected.</summary>
    Disconnected
}

/// <summary>Represents one WebSocket client connected to the fake server.</summary>
public sealed class FakeHaWebSocketSession : IAsyncDisposable
{
    private readonly FakeHaScenario _scenario;
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private int _subscriptionId;

    internal FakeHaWebSocketSession(FakeHaScenario scenario, WebSocket socket)
    {
        _scenario = scenario;
        _socket = socket;
        Id = Guid.NewGuid();
    }

    /// <summary>Gets the unique session identifier.</summary>
    public Guid Id { get; }
    /// <summary>Gets the current protocol state.</summary>
    public FakeHaWebSocketState State { get; private set; } = FakeHaWebSocketState.Connected;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        _scenario.Interactions.Record(
            FakeHaInteractionKind.WebSocket, "CONNECT", "connected");

        try
        {
            if (await CloseAtAsync(FakeHaWebSocketStep.Connected, cancellationToken)
                    .ConfigureAwait(false))
                return;

            State = FakeHaWebSocketState.AuthRequired;
            await SendAsync(new
            {
                type = "auth_required",
                ha_version = "2026.8.0-test"
            }, cancellationToken).ConfigureAwait(false);
            _scenario.Interactions.Record(
                FakeHaInteractionKind.WebSocket, "SERVER", "auth_required");
            if (await CloseAtAsync(FakeHaWebSocketStep.AuthRequired, cancellationToken)
                    .ConfigureAwait(false))
                return;

            await _scenario.Faults
                .WaitIfHeldAsync(FakeHaFaultPoint.WebSocketAuthentication, cancellationToken)
                .ConfigureAwait(false);
            using var auth = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (auth is null) return;

            var authType = GetString(auth.RootElement, "type");
            var accessToken = GetString(auth.RootElement, "access_token");
            var authenticated = authType == "auth"
                                && string.Equals(
                                    accessToken,
                                    _scenario.AccessToken,
                                    StringComparison.Ordinal);
            _scenario.Interactions.Record(
                FakeHaInteractionKind.WebSocket,
                "CLIENT",
                authType ?? "unknown",
                new { access_token = accessToken },
                authenticated ? "Success" : "Rejected");

            if (!authenticated)
            {
                await SendAsync(new
                {
                    type = "auth_invalid",
                    message = "Invalid access token"
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            State = FakeHaWebSocketState.Authenticated;
            await SendAsync(new { type = "auth_ok", ha_version = "2026.8.0-test" }, cancellationToken)
                .ConfigureAwait(false);
            _scenario.Interactions.Record(
                FakeHaInteractionKind.WebSocket, "SERVER", "auth_ok");
            if (await CloseAtAsync(FakeHaWebSocketStep.Authenticated, cancellationToken)
                    .ConfigureAwait(false))
                return;

            while (!cancellationToken.IsCancellationRequested
                   && _socket.State == WebSocketState.Open)
            {
                using var message = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (message is null) break;
                await HandleMessageAsync(message.RootElement, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            State = FakeHaWebSocketState.Disconnected;
            _scenario.Interactions.Record(
                FakeHaInteractionKind.WebSocket, "DISCONNECT", "disconnected");
            _scenario.State.WebSocketSessions.TryRemove(Id, out _);
        }
    }

    internal async Task SendNotificationAsync(
        string title,
        string message,
        string? confirmationId,
        CancellationToken cancellationToken)
    {
        if (State != FakeHaWebSocketState.PushSubscribed) return;

        var eventPayload = confirmationId is null
            ? new Dictionary<string, object?>
            {
                ["title"] = title,
                ["message"] = message
            }
            : new Dictionary<string, object?>
            {
                ["title"] = title,
                ["message"] = message,
                ["hass_confirm_id"] = confirmationId
            };

        await SendAsync(new
        {
            id = _subscriptionId,
            type = "event",
            @event = eventPayload
        }, cancellationToken).ConfigureAwait(false);
        _scenario.Interactions.Record(
            FakeHaInteractionKind.Notification,
            "SERVER",
            "event",
            new { title, message, confirmation_id = confirmationId });
    }

    internal async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "Test requested close",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleMessageAsync(
        JsonElement message,
        CancellationToken cancellationToken)
    {
        var type = GetString(message, "type") ?? "unknown";
        _scenario.Interactions.Record(
            FakeHaInteractionKind.WebSocket, "CLIENT", type, message);

        switch (type)
        {
            case "mobile_app/push_notification_channel":
                await _scenario.Faults
                    .WaitIfHeldAsync(FakeHaFaultPoint.PushSubscription, cancellationToken)
                    .ConfigureAwait(false);
                _subscriptionId = message.TryGetProperty("id", out var id)
                    ? id.GetInt32()
                    : 0;
                var webhook = GetString(message, "webhook_id");
                var accepted = string.Equals(
                    webhook,
                    _scenario.WebhookId,
                    StringComparison.Ordinal);
                await SendAsync(new
                {
                    id = _subscriptionId,
                    type = "result",
                    success = accepted,
                    result = accepted ? new { } : null,
                    error = accepted ? null : new { code = "not_found", message = "Unknown webhook" }
                }, cancellationToken).ConfigureAwait(false);
                if (accepted)
                {
                    State = FakeHaWebSocketState.PushSubscribed;
                    _scenario.Interactions.Record(
                        FakeHaInteractionKind.WebSocket,
                        "SERVER",
                        "push_subscribed");
                    await CloseAtAsync(FakeHaWebSocketStep.PushSubscribed, cancellationToken)
                        .ConfigureAwait(false);
                }
                break;

            case "mobile_app/push_notification_confirm":
                var confirmId = GetString(message, "confirm_id");
                if (!string.IsNullOrEmpty(confirmId))
                {
                    _scenario.State.ConfirmedNotifications.TryAdd(confirmId, 0);
                    _scenario.Interactions.Record(
                        FakeHaInteractionKind.Notification,
                        "CLIENT",
                        "confirmation",
                        new { confirm_id = confirmId });
                }
                break;

            case "ping":
                await SendAsync(new
                {
                    id = message.TryGetProperty("id", out var pingId) ? pingId.GetInt32() : 0,
                    type = "pong"
                }, cancellationToken).ConfigureAwait(false);
                break;

            default:
                await SendAsync(new
                {
                    id = message.TryGetProperty("id", out var unknownId) ? unknownId.GetInt32() : 0,
                    type = "result",
                    success = false,
                    error = new { code = "unknown_command", message = "Unknown command" }
                }, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task<bool> CloseAtAsync(
        FakeHaWebSocketStep step,
        CancellationToken cancellationToken)
    {
        if (_scenario.Faults.ClosePushChannelAt != step) return false;
        await CloseAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(
                    bytes,
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<JsonDocument?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new WebSocketException("Only text WebSocket frames are supported.");

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return JsonDocument.Parse(stream.ToArray());
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
        }
        _sendGate.Dispose();
        _socket.Dispose();
    }
}
