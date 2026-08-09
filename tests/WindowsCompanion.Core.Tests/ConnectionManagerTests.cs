using System.Threading.Channels;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

[Collection(AsyncLifecycleCollection.Name)]
public class ConnectionManagerTests
{
    [Fact]
    public async Task Auth_error_is_terminal_and_stops_reconnecting()
    {
        var socket = new ScriptedSocket(
            """{"type":"auth_required"}""",
            """{"type":"auth_invalid"}""");
        var client = CreateManager(() => socket, new FakeClient(), new MutableClock());
        var authError = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += state =>
        {
            if (state == ConnectionState.AuthError) authError.TrySetResult();
        };

        client.Start();
        await authError.Task.WaitAsync(TimeSpan.FromSeconds(10));
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
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = CreateManager(
            () =>
            {
                attempts++;
                if (attempts >= 2) retried.TrySetResult();
                return attempts == 1
                    ? new ThrowingSocket()
                    : new BlockingSocket();
            },
            new FakeClient(),
            new MutableClock());
        client.StateChanged += states.Add;

        client.Start();
        await retried.Task.WaitAsync(TimeSpan.FromSeconds(10));

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

    [Fact]
    public async Task Repeated_sync_failures_ask_for_another_address_to_be_tried()
    {
        var homeAssistant = new FakeClient();
        var client = CreateManager(
            () => new BlockingSocket(), homeAssistant, new MutableClock(), route: RouteKind.Internal);
        var reported = new List<RouteKind?>();
        client.RouteUnhealthy += reported.Add;

        client.Start();
        await WaitUntilAsync(() => client.LastSyncedAt is not null);

        homeAssistant.FailNextUpdate = true;
        await client.SyncNowAsync();
        // One failure is a blip, not a reason to move the whole connection.
        Assert.Empty(reported);

        homeAssistant.FailNextUpdate = true;
        await client.SyncNowAsync();

        Assert.Equal([RouteKind.Internal], reported);
        Assert.Equal(RouteKind.Internal, client.Route);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task A_connection_with_no_route_still_reports_failover_interest()
    {
        var homeAssistant = new FakeClient();
        var client = CreateManager(() => new BlockingSocket(), homeAssistant, new MutableClock());
        var raised = 0;
        client.RouteUnhealthy += _ => raised++;

        client.Start();
        await WaitUntilAsync(() => client.LastSyncedAt is not null);
        homeAssistant.FailNextUpdate = true;
        await client.SyncNowAsync();
        homeAssistant.FailNextUpdate = true;
        await client.SyncNowAsync();

        Assert.Equal(1, raised);
        Assert.Null(client.Route);
        await client.DisposeAsync();
    }

    private static ConnectionManager CreateManager(
        Func<IHaSocket> socketFactory,
        FakeClient client,
        MutableClock clock,
        TimeSpan? syncInterval = null,
        RouteKind? route = null)
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
            clock,
            route: route);
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

        public Task<HaInstanceInfo?> GetInstanceInfoAsync(
            string webhookId, CancellationToken ct = default) =>
            Task.FromResult<HaInstanceInfo?>(new HaInstanceInfo { DeviceId = "device" });

        public Task<HaConfigInfo?> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult<HaConfigInfo?>(null);
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
