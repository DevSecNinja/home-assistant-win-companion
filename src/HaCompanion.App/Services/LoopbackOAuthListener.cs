using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HaCompanion_App.Services;

/// <summary>
/// A minimal loopback HTTP listener (raw TCP, so it needs no URL ACL / admin
/// rights) that captures the OAuth authorization <c>code</c> redirected back by
/// the browser.
/// </summary>
public sealed class LoopbackOAuthListener
{
    public async Task<string> WaitForCodeAsync(int port, string expectedState, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                await using var stream = client.GetStream();

                var requestLine = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
                var target = ExtractTarget(requestLine);
                var query = ParseQuery(target);

                if (query.TryGetValue("error", out var error))
                {
                    await WriteAsync(stream, "Authorization failed. You can close this window.", ct).ConfigureAwait(false);
                    throw new InvalidOperationException($"Authorization failed: {error}");
                }

                if (query.TryGetValue("code", out var code) &&
                    query.TryGetValue("state", out var state))
                {
                    if (!string.Equals(state, expectedState, StringComparison.Ordinal))
                        throw new InvalidOperationException("OAuth state mismatch (possible CSRF).");

                    await WriteAsync(stream,
                        "Signed in to Home Assistant. You can close this tab and return to the app.",
                        ct).ConfigureAwait(false);
                    return code;
                }

                await WriteAsync(stream, "Waiting for Home Assistant authorization...", ct).ConfigureAwait(false);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var newline = text.IndexOf('\r');
        return newline >= 0 ? text[..newline] : text;
    }

    private static string ExtractTarget(string requestLine)
    {
        // "GET /?code=...&state=... HTTP/1.1"
        var parts = requestLine.Split(' ');
        return parts.Length >= 2 ? parts[1] : "/";
    }

    private static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var q = target.IndexOf('?');
        if (q < 0) return result;
        foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
            result[key] = value;
        }
        return result;
    }

    private static async Task WriteAsync(NetworkStream stream, string message, CancellationToken ct)
    {
        var body = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{Branding.ProductName}</title></head>"
                 + $"<body style=\"font-family:Segoe UI,sans-serif;padding:2rem\">{WebUtility.HtmlEncode(message)}</body></html>";
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = "HTTP/1.1 200 OK\r\n"
                   + "Content-Type: text/html; charset=utf-8\r\n"
                   + $"Content-Length: {bytes.Length}\r\n"
                   + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
