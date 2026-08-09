namespace HaCompanion.Core.Sensors;

/// <summary>
/// Owns the network sensors' OS subscription and turns raw change notifications into
/// pushes that are actually worth making.
/// </summary>
/// <remarks>
/// Windows raises address and availability changes in bursts while an adapter comes
/// up, and most of those bursts leave the reported values identical. Pushing on each
/// one would mean a Home Assistant round trip per event, so a change is captured
/// once, compared with what was last reported, and only a genuine difference asks
/// for a push. Overlapping notifications collapse into the in-flight capture instead
/// of stacking up, and nothing is captured at all while no network sensor is
/// enabled.
/// </remarks>
public sealed class NetworkIdentityMonitor
{
    private readonly INetworkChangeWatcher _watcher;
    private readonly Func<NetworkCaptureScope, NetworkIdentity> _capture;
    private readonly Func<NetworkCaptureScope> _currentScope;
    private readonly object _gate = new();

    private Action? _onChanged;
    private NetworkIdentity? _lastReported;
    private bool _started;
    private bool _capturing;
    private bool _pending;

    public NetworkIdentityMonitor(
        INetworkChangeWatcher watcher,
        Func<NetworkCaptureScope, NetworkIdentity> capture,
        Func<NetworkCaptureScope> currentScope)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _currentScope = currentScope ?? throw new ArgumentNullException(nameof(currentScope));
    }

    /// <summary>
    /// Captures the current state for a read. <see cref="NetworkCaptureScope.None"/>
    /// captures nothing, so a sensor nobody enabled costs no enumeration.
    /// </summary>
    public NetworkIdentity Read(NetworkCaptureScope scope)
    {
        if (scope == NetworkCaptureScope.None) return NetworkIdentity.NotConnected;

        var identity = _capture(scope);
        lock (_gate) _lastReported = identity;
        return identity;
    }

    /// <summary>Subscribes to OS network changes. Repeated calls do not stack subscriptions.</summary>
    public void Start(Action onChanged)
    {
        lock (_gate)
        {
            _onChanged = onChanged;
            if (_started) return;
            _started = true;
        }

        _watcher.Start(OnNetworkChanged);
    }

    /// <summary>
    /// Releases the subscription. A notification already in flight when this runs is
    /// discarded rather than delivered to a caller that has stopped listening.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            _onChanged = null;
            _lastReported = null;
        }

        _watcher.Stop();
    }

    private void OnNetworkChanged()
    {
        lock (_gate)
        {
            if (!_started) return;

            // A burst folds into the capture already running instead of queueing.
            if (_capturing)
            {
                _pending = true;
                return;
            }

            _capturing = true;
        }

        try
        {
            while (true)
            {
                Notify();

                lock (_gate)
                {
                    if (!_pending || !_started) return;
                    _pending = false;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _capturing = false;
                _pending = false;
            }
        }
    }

    private void Notify()
    {
        var scope = _currentScope();
        if (scope == NetworkCaptureScope.None) return;

        var identity = _capture(scope);
        Action? onChanged;

        lock (_gate)
        {
            if (!_started || identity == _lastReported) return;
            _lastReported = identity;
            onChanged = _onChanged;
        }

        onChanged?.Invoke();
    }
}
