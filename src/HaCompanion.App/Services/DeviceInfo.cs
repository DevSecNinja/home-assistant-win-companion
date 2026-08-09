using System.Runtime.InteropServices;
using HaCompanion.Core.Models;

namespace HaCompanion_App.Services;

/// <summary>Collects device metadata used for Home Assistant registration.</summary>
public static class DeviceInfo
{
    public static DeviceRegistrationRequest BuildRegistration(string deviceId)
    {
        var version = typeof(DeviceInfo).Assembly.GetName().Version?.ToString() ?? "0.1.0";
        return new DeviceRegistrationRequest
        {
            DeviceId = deviceId,
            AppId = "io.homeassistant.windows",
            AppName = "Windows Companion for Home Assistant",
            AppVersion = version,
            DeviceName = Environment.MachineName,
            // The real SMBIOS manufacturer/model make the Home Assistant device card
            // recognisable. No serial, SKU or UUID is read; see HardwareInfo.
            Manufacturer = HardwareInfo.Manufacturer() ?? "PC",
            Model = HardwareInfo.Model() ?? RuntimeInformation.OSArchitecture + " Windows PC",
            OsName = "Windows",
            OsVersion = Environment.OSVersion.Version.ToString(),
            SupportsEncryption = false
        };
    }
}
