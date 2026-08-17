using Microsoft.Win32;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reports whether Windows needs a restart to finish applying updates or a
/// pending file operation, read from the standard registry locations Windows
/// itself uses.
/// </summary>
/// <remarks>
/// The three signals are simple key/value existence checks - no PowerShell,
/// no Update Agent COM calls, no elevation - so like
/// <see cref="DiskUsageSensorSource"/> the registry is read every ten minutes
/// rather than on every sync, and the published snapshot only changes (and
/// pushes) when the pending-reboot state actually flips.
/// </remarks>
public sealed class PendingRebootSensorSource : ISensorSource, IRefreshableSensorSource
{
    public const string PendingRebootId = "pending_reboot";

    private const string WindowsUpdateRebootRequiredKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

    private const string ComponentBasedServicingRebootPendingKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";

    private const string SessionManagerKey =
        @"SYSTEM\CurrentControlSet\Control\Session Manager";

    private const string PendingFileRenameOperationsValue = "PendingFileRenameOperations";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private readonly Func<PendingRebootState> _read;
    private readonly SensorPollLoop _loop;
    private readonly ChangeGate<PendingRebootState> _state =
        new(PendingRebootState.None, PendingRebootFormatter.HasMeaningfullyChanged);

    private Action? _onChanged;

    public PendingRebootSensorSource(Func<PendingRebootState>? read = null, TimeSpan? pollInterval = null)
    {
        _read = read ?? QueryPendingReboot;
        _loop = new SensorPollLoop(CaptureAsync, pollInterval ?? PollInterval);
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            PendingRebootId,
            "Pending Reboot",
            "On while Windows needs a restart to finish applying updates or a pending "
            + "file operation.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            ResourceUsage: "Low. Checks a handful of registry values every 10 minutes. Sends "
                           + "an extra update only when the pending-reboot state changes.",
            AutomationIdea: "When a reboot becomes pending, send a reminder to restart before "
                             + "the next meeting.")
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
        Build(_state.Current, enabled);

    public async ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        // The settings preview must show a real value even before the poller runs.
        var state = await Task.Run(_read, cancellationToken).ConfigureAwait(false);
        return Build(state, requested);
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        _loop.Start();
    }

    public void Stop() => _loop.Stop();

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        _loop.RunOnceAsync(cancellationToken);

    private async Task CaptureAsync(SensorPollReason reason, CancellationToken cancellationToken)
    {
        var current = await Task.Run(_read, cancellationToken).ConfigureAwait(false);
        var changed = _state.TryUpdate(current);

        if (reason == SensorPollReason.Scheduled && changed) _onChanged?.Invoke();
    }

    private static IReadOnlyList<Sensor> Build(PendingRebootState state, IReadOnlySet<string> enabled)
    {
        if (!enabled.Contains(PendingRebootId)) return [];

        return
        [
            new()
            {
                UniqueId = PendingRebootId,
                Type = "binary_sensor",
                Name = "Pending Reboot",
                State = state.IsRebootPending,
                DeviceClass = "problem",
                EntityCategory = "diagnostic",
                Icon = PendingRebootFormatter.IconFor(state),
                Attributes = PendingRebootFormatter.BuildAttributes(state)
            }
        ];
    }

    /// <summary>
    /// Reads the three standard pending-reboot signals. Each check reports
    /// "not pending" rather than throwing when the key is unreadable, so a
    /// locked-down or unusual environment never breaks the sensor.
    /// </summary>
    private static PendingRebootState QueryPendingReboot() =>
        new(
            WindowsUpdateRebootRequired: KeyExists(WindowsUpdateRebootRequiredKey),
            ComponentBasedServicingRebootPending: KeyExists(ComponentBasedServicingRebootPendingKey),
            PendingFileRenameOperations: HasPendingFileRenameOperations());

    private static bool KeyExists(string path)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or System.Security.SecurityException
                                       or IOException)
        {
            return false;
        }
    }

    private static bool HasPendingFileRenameOperations()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SessionManagerKey);
            return key?.GetValue(PendingFileRenameOperationsValue) is string[] { Length: > 0 };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or System.Security.SecurityException
                                       or IOException)
        {
            return false;
        }
    }
}
