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
            AppName = "Home Assistant Windows Companion",
            AppVersion = version,
            DeviceName = Environment.MachineName,
            Manufacturer = "PC",
            Model = RuntimeInformation.OSArchitecture.ToString() + " Windows PC",
            OsName = "Windows",
            OsVersion = Environment.OSVersion.Version.ToString(),
            SupportsEncryption = false
        };
    }
}
