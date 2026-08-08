using System.Runtime.InteropServices;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Microsoft.Win32;

namespace HaCompanion_App.Services;

/// <summary>
/// Reports whether Windows apps are currently using dark mode.
/// </summary>
/// <remarks>
/// The value comes from the documented Personalization settings Windows stores
/// under <c>Themes\Personalize</c>, which is what the shell itself reads, plus
/// <c>SPI_GETHIGHCONTRAST</c> for accessibility themes. Reading them is a couple
/// of microseconds, so there is no cached snapshot and no polling: the sensor is
/// sampled at read time and <see cref="SystemEvents.UserPreferenceChanged"/>
/// drives an immediate push when the user changes colours.
/// </remarks>
public sealed class WindowsThemeSensorSource : ISensorSource
{
    public const string DarkModeId = "windows_dark_mode";

    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly object _gate = new();
    private Action? _onChanged;
    private bool _observing;
    private WindowsThemeState _last = WindowsThemeState.Default;

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            DarkModeId,
            "Dark Mode",
            "On while Windows apps are using the dark theme. High-contrast themes "
            + "are reported through the sensor's attributes.",
            SensorPrivacy.Benign,
            EnabledByDefault: true)
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(DarkModeId)) return [];

        var state = Query();
        lock (_gate) _last = state;

        return
        [
            new()
            {
                UniqueId = DarkModeId,
                Type = "binary_sensor",
                Name = "Dark Mode",
                State = WindowsThemeFormatter.IsDarkMode(state),
                EntityCategory = "diagnostic",
                Icon = WindowsThemeFormatter.IconFor(state),
                Attributes = WindowsThemeFormatter.BuildAttributes(state)
            }
        ];
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;

        lock (_gate) _last = Query();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _observing = false;
    }

    /// <summary>
    /// Windows raises this for many unrelated preferences, so the theme is
    /// compared before anything is pushed.
    /// </summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Accessibility))
        {
            return;
        }

        var state = Query();
        bool changed;

        lock (_gate)
        {
            changed = state != _last;
            _last = state;
        }

        if (changed) _onChanged?.Invoke();
    }

    private static WindowsThemeState Query()
    {
        var apps = ReadPreference("AppsUseLightTheme");
        var system = ReadPreference("SystemUsesLightTheme");

        return new WindowsThemeState(
            apps ?? true,
            // Windows only writes the system value once it diverges from the app one.
            system ?? apps ?? true,
            IsHighContrast());
    }

    private static bool? ReadPreference(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int value ? value != 0 : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or System.Security.SecurityException
                                       or IOException)
        {
            return null;
        }
    }

    private static bool IsHighContrast()
    {
        try
        {
            var info = new HIGHCONTRASTW { cbSize = (uint)Marshal.SizeOf<HIGHCONTRASTW>() };
            return SystemParametersInfoW(SPI_GETHIGHCONTRAST, info.cbSize, ref info, 0)
                   && (info.dwFlags & HCF_HIGHCONTRASTON) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private const uint SPI_GETHIGHCONTRAST = 0x0042;
    private const uint HCF_HIGHCONTRASTON = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct HIGHCONTRASTW
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(
        uint action, uint param, ref HIGHCONTRASTW value, uint winIni);
}
