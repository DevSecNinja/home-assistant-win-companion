using System.Runtime.InteropServices;

namespace WindowsCompanion_App.Services;

internal static class WindowsNetworkInterfaceIdentity
{
    private const int MaxInterfaceStringLength = 256;
    private const int MaxPhysicalAddressLength = 32;

    public static byte[]? PermanentPhysicalAddressOf(string interfaceId)
    {
        if (!Guid.TryParse(interfaceId, out var interfaceGuid)
            || ConvertInterfaceGuidToLuid(in interfaceGuid, out var interfaceLuid) != 0)
        {
            return null;
        }

        var row = new MibIfRow2
        {
            InterfaceLuid = interfaceLuid,
            Alias = string.Empty,
            Description = string.Empty,
            PhysicalAddress = new byte[MaxPhysicalAddressLength],
            PermanentPhysicalAddress = new byte[MaxPhysicalAddressLength]
        };

        if (GetIfEntry2(ref row) != 0) return null;

        var length = Math.Min((int)row.PhysicalAddressLength, MaxPhysicalAddressLength);
        return length == 0 ? null : row.PermanentPhysicalAddress[..length];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MibIfRow2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxInterfaceStringLength + 1)]
        public string Alias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxInterfaceStringLength + 1)]
        public string Description;

        public uint PhysicalAddressLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxPhysicalAddressLength)]
        public byte[] PhysicalAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxPhysicalAddressLength)]
        public byte[] PermanentPhysicalAddress;

        public uint Mtu;
        public uint Type;
        public int TunnelType;
        public int MediaType;
        public int PhysicalMediumType;
        public int AccessType;
        public int DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public int OperStatus;
        public int AdminStatus;
        public int MediaConnectState;
        public Guid NetworkGuid;
        public int ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint ConvertInterfaceGuidToLuid(
        in Guid interfaceGuid,
        out ulong interfaceLuid);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIfEntry2(ref MibIfRow2 row);
}
