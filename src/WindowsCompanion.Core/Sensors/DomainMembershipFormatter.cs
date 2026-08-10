namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Windows reports join status as one of four states from
/// <c>NetGetJoinInformation</c>: unknown, unjoined, workgroup member, or domain
/// member. This mirrors that enum so the formatting below stays independent of
/// the P/Invoke call that produces it.
/// </summary>
public enum DomainJoinStatus
{
    Unknown,
    Unjoined,
    Workgroup,
    Domain
}

/// <summary>
/// Turns the raw join status and name from <c>NetGetJoinInformation</c> into the
/// state and "membership type" attribute the domain sensor reports.
/// </summary>
/// <remarks>
/// The workgroup or domain name is not a unique hardware identifier, but it can
/// reveal an organisation's internal naming, so the sensor stays off by default
/// like the other network-identity sensors.
/// </remarks>
public static class DomainMembershipFormatter
{
    public const string Unknown = "Unknown";
    public const string NotJoined = "Not joined";

    public const string TypeDomain = "domain";
    public const string TypeWorkgroup = "workgroup";
    public const string TypeNone = "none";

    /// <summary>The sensor's state: the domain or workgroup name, or a fallback.</summary>
    public static string DescribeState(DomainJoinStatus status, string? name)
    {
        var cleaned = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        return status switch
        {
            DomainJoinStatus.Domain when cleaned is not null => cleaned,
            DomainJoinStatus.Workgroup when cleaned is not null => cleaned,
            DomainJoinStatus.Unjoined => NotJoined,
            _ => Unknown
        };
    }

    /// <summary>The "membership_type" attribute: domain, workgroup, or none.</summary>
    public static string DescribeType(DomainJoinStatus status) => status switch
    {
        DomainJoinStatus.Domain => TypeDomain,
        DomainJoinStatus.Workgroup => TypeWorkgroup,
        _ => TypeNone
    };
}
