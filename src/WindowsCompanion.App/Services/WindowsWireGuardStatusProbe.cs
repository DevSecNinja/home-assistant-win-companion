using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

internal interface IWireGuardStatusProbe
{
    WireGuardStatus Read();
}

internal readonly record struct WireGuardServiceInfo(string Name, bool Running);
internal readonly record struct WireGuardAdapterInfo(string Name, string Description, bool Operational);
internal readonly record struct WireGuardServicePage(
    IReadOnlyList<WireGuardServiceInfo> Services,
    uint NextResumeHandle,
    bool HasMore);

internal sealed class WindowsWireGuardStatusProbe : IWireGuardStatusProbe
{
    private const string ManagerServiceName = "WireGuardManager";
    private const string TunnelServicePrefix = "WireGuardTunnel$";
    private const string AdapterDescription = "WireGuard Tunnel";

    private readonly Func<IReadOnlyList<WireGuardServiceInfo>> _readServices;
    private readonly Func<IReadOnlyList<WireGuardAdapterInfo>> _readAdapters;

    public WindowsWireGuardStatusProbe()
        : this(ReadServices, ReadAdapters)
    {
    }

    internal WindowsWireGuardStatusProbe(
        Func<IReadOnlyList<WireGuardServiceInfo>> readServices,
        Func<IReadOnlyList<WireGuardAdapterInfo>> readAdapters)
    {
        _readServices = readServices ?? throw new ArgumentNullException(nameof(readServices));
        _readAdapters = readAdapters ?? throw new ArgumentNullException(nameof(readAdapters));
    }

    public WireGuardStatus Read()
    {
        try
        {
            return Classify(_readServices(), _readAdapters());
        }
        catch (Exception ex) when (ex is Win32Exception
                                       or NetworkInformationException
                                       or UnauthorizedAccessException
                                       or SecurityException
                                       or IOException)
        {
            return WireGuardStatus.Unavailable;
        }
    }

    internal static WireGuardStatus Classify(
        IReadOnlyList<WireGuardServiceInfo> services,
        IReadOnlyList<WireGuardAdapterInfo> adapters)
    {
        var clientDetected = services.Any(service =>
                                 service.Name.Equals(
                                     ManagerServiceName,
                                     StringComparison.OrdinalIgnoreCase)
                                 || service.Name.StartsWith(
                                     TunnelServicePrefix,
                                     StringComparison.OrdinalIgnoreCase))
                             || adapters.Any(adapter =>
                                 adapter.Description.Equals(
                                     AdapterDescription,
                                     StringComparison.Ordinal));

        var runningTunnels = services
            .Where(service => service.Running
                              && service.Name.StartsWith(
                                  TunnelServicePrefix,
                                  StringComparison.OrdinalIgnoreCase))
            .Select(service => service.Name[TunnelServicePrefix.Length..])
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var operationalAdapters = adapters
            .Where(adapter => adapter.Operational
                              && adapter.Description.Equals(
                                  AdapterDescription,
                                  StringComparison.Ordinal))
            .Select(adapter => adapter.Name)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return WireGuardStatusClassifier.Classify(
            inspectionSucceeded: true,
            clientDetected,
            runningTunnels,
            operationalAdapters);
    }

    private static IReadOnlyList<WireGuardAdapterInfo> ReadAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Select(adapter => new WireGuardAdapterInfo(
                adapter.Name,
                adapter.Description,
                adapter.OperationalStatus == OperationalStatus.Up))
            .ToArray();

    private static IReadOnlyList<WireGuardServiceInfo> ReadServices()
    {
        using var manager = OpenSCManager(null, null, ScManagerEnumerateService);
        if (manager.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return CollectServicePages(resumeHandle => ReadServicePage(manager, resumeHandle));
    }

    internal static IReadOnlyList<WireGuardServiceInfo> CollectServicePages(
        Func<uint, WireGuardServicePage> readPage)
    {
        ArgumentNullException.ThrowIfNull(readPage);

        var services = new List<WireGuardServiceInfo>();
        var resumeHandle = 0u;
        while (true)
        {
            var page = readPage(resumeHandle);
            services.AddRange(page.Services);
            if (!page.HasMore)
                return services;
            if (page.NextResumeHandle == resumeHandle)
                throw new Win32Exception(ErrorMoreData, "Service enumeration did not advance.");
            resumeHandle = page.NextResumeHandle;
        }
    }

    private static WireGuardServicePage ReadServicePage(
        ServiceManagerHandle manager,
        uint resumeHandle)
    {
        var bufferSize = 0u;
        while (true)
        {
            nint buffer = 0;
            try
            {
                if (bufferSize > 0)
                    buffer = Marshal.AllocHGlobal(checked((int)bufferSize));

                var nextResumeHandle = resumeHandle;
                var success = EnumServicesStatusEx(
                    manager,
                    ScEnumProcessInfo,
                    ServiceWin32,
                    ServiceStateAll,
                    buffer,
                    bufferSize,
                    out var bytesNeeded,
                    out var servicesReturned,
                    ref nextResumeHandle,
                    null);

                var page = ReadServiceBuffer(buffer, servicesReturned);
                if (success)
                    return new(page, nextResumeHandle, HasMore: false);

                var error = Marshal.GetLastWin32Error();
                if (error != ErrorMoreData)
                    throw new Win32Exception(error);
                if (servicesReturned > 0)
                    return new(page, nextResumeHandle, HasMore: true);
                if (bytesNeeded > bufferSize)
                {
                    bufferSize = Math.Min(bytesNeeded, MaximumServiceBufferSize);
                    continue;
                }

                throw new Win32Exception(error, "Service enumeration could not make progress.");
            }
            finally
            {
                if (buffer != 0)
                    Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static IReadOnlyList<WireGuardServiceInfo> ReadServiceBuffer(
        nint buffer,
        uint servicesReturned)
    {
        if (servicesReturned == 0)
            return [];

        var result = new WireGuardServiceInfo[servicesReturned];
        var itemSize = Marshal.SizeOf<EnumServiceStatusProcess>();
        for (var index = 0; index < servicesReturned; index++)
        {
            var address = nint.Add(buffer, checked((int)index * itemSize));
            var item = Marshal.PtrToStructure<EnumServiceStatusProcess>(address);
            result[index] = new(
                Marshal.PtrToStringUni(item.ServiceName) ?? string.Empty,
                item.Status.CurrentState == ServiceRunning);
        }

        return result;
    }

    private const uint ScManagerEnumerateService = 0x0004;
    private const int ScEnumProcessInfo = 0;
    private const uint ServiceWin32 = 0x00000030;
    private const uint ServiceStateAll = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const int ErrorMoreData = 234;
    private const uint MaximumServiceBufferSize = 256 * 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public nint ServiceName;
        public nint DisplayName;
        public ServiceStatusProcess Status;
    }

    private sealed class ServiceManagerHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private ServiceManagerHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceManagerHandle OpenSCManager(
        string? machineName,
        string? databaseName,
        uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(
        ServiceManagerHandle serviceManager,
        int infoLevel,
        uint serviceType,
        uint serviceState,
        nint services,
        uint bufferSize,
        out uint bytesNeeded,
        out uint servicesReturned,
        ref uint resumeHandle,
        string? groupName);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(nint serviceHandle);
}
