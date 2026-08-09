using WindowsCompanion.Core.Sensors;
using Microsoft.Win32;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reads the machine's manufacturer and model from the SMBIOS values Windows
/// publishes in the registry.
/// </summary>
/// <remarks>
/// Only <c>SystemManufacturer</c> and <c>SystemProductName</c> are read. The
/// neighbouring serial number, SKU, UUID and BIOS identifiers are deliberately
/// never touched: they are unique hardware identifiers, and this app reports a
/// model, not an inventory. No WMI query and no external process is involved.
/// </remarks>
internal static class HardwareInfo
{
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";

    /// <summary>
    /// The recognisable model name, e.g. "Dell Precision 5560", or
    /// <see cref="HostModelFormatter.Unknown"/> when the OEM left the SMBIOS
    /// fields at their template values.
    /// </summary>
    public static string DescribeModel()
    {
        var (manufacturer, model) = Read();
        return HostModelFormatter.Describe(manufacturer, model);
    }

    /// <summary>The cleaned manufacturer, for Home Assistant device registration.</summary>
    public static string? Manufacturer()
    {
        var (manufacturer, _) = Read();
        var described = HostModelFormatter.Describe(manufacturer, null);
        return described == HostModelFormatter.Unknown ? null : described;
    }

    /// <summary>The cleaned product name, for Home Assistant device registration.</summary>
    public static string? Model()
    {
        var (_, model) = Read();
        var described = HostModelFormatter.Describe(null, model);
        return described == HostModelFormatter.Unknown ? null : described;
    }

    private static (string? Manufacturer, string? Model) Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BiosKey);
            return (
                key?.GetValue("SystemManufacturer") as string,
                key?.GetValue("SystemProductName") as string);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or System.Security.SecurityException
                                       or IOException)
        {
            // A locked-down or virtualised machine simply has no model to report.
            return (null, null);
        }
    }
}
