using System.Runtime.InteropServices;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reports whether this PC is joined to an Active Directory domain or a
/// workgroup, and its name. Domain membership does not change during a
/// session, so there is nothing to observe.
/// </summary>
/// <remarks>
/// The name comes from <c>NetGetJoinInformation</c>, the documented API for
/// this question; nothing about the domain controller, forest, or the
/// machine's AD object is read. A workgroup or domain name can reveal an
/// organisation's internal naming, so this sensor is off by default like the
/// other network-identity sensors.
/// </remarks>
public sealed class DomainSensorSource : ISensorSource
{
    public const string DomainId = "domain";

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            DomainId,
            "Domain",
            "The Active Directory domain or workgroup this PC is joined to.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Reads this PC's join status once per sync; it does not change "
                           + "while the PC is running.")
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(DomainId)) return [];

        var (status, name) = Query();

        return
        [
            new()
            {
                UniqueId = DomainId,
                Type = "sensor",
                Name = "Domain",
                State = DomainMembershipFormatter.DescribeState(status, name),
                EntityCategory = "diagnostic",
                Icon = "mdi:domain",
                Attributes = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["membership_type"] = DomainMembershipFormatter.DescribeType(status)
                }
            }
        ];
    }

    public void Start(Action onChanged) { }

    public void Stop() { }

    private static (DomainJoinStatus Status, string? Name) Query()
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

    private static DomainJoinStatus ToDomainJoinStatus(int status) => status switch
    {
        1 => DomainJoinStatus.Unjoined,
        2 => DomainJoinStatus.Workgroup,
        3 => DomainJoinStatus.Domain,
        _ => DomainJoinStatus.Unknown
    };

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(
        string? server, out IntPtr domain, out int status);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
