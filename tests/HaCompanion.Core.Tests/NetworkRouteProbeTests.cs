using System.Net.Sockets;
using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

/// <summary>
/// The route lookup runs on every network change, so a probe that survives a failure
/// would leak a handle per event. These pin the disposal contract.
/// </summary>
public class NetworkRouteProbeTests
{
    [Fact]
    public void Returns_the_local_address_and_releases_the_probe()
    {
        var probe = new FakeProbe();

        var result = NetworkRouteProbe.Resolve(() => probe, _ => "192.168.1.20");

        Assert.Equal("192.168.1.20", result);
        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public void Releases_the_probe_when_the_route_cannot_be_resolved()
    {
        var probe = new FakeProbe();

        var result = NetworkRouteProbe.Resolve<FakeProbe>(
            () => probe,
            _ => throw new SocketException((int)SocketError.NetworkUnreachable));

        Assert.Null(result);
        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public void Treats_an_unsupported_address_family_as_no_route()
    {
        var probe = new FakeProbe();

        var result = NetworkRouteProbe.Resolve<FakeProbe>(
            () => probe, _ => throw new NotSupportedException());

        Assert.Null(result);
        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public void Releases_the_probe_when_the_lookup_is_cancelled()
    {
        var probe = new FakeProbe();

        Assert.Throws<OperationCanceledException>(() => NetworkRouteProbe.Resolve<FakeProbe>(
            () => probe, _ => throw new OperationCanceledException()));

        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public void Releases_the_probe_when_the_lookup_fails_unexpectedly()
    {
        var probe = new FakeProbe();

        Assert.Throws<InvalidOperationException>(() => NetworkRouteProbe.Resolve<FakeProbe>(
            () => probe, _ => throw new InvalidOperationException()));

        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public void Reports_no_route_when_the_probe_cannot_even_be_opened()
    {
        var result = NetworkRouteProbe.Resolve<FakeProbe>(
            () => throw new SocketException((int)SocketError.AddressFamilyNotSupported),
            _ => "unreachable");

        Assert.Null(result);
    }

    [Fact]
    public void Repeated_lookups_never_accumulate_probes()
    {
        var opened = new List<FakeProbe>();

        for (var attempt = 0; attempt < 200; attempt++)
        {
            NetworkRouteProbe.Resolve<FakeProbe>(
                () =>
                {
                    var probe = new FakeProbe();
                    opened.Add(probe);
                    return probe;
                },
                p => p.Failing
                    ? throw new SocketException((int)SocketError.NetworkUnreachable)
                    : "192.168.1.20");
        }

        Assert.Equal(200, opened.Count);
        Assert.All(opened, probe => Assert.Equal(1, probe.DisposeCount));
    }

    private sealed class FakeProbe : IDisposable
    {
        private static int _sequence;

        public FakeProbe() => Failing = Interlocked.Increment(ref _sequence) % 3 == 0;

        public bool Failing { get; }

        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
