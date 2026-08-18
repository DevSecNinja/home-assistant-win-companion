using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

public sealed class WireGuardSensorSource : ISensorSource
{
    public const string StatusId = "wireguard_status";

    private readonly IWireGuardStatusProbe _probe;
    private readonly INetworkChangeWatcher _watcher;
    private readonly object _gate = new();

    private Action? _onChanged;
    private WireGuardStatus _lastReported;
    private bool _hasLastReported;
    private bool _started;
    private bool _capturing;
    private bool _pending;
    private long _generation;

    public WireGuardSensorSource()
        : this(new WindowsWireGuardStatusProbe(), new SystemNetworkChangeWatcher())
    {
    }

    internal WireGuardSensorSource(
        IWireGuardStatusProbe probe,
        INetworkChangeWatcher watcher)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            StatusId,
            "WireGuard Status",
            "Whether an official WireGuard tunnel is locally ready. This does not "
            + "verify a recent handshake, peer reachability, or internet access.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Reads local Windows service and adapter state during normal "
                           + "sensor updates and after meaningful network changes. Does not "
                           + "run WireGuard tools or request administrator access.",
            AutomationIdea: "When WireGuard connects, enable devices that should only be "
                            + "available through the VPN.",
            OptInPlaceholder: "Enable to read WireGuard status")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled,
        SensorReadContext context)
    {
        if (!enabled.Contains(StatusId))
            return [];

        var status = _probe.Read();
        lock (_gate)
        {
            _lastReported = status;
            _hasLastReported = true;
        }

        return Build(status);
    }

    public async ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        if (!requested.Contains(StatusId))
            return [];

        var status = await Task.Run(_probe.Read, cancellationToken).ConfigureAwait(false);
        return Build(status);
    }

    public void Start(Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        lock (_gate)
        {
            _onChanged = onChanged;
            if (_started)
                return;
            _started = true;
            _generation++;
            _capturing = false;
            _pending = false;
        }

        try
        {
            _watcher.Start(OnNetworkChanged);
        }
        catch (Exception startFailure)
        {
            Exception? stopFailure = null;
            try
            {
                _watcher.Stop();
            }
            catch (Exception ex)
            {
                stopFailure = ex;
            }

            lock (_gate)
            {
                _started = false;
                _onChanged = null;
                _hasLastReported = false;
                _pending = false;
                _capturing = false;
            }

            if (stopFailure is not null)
            {
                throw new AggregateException(
                    "Could not start or unwind WireGuard network monitoring.",
                    startFailure,
                    stopFailure);
            }

            throw;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
                return;

            _started = false;
            _onChanged = null;
            _hasLastReported = false;
            _pending = false;
        }

        _watcher.Stop();
    }

    private static IReadOnlyList<Sensor> Build(WireGuardStatus status) =>
    [
        new()
        {
            UniqueId = StatusId,
            Type = "sensor",
            Name = "WireGuard Status",
            State = WireGuardStatusFormatter.Format(status),
            EntityCategory = "diagnostic",
            Icon = "mdi:vpn"
        }
    ];

    private void OnNetworkChanged()
    {
        long generation;
        lock (_gate)
        {
            if (!_started)
                return;

            _pending = true;
            if (_capturing)
                return;
            _capturing = true;
            generation = _generation;
        }

        while (true)
        {
            lock (_gate)
            {
                if (!_started || generation != _generation)
                {
                    return;
                }

                _pending = false;
            }

            try
            {
                NotifyIfChanged(generation);
            }
            catch
            {
                lock (_gate)
                {
                    if (generation == _generation)
                        _capturing = false;
                }
                throw;
            }

            lock (_gate)
            {
                if (!_started || generation != _generation)
                {
                    return;
                }

                if (_pending)
                    continue;

                _capturing = false;
                return;
            }
        }
    }

    private void NotifyIfChanged(long generation)
    {
        var status = _probe.Read();
        Action? onChanged;

        lock (_gate)
        {
            if (!_started || generation != _generation)
                return;

            if (_hasLastReported && status == _lastReported)
                return;

            _lastReported = status;
            _hasLastReported = true;
            onChanged = _onChanged;
        }

        onChanged?.Invoke();
    }
}
