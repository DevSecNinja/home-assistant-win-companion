using System.Threading.Channels;
using HaCompanion.Core.Abstractions;
using HaCompanion.Core.App;
using HaCompanion.Core.HomeAssistant;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

public class ConnectionManagerTests
{
    [Fact]
    public async Task Auth_error_is_terminal_and_stops_reconnecting()
    {
        var socket = new ScriptedSocket(
            """{"type":"auth_required"}""",
            """{"type":"auth_invalid"}""");
        var client = CreateManager(() => socket, new FakeClient(), new MutableClock());

        client.Start();
        await WaitUntilAsync(() => client.State == ConnectionState.AuthError);
        var connectCount = socket.ConnectCount;
        await Task.Delay(100);

        Assert.Equal(ConnectionState.AuthError, client.State);
        Assert.Equal(connectCount, socket.ConnectCount);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Socket_failure_transitions_through_reconnecting_and_retries()
    {
        var attempts = 0;
        var states = new List<ConnectionState>();
        var client = CreateManager(
            () =>
            {
                attempts++;
                return attempts == 1
                    ? new ThrowingSocket()
                    : new BlockingSocket();
            },
            new FakeClient(),
            new MutableClock());
        client.StateChanged += states.Add;

        client.Start();
        await WaitUntilAsync(() => attempts >= 2);

        Assert.Contains(ConnectionState.Reconnecting, states);
        Assert.True(attempts >= 2);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Health_tracks_success_failure_and_staleness()
    {
        var clock = new MutableClock();
        var homeAssistant = new FakeClient();
        var client = CreateManager(
            () => new BlockingSocket(),
            homeAssistant,
            clock,
            syncInterval: TimeSpan.FromMinutes(1));

        client.Start();
        await WaitUntilAsync(() => client.LastSyncedAt is not null);
        Assert.True(client.IsHealthy);

        clock.UtcNow += TimeSpan.FromMinutes(3);
        Assert.False(client.IsHealthy);

        homeAssistant.FailNextUpdate = true;
        await client.SyncNowAsync();
        Assert.Equal(1, client.ConsecutiveFailures);
        Assert.False(client.IsHealthy);

        await client.SyncNowAsync();
        Assert.Equal(0, client.ConsecutiveFailures);
        Assert.True(client.IsHealthy);
        await client.DisposeAsync();
    }

    [Fact]
    public void Backoff_grows_and_caps_at_sixty_seconds()
    {
        var client = CreateManager(
            () => new BlockingSocket(), new FakeClient(), new MutableClock());

        Assert.InRange(client.NextBackoff(0), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5));
        Assert.InRange(client.NextBackoff(3), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12));
        Assert.Equal(TimeSpan.FromSeconds(60), client.NextBackoff(20));
    }

    private static ConnectionManager CreateManager(
        Func<IHaSocket> socketFactory,
        FakeClient client,
        MutableClock clock,
        TimeSpan? syncInterval = null)
    {
        var webSocket = new HaWebSocketClient(
            socketFactory,
            "https://ha.local:8123",
            new StaticTokenProvider("token"),
            "hook");
        var catalog = new SensorCatalog(
            [new StubSource()],
            new SensorPreferences());
        var sensors = new SensorSyncService(client, catalog);
        return new ConnectionManager(
            webSocket,
            sensors,
            "hook",
            syncInterval ?? TimeSpan.FromHours(1),
            clock);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeClient : IHomeAssistantClient
    {
        public bool FailNextUpdate { get; set; }

        public Task<bool> ValidateAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<DeviceRegistrationResponse> RegisterDeviceAsync(
            DeviceRegistrationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new DeviceRegistrationResponse { WebhookId = "hook" });

        public Task UpdateRegistrationAsync(
            string webhookId, DeviceRegistrationRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RegisterSensorAsync(
            string webhookId, Sensor sensor, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateSensorsAsync(
            string webhookId, IReadOnlyList<Sensor> sensors, CancellationToken ct = default)
        {
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                throw new HttpRequestException("sync failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubSource : ISensorSource
    {
        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new("stub", "Stub", "Test sensor.", SensorPrivacy.Benign, true)
        ];

        public IReadOnlyList<Sensor> Read(
            IReadOnlySet<string> enabled, SensorReadContext context) =>
        [
            new() { UniqueId = "stub", Name = "Stub", State = 1 }
        ];

        public void Start(Action onChanged) { }
        public void Stop() { }
    }

    private sealed class ScriptedSocket : IHaSocket
    {
        private readonly Channel<string?> _messages = Channel.CreateUnbounded<string?>();
        public int ConnectCount { get; private set; }

        public ScriptedSocket(params string[] messages)
        {
            foreach (var message in messages) _messages.Writer.TryWrite(message);
        }

        public Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            ConnectCount++;
            return Task.CompletedTask;
        }

        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;

        public async Task<string?> ReceiveAsync(CancellationToken ct) =>
            await _messages.Reader.ReadAsync(ct);

        public void Dispose() { }
    }

    private sealed class ThrowingSocket : IHaSocket
    {
        public Task ConnectAsync(Uri uri, CancellationToken ct) =>
            throw new IOException("offline");
        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> ReceiveAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    private sealed class BlockingSocket : IHaSocket
    {
        public Task ConnectAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;

        public async Task<string?> ReceiveAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        }

        public void Dispose() { }
    }
}
