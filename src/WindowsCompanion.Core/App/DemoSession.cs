using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.App;

/// <summary>
/// A local-only tour of the sensor catalog for someone who has not connected a
/// Home Assistant server yet: every sensor can be browsed, switched on and read
/// on this PC, and nothing is registered, persisted or transmitted.
/// </summary>
/// <remarks>
/// The catalog is deliberately never started. A started catalog installs the OS
/// hooks and poll loops of every enabled source and asks for immediate pushes,
/// which is exactly what a demo without a server must not do. Values shown come
/// from <see cref="SensorCatalog.PreviewAsync"/>, which reads once on request and
/// still honours <see cref="SensorPreviewGate"/>, so a privacy-sensitive value is
/// only read after the user switches that sensor on.
/// </remarks>
public sealed class DemoSession
{
    /// <summary>Heading of the warning shown on every screen while the demo runs.</summary>
    public const string Title = "Demo mode";

    /// <summary>The warning itself, shown on every screen while the demo runs.</summary>
    public const string Message =
        "This is a preview of what this PC could report. No Home Assistant server is "
        + "connected, nothing is registered, and no sensor value leaves this device. "
        + "Sensor choices made here are not saved.";

    /// <summary>Health line for the status view and the tray tooltip.</summary>
    public const string HealthSummary = "Demo mode: nothing is sent to Home Assistant";

    /// <summary>What the status view shows instead of a server address.</summary>
    public const string ServerLabel = "No server (demo mode)";

    /// <summary>What the status view and tray tooltip show instead of a route.</summary>
    public const string RouteSummary = "Demo mode";

    private readonly SensorCatalog _catalog;

    public DemoSession(IEnumerable<ISensorSource> sources, SensorPreferences? preferences = null)
    {
        Preferences = preferences ?? new SensorPreferences();
        _catalog = new SensorCatalog(sources, Preferences);
    }

    /// <summary>The catalog to render. Started by nobody, for the whole demo.</summary>
    public SensorCatalog Catalog => _catalog;

    /// <summary>The in-memory sensor choices. Never written to settings.json.</summary>
    public SensorPreferences Preferences { get; }

    /// <summary>Switches a sensor on or off for the demo only.</summary>
    public void SetSensorEnabled(string uniqueId, bool enabled) =>
        _catalog.SetEnabled(uniqueId, enabled);

    /// <summary>Reads what every sensor would report, locally.</summary>
    public Task<IReadOnlyDictionary<string, string>> PreviewAsync(
        CancellationToken cancellationToken = default) =>
        _catalog.PreviewAsync(cancellationToken);

    /// <summary>
    /// Ends the demo. Nothing should be running, so this only releases a source
    /// that a caller started behind the session's back.
    /// </summary>
    public void End() => _catalog.Stop();
}
