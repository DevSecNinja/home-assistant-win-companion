using System.Net;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.App;

/// <summary>Why a route address was rejected, in terms the UI can show.</summary>
public enum RouteUrlProblem
{
    None,

    /// <summary>The address is not a usable absolute HTTP(S) URL.</summary>
    Invalid,

    /// <summary>The external address is plain HTTP, which would expose the token.</summary>
    ExternalMustUseHttps,

    /// <summary>A redirect moved the request to an unrelated host.</summary>
    RedirectedToDifferentHost,

    /// <summary>An HTTPS address redirected down to plain HTTP.</summary>
    RedirectDowngradedToHttp
}

/// <summary>Result of normalizing one of the two configured addresses.</summary>
/// <param name="Url">The normalized address, when accepted.</param>
/// <param name="Problem">Why it was rejected, or <see cref="RouteUrlProblem.None"/>.</param>
/// <param name="Message">User-facing explanation.</param>
/// <param name="InsecureTransport">
/// True for an accepted internal HTTP address, so the UI can warn.
/// </param>
public sealed record RouteUrlResult(
    string? Url,
    RouteUrlProblem Problem,
    string? Message = null,
    bool InsecureTransport = false)
{
    /// <summary>True when the address is usable, or simply not configured.</summary>
    public bool Accepted => Problem == RouteUrlProblem.None;

    /// <summary>True when this route has no address at all.</summary>
    public bool IsEmpty => Accepted && Url is null;
}

/// <summary>
/// Transport rules for the internal and external addresses, and the redirect
/// guards applied when a server rewrites the address we asked for.
/// </summary>
/// <remarks>
/// Kept separate from connectivity so the security decisions can be tested
/// exhaustively without a network. Certificate validation is never relaxed: an
/// internal HTTPS address with a private certificate must be trusted by Windows,
/// which is a deliberate future design rather than a bypass switch.
/// </remarks>
public static class RouteUrlPolicy
{
    /// <summary>
    /// Normalizes an address for a route and applies the transport rule for it:
    /// the external address must be HTTPS, while internal HTTP is accepted only
    /// with a warning about the transport risk the user is choosing.
    /// </summary>
    public static RouteUrlResult Normalize(string? url, RouteKind route)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new RouteUrlResult(null, RouteUrlProblem.None);

        string normalized;
        try
        {
            normalized = ServerUrlNormalizer.Normalize(url);
        }
        catch (ArgumentException ex)
        {
            return new RouteUrlResult(null, RouteUrlProblem.Invalid, ex.Message);
        }

        var isHttp = new Uri(normalized).Scheme == Uri.UriSchemeHttp;

        if (route == RouteKind.External && isHttp)
        {
            return new RouteUrlResult(
                null,
                RouteUrlProblem.ExternalMustUseHttps,
                "The external address must use HTTPS. Sending your Home Assistant "
                + "credentials over plain HTTP would expose them.");
        }

        return new RouteUrlResult(
            normalized,
            RouteUrlProblem.None,
            isHttp
                ? "This internal address uses plain HTTP. Use it only if you deliberately "
                  + "accept that anyone on the matched local network could read or alter "
                  + "the traffic; HTTPS is recommended."
                : null,
            isHttp);
    }

    /// <summary>
    /// Checks where following redirects actually landed. A captive portal, a
    /// DNS-rebinding attempt and a misconfigured reverse proxy all show up the
    /// same way: the effective host is not the one the user typed.
    /// </summary>
    public static RouteUrlResult ValidateRedirect(string requested, string resolved, RouteKind route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requested);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolved);

        var from = new Uri(requested);
        var to = new Uri(resolved);

        if (from.Scheme == Uri.UriSchemeHttps && to.Scheme == Uri.UriSchemeHttp)
        {
            return new RouteUrlResult(
                null,
                RouteUrlProblem.RedirectDowngradedToHttp,
                "The address redirected from HTTPS to plain HTTP, which would expose "
                + "your credentials. Fix the redirect on the server before using it.");
        }

        if (route == RouteKind.External && to.Scheme == Uri.UriSchemeHttp)
        {
            return new RouteUrlResult(
                null,
                RouteUrlProblem.ExternalMustUseHttps,
                "The external address ended up on plain HTTP after redirects.");
        }

        if (!string.Equals(from.Host, to.Host, StringComparison.OrdinalIgnoreCase))
        {
            return new RouteUrlResult(
                null,
                RouteUrlProblem.RedirectedToDifferentHost,
                "The address redirected to a different host. That is what a captive "
                + "portal or a hijacked DNS answer looks like, so it was not used. "
                + "Enter the address the server actually serves.");
        }

        return new RouteUrlResult(resolved, RouteUrlProblem.None,
            InsecureTransport: to.Scheme == Uri.UriSchemeHttp);
    }

    /// <summary>
    /// True for addresses that can only exist inside a LAN. Used to suggest a
    /// classification and to warn about an obviously wrong one - never to decide
    /// on the user's behalf.
    /// </summary>
    public static bool LooksPrivate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return true;
        if (!host.Contains('.') && !IPAddress.TryParse(host, out _)) return true;

        if (!IPAddress.TryParse(host, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;

        return bytes[0] switch
        {
            10 => true,
            127 => true,
            169 when bytes[1] == 254 => true,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
            192 when bytes[1] == 168 => true,
            _ => false
        };
    }
}
