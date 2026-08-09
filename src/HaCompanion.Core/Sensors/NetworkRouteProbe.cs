using System.Net.Sockets;

namespace HaCompanion.Core.Sensors;

/// <summary>
/// Runs a route lookup against a disposable OS handle. Windows answers "which local
/// address would carry this traffic" by connecting a UDP socket, which resolves the
/// route without transmitting a packet, and the handle must be released whether the
/// lookup succeeds, fails or is cancelled - a leaked socket per network change would
/// accumulate for as long as the app runs.
/// </summary>
public static class NetworkRouteProbe
{
    /// <summary>
    /// Opens a probe, reads the local address from it and always disposes it.
    /// Returns null when the OS cannot answer, which is the normal case for a family
    /// the machine has no route for. Cancellation is never swallowed.
    /// </summary>
    public static string? Resolve<TProbe>(Func<TProbe> open, Func<TProbe, string?> read)
        where TProbe : IDisposable
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(read);

        TProbe probe;
        try
        {
            probe = open();
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return null;
        }

        try
        {
            return read(probe);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return null;
        }
        finally
        {
            probe.Dispose();
        }
    }

    private static bool IsUnavailable(Exception exception) => exception
        is SocketException
        or NotSupportedException
        or ObjectDisposedException;
}
