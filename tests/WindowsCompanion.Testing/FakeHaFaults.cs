using System.Collections.Concurrent;

namespace WindowsCompanion.Testing;

/// <summary>Identifies a fake-server operation that tests can pause.</summary>
public enum FakeHaFaultPoint
{
    /// <summary>OAuth authorization.</summary>
    Authorization,
    /// <summary>Authorization-code exchange.</summary>
    TokenExchange,
    /// <summary>Refresh-token exchange.</summary>
    Refresh,
    /// <summary>Authenticated REST API access.</summary>
    Api,
    /// <summary>Mobile-app registration.</summary>
    Registration,
    /// <summary>Webhook handling.</summary>
    Webhook,
    /// <summary>WebSocket authentication.</summary>
    WebSocketAuthentication,
    /// <summary>Push-channel subscription.</summary>
    PushSubscription
}

/// <summary>Identifies a WebSocket lifecycle step at which the server can disconnect.</summary>
public enum FakeHaWebSocketStep
{
    /// <summary>The socket has connected.</summary>
    Connected,
    /// <summary>The server has requested authentication.</summary>
    AuthRequired,
    /// <summary>The client has authenticated.</summary>
    Authenticated,
    /// <summary>The client has subscribed to push notifications.</summary>
    PushSubscribed
}

/// <summary>Provides deterministic rejection, availability, pause, and disconnect controls.</summary>
public sealed class FakeHaFaults : IDisposable
{
    private readonly ConcurrentDictionary<FakeHaFaultPoint, HoldState> _holds = new();

    /// <summary>Gets or sets whether authorization-code exchange is rejected.</summary>
    public bool RejectAuthorizationCode { get; set; }
    /// <summary>Gets or sets whether refresh-token exchange is rejected.</summary>
    public bool RejectRefreshToken { get; set; }
    /// <summary>Gets or sets whether REST and webhook operations are unavailable.</summary>
    public bool ApiUnavailable { get; set; }
    /// <summary>Gets or sets whether mobile-app registration is unavailable.</summary>
    public bool MobileAppUnavailable { get; set; }
    /// <summary>Gets or sets the sensor unique ID whose state update is rejected.</summary>
    public string? RejectSensorUniqueId { get; set; }
    /// <summary>Gets or sets whether the scenario webhook is treated as unknown.</summary>
    public bool UnknownWebhook { get; set; }
    /// <summary>Gets or sets the WebSocket lifecycle step at which the server disconnects.</summary>
    public FakeHaWebSocketStep? ClosePushChannelAt { get; set; }

    /// <summary>Pauses the selected operation until <see cref="Release"/> is called.</summary>
    public void Hold(FakeHaFaultPoint point)
    {
        Release(point);
        _holds[point] = new HoldState();
    }

    /// <summary>Waits for a configured hold to be released, or returns immediately.</summary>
    public async Task WaitIfHeldAsync(
        FakeHaFaultPoint point,
        CancellationToken cancellationToken)
    {
        if (!_holds.TryGetValue(point, out var hold)) return;
        hold.Entered.TrySetResult();
        await hold.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Waits until server execution reaches a configured hold.</summary>
    public Task WaitUntilHeldAsync(
        FakeHaFaultPoint point,
        CancellationToken cancellationToken = default) =>
        _holds.TryGetValue(point, out var hold)
            ? hold.Entered.Task.WaitAsync(cancellationToken)
            : throw new InvalidOperationException($"No hold is configured for {point}.");

    /// <summary>Releases the selected held operation.</summary>
    public void Release(FakeHaFaultPoint point)
    {
        if (_holds.TryRemove(point, out var hold)) hold.Release.TrySetResult();
    }

    /// <summary>Clears all configured faults and releases held operations.</summary>
    public void Reset()
    {
        RejectAuthorizationCode = false;
        RejectRefreshToken = false;
        ApiUnavailable = false;
        MobileAppUnavailable = false;
        RejectSensorUniqueId = null;
        UnknownWebhook = false;
        ClosePushChannelAt = null;
        foreach (var point in _holds.Keys) Release(point);
    }

    /// <inheritdoc />
    public void Dispose() => Reset();

    private sealed class HoldState
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
