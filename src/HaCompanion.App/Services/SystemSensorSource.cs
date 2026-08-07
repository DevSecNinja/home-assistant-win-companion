using Microsoft.Win32;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion_App.Services;

/// <summary>
/// System diagnostics: the Windows version and when the machine last booted.
/// Neither changes during a session, so there is nothing to observe.
/// </summary>
public sealed class SystemSensorSource : ISensorSource
{
    public const string OsVersionId = "os_version";
    public const string LastBootId = "last_boot";

    private DateTimeOffset? _bootTime;

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

        return readings;
    }

    public void Start(Action onChanged) { }

    public void Stop() { }

    /// <summary>
    /// Boot time derived from the tick count drifts by a few milliseconds on every
    /// read, and a timestamp sensor that changes on every push would fill Home
    /// Assistant's history with meaningless state changes. So the first value is
    /// cached and only recomputed if it moves substantially, which happens when the
    /// machine resumes from hibernation (sleep does not advance the tick count).
    /// </summary>
    private DateTimeOffset GetBootTime()
    {
        var measured = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

        if (_bootTime is null || Math.Abs((measured - _bootTime.Value).TotalSeconds) > 60)
            _bootTime = new DateTimeOffset(measured.UtcDateTime.AddTicks(-(measured.UtcTicks % TimeSpan.TicksPerSecond)), TimeSpan.Zero);

        return _bootTime.Value;
    }

    private static string DescribeOs()
    {
        // Environment.OSVersion reports the build but not the marketing name, which
        // is what a user actually recognises; the registry has both.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string;
            var display = key?.GetValue("DisplayVersion") as string;
            var build = key?.GetValue("CurrentBuild") as string;
            var ubr = key?.GetValue("UBR");

            // Windows 11 still reports "Windows 10 ..." in ProductName.
            if (product is not null && int.TryParse(build, out var buildNumber) && buildNumber >= 22000)
                product = product.Replace("Windows 10", "Windows 11");

            var version = ubr is not null ? $"{build}.{ubr}" : build;
            var parts = new[] { product, display, version }.Where(p => !string.IsNullOrEmpty(p));
            var text = string.Join(' ', parts);

            return string.IsNullOrWhiteSpace(text) ? Environment.OSVersion.Version.ToString() : text;
        }
        catch
        {
            return Environment.OSVersion.Version.ToString();
        }
    }
}
