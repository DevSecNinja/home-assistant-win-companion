using System.Runtime.InteropServices;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reports whether this PC is joined to an on-premises Active Directory domain
/// or workgroup, and separately whether it is joined or registered to
/// Microsoft Entra ID (formerly Azure AD) - a PC can be either, both (hybrid)
/// or neither. Join status does not change during a session, so there is
/// nothing to observe.
/// </summary>
/// <remarks>
/// The domain/workgroup name comes from <c>NetGetJoinInformation</c> and the
/// Entra ID status from <c>NetGetAadJoinInformation</c>, the documented APIs
/// for these questions. Only the join type and the Entra tenant display name are
/// read; the join certificate, tenant id, MDM enrollment URLs and the signed-in
/// user's email are deliberately never touched, since those would leak more
/// than a sensor state should. A workgroup, domain or Entra tenant display name can
/// still reveal an organisation's internal naming, so this sensor is off by
/// default like the other network-identity sensors.
/// </remarks>
public sealed class DomainSensorSource : ISensorSource
{
    public const string DomainId = "domain";

    private readonly SensorPreferences _preferences;

    public DomainSensorSource(SensorPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            DomainId,
            "Domain",
            "The Active Directory domain/workgroup and Microsoft Entra ID join status for this PC.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Reads this PC's join status once per sync; it does not change "
                           + "while the PC is running.",
            OptInPlaceholder: "Enable to read domain join status")
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(DomainId)) return [];

        var (status, name) = QueryDomain();
        var (entraJoinType, entraTenantDisplayName) = QueryEntra();

        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["membership_type"] = DomainMembershipFormatter.DescribeType(status, entraJoinType),
            ["entra_join_type"] = DomainMembershipFormatter.DescribeEntraJoinType(entraJoinType)
        };

        return
        [
            new()
            {
                UniqueId = DomainId,
                Type = "sensor",
                Name = "Domain",
                State = DomainMembershipFormatter.DescribeState(
                    status, name, entraJoinType, entraTenantDisplayName),
                EntityCategory = "diagnostic",
                Icon = "mdi:domain",
                Attributes = attributes
            }
        ];
    }

    public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        if (!Definitions.Any(_preferences.IsEnabled))
        {
            return ValueTask.FromResult<IReadOnlyList<Sensor>>(
            [
                new() { UniqueId = DomainId, Name = "Domain", State = "Enable to read domain join status" }
            ]);
        }

        return ValueTask.FromResult(Read(requested, new SensorReadContext("Preview")));
    }

    public void Start(Action onChanged) { }

    public void Stop() { }

    private static (DomainJoinStatus Status, string? Name) QueryDomain()
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            var result = NetGetJoinInformation(null, out buffer, out var status);
            if (result != 0) return (DomainJoinStatus.Unknown, null);

            var name = buffer != IntPtr.Zero ? Marshal.PtrToStringUni(buffer) : null;
            return (ToDomainJoinStatus(status), name);
        }
        catch (DllNotFoundException)
        {
            return (DomainJoinStatus.Unknown, null);
        }
        catch (EntryPointNotFoundException)
        {
            return (DomainJoinStatus.Unknown, null);
        }
        finally
        {
            if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
        }
    }

    private static (EntraJoinType JoinType, string? TenantDisplayName) QueryEntra()
    {
        IntPtr joinInfo = IntPtr.Zero;
        try
        {
            var result = NetGetAadJoinInformation(null, out joinInfo);
            if (result != 0) return (EntraJoinType.Unknown, null);
            if (joinInfo == IntPtr.Zero) return (EntraJoinType.None, null);

            var info = Marshal.PtrToStructure<DsRegJoinInfo>(joinInfo);
            return (
                ToEntraJoinType(info.JoinType),
                Marshal.PtrToStringUni(info.TenantDisplayName));
        }
        catch (DllNotFoundException)
        {
            return (EntraJoinType.Unknown, null);
        }
        catch (EntryPointNotFoundException)
        {
            return (EntraJoinType.Unknown, null);
        }
        finally
        {
            if (joinInfo != IntPtr.Zero) NetFreeAadJoinInformation(joinInfo);
        }
    }

    private static DomainJoinStatus ToDomainJoinStatus(int status) => status switch
    {
        1 => DomainJoinStatus.Unjoined,
        2 => DomainJoinStatus.Workgroup,
        3 => DomainJoinStatus.Domain,
        _ => DomainJoinStatus.Unknown
    };

    private static EntraJoinType ToEntraJoinType(int joinType) => joinType switch
    {
        1 => EntraJoinType.Joined,
        2 => EntraJoinType.Registered,
        _ => EntraJoinType.None
    };

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(
        string? server, out IntPtr domain, out int status);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetAadJoinInformation(string? tenantId, out IntPtr joinInfo);

    [DllImport("netapi32.dll")]
    private static extern void NetFreeAadJoinInformation(IntPtr joinInfo);

    /// <summary>
    /// Mirrors the fields of Win32's <c>DSREG_JOIN_INFO</c> that this source
    /// reads. The certificate, tenant id, join user email and MDM URLs are
    /// declared only so the struct's layout matches; their values are never
    /// read or surfaced.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DsRegJoinInfo
    {
        public int JoinType;
        public IntPtr JoinCertificate;
        public IntPtr DeviceId;
        public IntPtr IdpDomain;
        public IntPtr TenantId;
        public IntPtr JoinUserEmail;
        public IntPtr TenantDisplayName;
        public IntPtr MdmEnrollmentUrl;
        public IntPtr MdmTermsOfUseUrl;
        public IntPtr MdmComplianceUrl;
        public IntPtr UserSettingSyncUrl;
        public IntPtr UserInfo;
    }
}
