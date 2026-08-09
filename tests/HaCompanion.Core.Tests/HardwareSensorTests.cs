using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

/// <summary>
/// Selection, formatting and change-detection rules for the hardware, display,
/// environment and storage sensors. All of it is deterministic Core logic, so it
/// is verified without a monitor, a disk or a registry.
/// </summary>
public class HardwareSensorTests
{
    [Theory]
    [InlineData("Dell Inc.", "Precision 5560", "Dell Inc. Precision 5560")]
    [InlineData("LENOVO", "20XW00DRMH", "LENOVO 20XW00DRMH")]
    [InlineData("  Dell Inc.  ", "  Precision   5560 ", "Dell Inc. Precision 5560")]
    public void Model_combines_manufacturer_and_product(
        string manufacturer, string model, string expected)
    {
        Assert.Equal(expected, HostModelFormatter.Describe(manufacturer, model));
    }

    [Fact]
    public void Model_does_not_repeat_a_manufacturer_the_oem_already_included()
    {
        Assert.Equal("HP EliteBook 840 G8", HostModelFormatter.Describe("HP", "HP EliteBook 840 G8"));
    }

    [Theory]
    [InlineData("System manufacturer", "System Product Name")]
    [InlineData("To Be Filled By O.E.M.", "To be filled by O.E.M.")]
    [InlineData("Default string", "Default string")]
    [InlineData(null, "")]
    [InlineData("  ", null)]
    public void Placeholder_smbios_values_report_unknown_rather_than_noise(
        string? manufacturer, string? model)
    {
        Assert.Equal(HostModelFormatter.Unknown, HostModelFormatter.Describe(manufacturer, model));
    }

    [Fact]
    public void Model_reports_whichever_half_the_firmware_filled_in()
    {
        Assert.Equal("Precision 5560", HostModelFormatter.Describe("Default string", "Precision 5560"));
        Assert.Equal("Dell Inc.", HostModelFormatter.Describe("Dell Inc.", "System Product Name"));
    }

    [Fact]
    public void Model_is_bounded_so_a_verbose_firmware_cannot_flood_the_state()
    {
        var text = HostModelFormatter.Describe(new string('A', 200), new string('B', 200));

        Assert.Equal(HostModelFormatter.MaxLength, text.Length);
    }

    [Fact]
    public void Displays_are_ordered_with_the_primary_first()
    {
        var external = Display(2560, 1440, primary: false);
        var laptop = Display(1920, 1200, primary: true, connection: DisplayConnection.Internal);

        var ordered = DisplaySummary.Order([external, laptop]);

        Assert.Same(laptop, ordered[0]);
        Assert.Equal("1920x1200 + 2560x1440", DisplaySummary.Describe([external, laptop]));
    }

    [Fact]
    public void Displays_reporting_no_pixels_are_ignored()
    {
        var ghost = Display(0, 0, primary: false);
        var real = Display(3840, 2160, primary: true);

        Assert.Equal(1, DisplaySummary.Count([ghost, real]));
        Assert.Equal("3840x2160", DisplaySummary.Describe([ghost, real]));
    }

    [Fact]
    public void No_displays_reports_a_state_rather_than_an_empty_string()
    {
        Assert.Equal(DisplaySummary.NoDisplays, DisplaySummary.Describe([]));
        Assert.Equal(0, DisplaySummary.Count([]));
        Assert.Equal("mdi:monitor-off", DisplaySummary.IconFor(0));
    }

    [Fact]
    public void A_dock_with_many_outputs_stays_within_the_state_limit()
    {
        var displays = Enumerable.Range(0, 9)
            .Select(index => Display(3840 - index, 2160, primary: index == 0))
            .ToArray();

        var text = DisplaySummary.Describe(displays);

        Assert.EndsWith("+ 5 more", text);
        Assert.True(text.Length < 255);
    }

    [Fact]
    public void Display_detail_reports_mode_and_whether_the_panel_is_built_in()
    {
        var laptop = new DisplayInfo(3840, 2400, 60, 250, DisplayConnection.Internal, IsPrimary: true);
        var monitor = new DisplayInfo(2560, 1440, 0, 100, DisplayConnection.External, IsPrimary: false);
        var unknown = new DisplayInfo(1920, 1080, 60, 0, DisplayConnection.Unknown, IsPrimary: false);

        Assert.Equal("3840x2400 @ 60 Hz, 250%, built-in", DisplaySummary.DescribeDetail(laptop));
        Assert.Equal("2560x1440, 100%, external", DisplaySummary.DescribeDetail(monitor));
        Assert.Equal("1920x1080 @ 60 Hz", DisplaySummary.DescribeDetail(unknown));
    }

    [Fact]
    public void Display_attributes_stay_bounded_and_summarise_the_topology()
    {
        var displays = new[]
        {
            new DisplayInfo(1920, 1200, 60, 150, DisplayConnection.Internal, IsPrimary: true),
            new DisplayInfo(2560, 1440, 60, 100, DisplayConnection.External, IsPrimary: false)
        };

        var attributes = DisplaySummary.BuildAttributes(displays);

        Assert.Equal(2, attributes["count"]);
        Assert.Equal(1, attributes["built_in"]);
        Assert.Equal(1, attributes["external"]);
        Assert.Equal("1920x1200", attributes["primary_resolution"]);
        Assert.Equal(150, attributes["primary_scale"]);
        Assert.Equal(2, Assert.IsType<string[]>(attributes["displays"]).Length);
    }

    [Fact]
    public void Display_attributes_never_list_more_than_the_bound()
    {
        var displays = Enumerable.Range(0, 20)
            .Select(index => Display(1920, 1080 + index, primary: index == 0))
            .ToArray();

        var attributes = DisplaySummary.BuildAttributes(displays);

        Assert.Equal(DisplaySummary.MaxDetailed, Assert.IsType<string[]>(attributes["displays"]).Length);
        Assert.Equal(20, attributes["count"]);
    }

    [Fact]
    public void Disk_usage_rounds_to_values_worth_reporting()
    {
        var usage = new DiskUsage(1_000_000_000_000, 250_000_000_000);

        Assert.Equal(1000d, DiskUsageFormatter.TotalGigabytes(usage));
        Assert.Equal(250d, DiskUsageFormatter.FreeGigabytes(usage));
        Assert.Equal(750d, DiskUsageFormatter.UsedGigabytes(usage));
        Assert.Equal(75d, DiskUsageFormatter.UsedPercent(usage));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 10)]
    [InlineData(100, 200)]
    public void An_unreadable_volume_reports_nothing_rather_than_nonsense(long total, long free)
    {
        var usage = new DiskUsage(total, free);

        Assert.False(usage.IsAvailable);
        Assert.Null(DiskUsageFormatter.UsedPercent(usage));
        Assert.Null(DiskUsageFormatter.FreeGigabytes(usage));
        Assert.Equal(0, usage.UsedBytes);
    }

    [Fact]
    public void Small_disk_movement_does_not_write_home_assistant_history()
    {
        var previous = new DiskUsage(1_000_000_000_000, 250_000_000_000);
        var drift = new DiskUsage(1_000_000_000_000, 249_800_000_000);

        Assert.False(DiskUsageFormatter.HasMeaningfullyChanged(previous, drift));
    }

    [Fact]
    public void A_real_change_in_free_space_is_published()
    {
        var previous = new DiskUsage(1_000_000_000_000, 250_000_000_000);
        var afterDownload = new DiskUsage(1_000_000_000_000, 244_000_000_000);

        Assert.True(DiskUsageFormatter.HasMeaningfullyChanged(previous, afterDownload));
    }

    [Fact]
    public void Appearing_and_disappearing_volumes_always_count_as_a_change()
    {
        var usage = new DiskUsage(500_000_000_000, 100_000_000_000);

        Assert.True(DiskUsageFormatter.HasMeaningfullyChanged(DiskUsage.Unavailable, usage));
        Assert.True(DiskUsageFormatter.HasMeaningfullyChanged(usage, DiskUsage.Unavailable));
        Assert.False(DiskUsageFormatter.HasMeaningfullyChanged(
            DiskUsage.Unavailable, DiskUsage.Unavailable));
    }

    [Theory]
    [InlineData(95d, "mdi:gauge-full")]
    [InlineData(70d, "mdi:gauge")]
    [InlineData(12d, "mdi:gauge-low")]
    [InlineData(null, "mdi:harddisk")]
    public void Disk_icons_reflect_how_full_the_drive_is(double? percent, string expected)
    {
        Assert.Equal(expected, DiskUsageFormatter.IconFor(percent));
    }

    [Theory]
    [InlineData("nl-NL", "nl-NL")]
    [InlineData("en", "en")]
    [InlineData("sr-Latn-RS", "sr-Latn-RS")]
    [InlineData("nl_NL", "nl-NL")]
    [InlineData("  de-DE  ", "de-DE")]
    public void Locale_names_are_reported_as_bcp_47(string value, string expected)
    {
        Assert.Equal(expected, LocaleFormatter.Describe(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("nl-NL; DROP")]
    public void Missing_or_implausible_locales_report_unknown(string? value)
    {
        Assert.Equal(LocaleFormatter.Unknown, LocaleFormatter.Describe(value));
    }

    [Fact]
    public void Time_zone_prefers_the_iana_name_home_assistant_uses()
    {
        Assert.Equal(
            "Europe/Amsterdam",
            LocaleFormatter.DescribeTimeZone("Europe/Amsterdam", "W. Europe Standard Time"));
    }

    [Fact]
    public void Time_zone_falls_back_to_the_windows_id_when_there_is_no_iana_name()
    {
        Assert.Equal(
            "W. Europe Standard Time",
            LocaleFormatter.DescribeTimeZone(null, "W. Europe Standard Time"));
        Assert.Equal(LocaleFormatter.Unknown, LocaleFormatter.DescribeTimeZone(null, null));
    }

    [Fact]
    public void Dark_mode_follows_the_app_theme_preference()
    {
        var dark = new WindowsThemeState(AppsUseLightTheme: false, SystemUsesLightTheme: false, HighContrast: false);
        var light = WindowsThemeState.Default;

        Assert.True(WindowsThemeFormatter.IsDarkMode(dark));
        Assert.False(WindowsThemeFormatter.IsDarkMode(light));
        Assert.Equal("mdi:weather-night", WindowsThemeFormatter.IconFor(dark));
        Assert.Equal("mdi:weather-sunny", WindowsThemeFormatter.IconFor(light));
    }

    [Fact]
    public void App_and_system_themes_are_reported_separately()
    {
        // Windows allows dark apps with a light taskbar and vice versa.
        var mixed = new WindowsThemeState(AppsUseLightTheme: false, SystemUsesLightTheme: true, HighContrast: false);

        var attributes = WindowsThemeFormatter.BuildAttributes(mixed);

        Assert.Equal("Dark", attributes["theme"]);
        Assert.Equal("Light", attributes["system_theme"]);
        Assert.Equal(false, attributes["high_contrast"]);
    }

    [Fact]
    public void High_contrast_is_labelled_rather_than_squeezed_into_dark_or_light()
    {
        var highContrast = new WindowsThemeState(AppsUseLightTheme: false, SystemUsesLightTheme: false, HighContrast: true);

        var attributes = WindowsThemeFormatter.BuildAttributes(highContrast);

        Assert.Equal(WindowsThemeFormatter.HighContrast, attributes["theme"]);
        Assert.Equal(WindowsThemeFormatter.HighContrast, attributes["system_theme"]);
        Assert.Equal(true, attributes["high_contrast"]);
        Assert.Equal("mdi:contrast-circle", WindowsThemeFormatter.IconFor(highContrast));
    }

    [Theory]
    [InlineData(NotificationState.PresentationMode, true)]
    [InlineData(NotificationState.RunningDirect3DFullScreen, true)]
    [InlineData(NotificationState.Busy, true)]
    [InlineData(NotificationState.QuietTime, true)]
    [InlineData(NotificationState.NotPresent, true)]
    [InlineData(NotificationState.AcceptsNotifications, false)]
    [InlineData(NotificationState.App, false)]
    [InlineData(NotificationState.Unknown, false)]
    public void Notification_suppression_is_derived_from_the_shell_state(
        NotificationState state, bool expected)
    {
        Assert.Equal(expected, NotificationStateFormatter.SuppressesNotifications(state));
    }

    [Fact]
    public void Notification_state_says_plainly_that_it_excludes_do_not_disturb()
    {
        // Windows 11 Do Not Disturb is not exposed by SHQueryUserNotificationState
        // and has no supported alternative, so the sensor must not imply otherwise.
        var attributes = NotificationStateFormatter.BuildAttributes(
            NotificationState.AcceptsNotifications);

        Assert.Equal(false, attributes["includes_do_not_disturb"]);
        Assert.Equal(false, attributes["suppresses_notifications"]);
    }

    private static DisplayInfo Display(
        int width,
        int height,
        bool primary,
        DisplayConnection connection = DisplayConnection.External) =>
        new(width, height, 60, 100, connection, primary);
}
