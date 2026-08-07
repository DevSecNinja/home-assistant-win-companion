using System.Runtime.InteropServices;
using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;

namespace HaCompanion_App.Services;

/// <summary>
/// Reads battery/power status from the Win32 <c>GetSystemPowerStatus</c> API.
/// </summary>
public sealed class WindowsSystemStatusProvider : ISystemStatusProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private const byte NoSystemBattery = 128;
    private const byte ChargingFlag = 8;
    private const byte UnknownStatus = 255;

    public SystemStatus GetStatus()
    {
        if (!GetSystemPowerStatus(out var s))
            return new SystemStatus(false, 100, PowerState.Unknown);

        var hasBattery = (s.BatteryFlag & NoSystemBattery) == 0 && s.BatteryFlag != UnknownStatus;
        var percent = s.BatteryLifePercent == UnknownStatus ? (hasBattery ? 0 : 100) : s.BatteryLifePercent;

        PowerState state;
        if (!hasBattery)
        {
            state = PowerState.PluggedIn;
        }
        else if ((s.BatteryFlag & ChargingFlag) != 0)
        {
            state = PowerState.Charging;
        }
        else if (s.AcLineStatus == 1)
        {
            state = percent >= 100 ? PowerState.Full : PowerState.NotCharging;
        }
        else
        {
            state = PowerState.Discharging;
        }

        return new SystemStatus(hasBattery, percent, state);
    }
}
