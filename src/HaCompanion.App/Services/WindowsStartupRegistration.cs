using HaCompanion.Core.App;
using Microsoft.Win32;

namespace HaCompanion_App.Services;

public enum StartupRegistrationState
{
    Disabled,
    Enabled,
    NeedsRepair
}

public sealed class WindowsStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HaCompanion";

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
        if (string.IsNullOrEmpty(value)) return StartupRegistrationState.Disabled;

        return string.Equals(value, ExpectedCommand, StringComparison.OrdinalIgnoreCase)
            ? StartupRegistrationState.Enabled
            : StartupRegistrationState.NeedsRepair;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");

        if (enabled)
            key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
