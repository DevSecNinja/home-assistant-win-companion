using System.Net.WebSockets;
using System.Text;

namespace WindowsCompanion.Core.HomeAssistant;

/// <summary>
/// Minimal duplex text-frame socket abstraction so the Home Assistant WebSocket
/// protocol logic can be unit-tested without a real network connection.
/// </summary>
public interface IHaSocket : IDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken ct);
    Task SendAsync(string json, CancellationToken ct);
    /// <summary>Receives the next text message, or null if the socket closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken ct);
}

/// <summary>Real <see cref="IHaSocket"/> backed by <see cref="ClientWebSocket"/>.</summary>
public sealed class ClientWebSocketAdapter : IHaSocket
{
    private readonly ClientWebSocket _socket = new();

    public ClientWebSocketAdapter()
    {
        // Without a finite keep-alive timeout a silently dropped link (VPN drop,
        // Wi-Fi change, sleeping NAT) leaves ReceiveAsync blocked forever: the
        // notification channel is dead but the loop never returns, so reconnect
        // never runs. .NET pings by default but waits indefinitely for the pong.
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        _socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
    }

    public Task ConnectAsync(Uri uri, CancellationToken ct) => _socket.ConnectAsync(uri, ct);

    public Task SendAsync(string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    public void Dispose() => _socket.Dispose();
}
