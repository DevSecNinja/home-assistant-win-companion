namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Windows reports classic on-premises join status as one of four states from
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
/// Microsoft Entra ID (formerly Azure AD) join status from
/// <c>NetGetAadJoinInformation</c>, separate from the on-premises status above
/// because a PC can be Entra-joined, Entra-registered, both (hybrid) or
/// neither, independently of its classic domain/workgroup membership.
/// </summary>
public enum EntraJoinType
{
    /// <summary>Not joined or registered to any Microsoft Entra ID tenant.</summary>
    None,

    /// <summary>A work or school account has been added (Entra-registered).</summary>
    Registered,

    /// <summary>The device itself is joined to a Microsoft Entra ID tenant.</summary>
    Joined,

    /// <summary>The Entra ID join query failed, so the actual status is unavailable.</summary>
    Unknown
}

/// <summary>
/// Turns the raw join status from <c>NetGetJoinInformation</c> and
/// <c>NetGetAadJoinInformation</c> into the state and attributes the domain
/// sensor reports.
/// </summary>
/// <remarks>
/// The workgroup, domain or Microsoft Entra tenant display name is not a unique
/// hardware identifier, but it can reveal an organisation's internal naming, so
/// the sensor stays off by default like the other network-identity sensors.
/// </remarks>
public static class DomainMembershipFormatter
{
    public const string Unknown = "Unknown";
    public const string NotJoined = "Not joined";
    public const string EntraJoinedFallback = "Microsoft Entra ID";

    public const string TypeDomain = "domain";
    public const string TypeWorkgroup = "workgroup";
    public const string TypeEntra = "entra";
    public const string TypeHybrid = "hybrid";
    public const string TypeNone = "none";
    public const string TypeUnknown = "unknown";

    public const string EntraJoinTypeNone = "none";
    public const string EntraJoinTypeRegistered = "registered";
    public const string EntraJoinTypeJoined = "joined";
    public const string EntraJoinTypeUnknown = "unknown";

    /// <summary>
    /// The sensor's state: an on-premises domain name takes priority (a hybrid
    /// join still names the AD domain a user would recognise), otherwise a
    /// Microsoft Entra ID join using its human-readable tenant display name,
    /// otherwise the workgroup name, otherwise a
    /// fallback.
    /// </summary>
    public static string DescribeState(
        DomainJoinStatus status,
        string? name,
        EntraJoinType entraJoinType,
        string? entraTenantDisplayName)
    {
        var cleanedName = Clean(name);
        var cleanedEntraTenantDisplayName = Clean(entraTenantDisplayName);

        if (status == DomainJoinStatus.Domain && cleanedName is not null) return cleanedName;
        if (entraJoinType == EntraJoinType.Joined)
            return cleanedEntraTenantDisplayName ?? EntraJoinedFallback;
        if (status == DomainJoinStatus.Workgroup && cleanedName is not null) return cleanedName;
        if (status == DomainJoinStatus.Unjoined) return NotJoined;

        return Unknown;
    }

    /// <summary>
    /// The "membership_type" attribute: domain, entra, hybrid (both), workgroup,
    /// none, or unknown if the Entra ID query failed and there is no confirmed
    /// on-premises domain/workgroup membership to fall back on.
    /// </summary>
    public static string DescribeType(DomainJoinStatus status, EntraJoinType entraJoinType)
    {
        var domainJoined = status == DomainJoinStatus.Domain;
        var entraJoined = entraJoinType == EntraJoinType.Joined;

        if (domainJoined && entraJoined) return TypeHybrid;
        if (domainJoined) return TypeDomain;
        if (entraJoined) return TypeEntra;
        if (status == DomainJoinStatus.Workgroup) return TypeWorkgroup;
        if (status == DomainJoinStatus.Unjoined && entraJoinType == EntraJoinType.Unknown) return TypeUnknown;

        return TypeNone;
    }

    /// <summary>The "entra_join_type" attribute: none, registered, joined, or unknown.</summary>
    public static string DescribeEntraJoinType(EntraJoinType entraJoinType) => entraJoinType switch
    {
        EntraJoinType.Joined => EntraJoinTypeJoined,
        EntraJoinType.Registered => EntraJoinTypeRegistered,
        EntraJoinType.Unknown => EntraJoinTypeUnknown,
        _ => EntraJoinTypeNone
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
