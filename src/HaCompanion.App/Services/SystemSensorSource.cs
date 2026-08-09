using Microsoft.Win32;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion_App.Services;

/// <summary>
/// System diagnostics: the Windows version, this PC's model, and when the
/// machine last booted. None of them changes during a session, so there is
/// nothing to observe.
/// </summary>
public sealed class SystemSensorSource : ISensorSource
{
    public const string OsVersionId = "os_version";
    public const string LastBootId = "last_boot";
    public const string HostModelId = "host_model";

    private readonly BootTimeCalculator _bootTime = new();

    public IReadOnlyList<SensorDefinition> Definitions { get; } = new[]
    {
        new SensorDefinition(
            OsVersionId,
            "OS Version",
            "The Windows edition and build running on this PC.",
            SensorPrivacy.Benign,
            EnabledByDefault: true),
        new SensorDefinition(
            LastBootId,
            "Last Boot",
            "When this PC last started up.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            AutomationIdea: "When the last boot is over 30 days ago, send a reminder to reboot."),
        new SensorDefinition(
            HostModelId,
            "Model",
            "This PC's manufacturer and model, for example Dell Precision 5560. "
            + "No serial number, service tag or other unique identifier is read.",
            SensorPrivacy.Benign,
            EnabledByDefault: true)
    };

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        var readings = new List<Sensor>();

        if (enabled.Contains(OsVersionId))
        {
            readings.Add(new Sensor
            {
                UniqueId = OsVersionId,
                Type = "sensor",
                Name = "OS Version",
                State = DescribeOs(),
                EntityCategory = "diagnostic",
                Icon = "mdi:microsoft-windows"
            });
        }

        if (enabled.Contains(LastBootId))
        {
            readings.Add(new Sensor
            {
                UniqueId = LastBootId,
                Type = "sensor",
                Name = "Last Boot",
                State = GetBootTime().ToString("o"),
                DeviceClass = "timestamp",
                EntityCategory = "diagnostic",
                Icon = "mdi:restart"
            });
        }

        if (enabled.Contains(HostModelId))
        {
            readings.Add(new Sensor
            {
                UniqueId = HostModelId,
                Type = "sensor",
                Name = "Model",
                State = HardwareInfo.DescribeModel(),
                EntityCategory = "diagnostic",
                Icon = "mdi:desktop-tower-monitor"
            });
        }

        return readings;
    }

    public void Start(Action onChanged) { }

    public void Stop() { }

    /// <summary>
    /// Boot time is derived from the tick count and stabilised by
    /// <see cref="BootTimeCalculator"/>, so it does not jitter on every read.
    /// </summary>
    private DateTimeOffset GetBootTime() =>
        _bootTime.Resolve(DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64));

    private static string DescribeOs()
    {
        // Environment.OSVersion reports the build but not the marketing name, which
        // is what a user actually recognises; the registry has both.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            return OsVersionFormatter.Describe(
                key?.GetValue("ProductName") as string,
                key?.GetValue("DisplayVersion") as string,
                key?.GetValue("CurrentBuild") as string,
                key?.GetValue("UBR")?.ToString(),
                Environment.OSVersion.Version.ToString());
        }
        catch
        {
            return Environment.OSVersion.Version.ToString();
        }
    }
}
