using WindowsCompanion.Core.App;
using Microsoft.Win32;

namespace WindowsCompanion_App.Services;

public enum StartupRegistrationState
{
    Disabled,
    Enabled,
    NeedsRepair
}

public interface IStartupRegistration
{
    bool IsSupported { get; }
    StartupRegistrationState GetState();
    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowsCompanion";

    /// <summary>Value name used before the product rename.</summary>
    private const string LegacyValueName = "HaCompanion";

    public bool IsSupported => true;

    public string ExpectedCommand
    {
        get
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the application path.");
            return StartupCommand.Build(executable);
        }
    }

    public StartupRegistrationState GetState()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var value = key?.GetValue(ValueName) as string;

        // A pre-rename entry still launches the app, so treat it as enabled and
        // let SetEnabled rewrite it under the new name. Reporting "disabled"
        // here would silently drop the user's existing preference.
        value ??= key?.GetValue(LegacyValueName) as string;
        if (string.IsNullOrEmpty(value)) return StartupRegistrationState.Disabled;

        return string.Equals(value, ExpectedCommand, StringComparison.OrdinalIgnoreCase)
            ? StartupRegistrationState.Enabled
            : StartupRegistrationState.NeedsRepair;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");

        // The legacy value always goes, whether enabling or disabling: leaving it
        // behind would start the old executable path a second time.
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

        if (enabled)
            key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

internal sealed class DisabledStartupRegistration : IStartupRegistration
{
    public bool IsSupported => false;
    public StartupRegistrationState GetState() => StartupRegistrationState.Disabled;
    public void SetEnabled(bool enabled)
    {
    }
}
