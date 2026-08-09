using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion_App.Services;

/// <summary>
/// Reports free space, used space and usage percentage for the Windows system
/// drive, read through the standard volume APIs.
/// </summary>
/// <remarks>
/// Only the system drive is reported. Enumerating every volume would expose the
/// machine's storage layout, drag in removable, network and BitLocker-locked
/// volumes that appear and vanish, and turn the companion into an inventory
/// agent - none of which is the goal.
///
/// The volume is read every ten minutes rather than on every one-minute sync,
/// and the published snapshot is only replaced when
/// <see cref="DiskUsageFormatter.HasMeaningfullyChanged"/> says the movement is
/// worth a Home Assistant recorder row. All three sensors share the single
/// reading, so enabling all of them costs one query.
/// </remarks>
public sealed class DiskUsageSensorSource : ISensorSource, IRefreshableSensorSource
{
    public const string FreeSpaceId = "disk_free_space";
    public const string UsedSpaceId = "disk_used_space";
    public const string UsageId = "disk_usage";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private readonly Func<DiskUsage> _read;
    private readonly SensorPollLoop _loop;
    private readonly ChangeGate<DiskUsage> _usage =
        new(DiskUsage.Unavailable, DiskUsageFormatter.HasMeaningfullyChanged);

    private Action? _onChanged;

    public DiskUsageSensorSource(Func<DiskUsage>? read = null, TimeSpan? pollInterval = null)
    {
        _read = read ?? ReadSystemDrive;
        _loop = new SensorPollLoop(CaptureAsync, pollInterval ?? PollInterval);
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            UsageId,
            "Disk Usage",
            "How full the Windows system drive is, as a percentage.",
            SensorPrivacy.Benign,
            EnabledByDefault: true),
        new(
            FreeSpaceId,
            "Disk Free Space",
            "Free space on the Windows system drive. Off by default because the "
            + "value moves constantly and writes Home Assistant history.",
            SensorPrivacy.Benign,
            EnabledByDefault: false),
        new(
            UsedSpaceId,
            "Disk Used Space",
            "Used space on the Windows system drive. Off by default because the "
            + "value moves constantly and writes Home Assistant history.",
            SensorPrivacy.Benign,
            EnabledByDefault: false)
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
        Build(_usage.Current, enabled);

    public async ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        // The settings preview must show a real value even before the poller runs.
        var usage = await Task.Run(_read, cancellationToken).ConfigureAwait(false);
        return Build(usage, requested);
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
        var changed = _usage.TryUpdate(current);

        if (reason == SensorPollReason.Scheduled && changed) _onChanged?.Invoke();
    }

    private static IReadOnlyList<Sensor> Build(DiskUsage usage, IReadOnlySet<string> enabled)
    {
        var readings = new List<Sensor>();
        var percent = DiskUsageFormatter.UsedPercent(usage);

        if (enabled.Contains(UsageId))
        {
            readings.Add(new Sensor
            {
                UniqueId = UsageId,
                Type = "sensor",
                Name = "Disk Usage",
                State = (object?)percent ?? "unavailable",
                UnitOfMeasurement = "%",
                StateClass = "measurement",
                EntityCategory = "diagnostic",
                Icon = DiskUsageFormatter.IconFor(percent),
                Attributes = BuildAttributes(usage)
            });
        }

        if (enabled.Contains(FreeSpaceId))
        {
            readings.Add(Bytes(
                FreeSpaceId, "Disk Free Space", DiskUsageFormatter.FreeGigabytes(usage)));
        }

        if (enabled.Contains(UsedSpaceId))
        {
            readings.Add(Bytes(
                UsedSpaceId, "Disk Used Space", DiskUsageFormatter.UsedGigabytes(usage)));
        }

        return readings;
    }

    private static Sensor Bytes(string uniqueId, string name, double? gigabytes) => new()
    {
        UniqueId = uniqueId,
        Type = "sensor",
        Name = name,
        State = (object?)gigabytes ?? "unavailable",
        DeviceClass = "data_size",
        UnitOfMeasurement = "GB",
        StateClass = "measurement",
        EntityCategory = "diagnostic",
        Icon = "mdi:harddisk"
    };

    private static IDictionary<string, object>? BuildAttributes(DiskUsage usage)
    {
        var total = DiskUsageFormatter.TotalGigabytes(usage);
        if (total is null) return null;

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["total_gb"] = total.Value,
            ["free_gb"] = DiskUsageFormatter.FreeGigabytes(usage)!.Value,
            ["used_gb"] = DiskUsageFormatter.UsedGigabytes(usage)!.Value
        };
    }

    /// <summary>
    /// Reads the drive Windows booted from. A locked, disconnected or otherwise
    /// unreadable volume reports nothing rather than throwing.
    /// </summary>
    private static DiskUsage ReadSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(root)) return DiskUsage.Unavailable;

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) return DiskUsage.Unavailable;

            return new DiskUsage(drive.TotalSize, drive.TotalFreeSpace);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or System.Security.SecurityException)
        {
            return DiskUsage.Unavailable;
        }
    }
}
