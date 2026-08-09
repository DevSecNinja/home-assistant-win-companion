using System.Net.NetworkInformation;
using HaCompanion.Core.Sensors;

namespace HaCompanion_App.Services;

/// <summary>
/// Windows' address and availability change events, as a subscription that can be
/// held exactly once. <see cref="NetworkChange"/> exposes static events, so an
/// unmatched handler would stay attached for the lifetime of the process.
/// </summary>
public sealed class SystemNetworkChangeWatcher : INetworkChangeWatcher
{
    private Action? _onChanged;

    public void Start(Action onChanged)
    {
        if (_onChanged is not null) return;

        _onChanged = onChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    public void Stop()
    {
        if (_onChanged is null) return;

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _onChanged = null;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _onChanged?.Invoke();
}
