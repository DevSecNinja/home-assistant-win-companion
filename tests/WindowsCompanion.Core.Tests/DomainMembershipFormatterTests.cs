using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

/// <summary>
/// Formatting rules for the domain/workgroup and Microsoft Entra ID sensor. All
/// deterministic Core logic, so it is verified without calling
/// <c>NetGetJoinInformation</c> or <c>NetGetAadJoinInformation</c>.
/// </summary>
public class DomainMembershipFormatterTests
{
    [Fact]
    public void Domain_member_reports_the_domain_name()
    {
        Assert.Equal(
            "corp.example",
            DomainMembershipFormatter.DescribeState(
                DomainJoinStatus.Domain, "corp.example", EntraJoinType.None, null));
    }

    [Fact]
    public void Workgroup_member_reports_the_workgroup_name()
    {
        Assert.Equal(
            "WORKGROUP",
            DomainMembershipFormatter.DescribeState(
                DomainJoinStatus.Workgroup, "WORKGROUP", EntraJoinType.None, null));
    }

    [Fact]
    public void Unjoined_pc_reports_not_joined_rather_than_a_blank_name()
    {
        Assert.Equal(
            DomainMembershipFormatter.NotJoined,
            DomainMembershipFormatter.DescribeState(
                DomainJoinStatus.Unjoined, null, EntraJoinType.None, null));
    }

    [Theory]
    [InlineData(DomainJoinStatus.Domain, null)]
    [InlineData(DomainJoinStatus.Workgroup, "   ")]
    [InlineData(DomainJoinStatus.Unknown, "corp.example")]
    public void Missing_or_unusable_data_reports_unknown(DomainJoinStatus status, string? name)
    {
        Assert.Equal(
            DomainMembershipFormatter.Unknown,
            DomainMembershipFormatter.DescribeState(status, name, EntraJoinType.None, null));
    }

    [Fact]
    public void Name_is_trimmed()
    {
        Assert.Equal(
            "corp.example",
            DomainMembershipFormatter.DescribeState(
                DomainJoinStatus.Domain, "  corp.example  ", EntraJoinType.None, null));
    }

    [Fact]
    public void Entra_joined_pc_with_no_ad_domain_reports_the_tenant_display_name()
    {
        Assert.Equal(
            "Contoso Example",
            DomainMembershipFormatter.DescribeState(
                DomainJoinStatus.Workgroup, "WORKGROUP", EntraJoinType.Joined, "Contoso Example"));
    }

    [Fact]
    public void Entra_joined_pc_with_no_domain_name_falls_back_to_a_generic_label()
    {
        Assert.Equal(
            DomainMembershipFormatter.EntraJoinedFallback,
            DomainMembershipFormatter.DescribeState(
                DomainJoinStatus.Unjoined, null, EntraJoinType.Joined, "   "));
    }

    [Fact]
    public void Hybrid_joined_pc_prefers_the_on_premises_domain_name_as_state()
    {
        Assert.Equal(
            "corp.example",
            DomainMembershipFormatter.DescribeState(
                DomainJoinStatus.Domain, "corp.example", EntraJoinType.Joined, "Contoso Example"));
    }

    [Fact]
    public void Entra_registered_only_does_not_override_the_ad_state()
    {
        Assert.Equal(
            "WORKGROUP",
            DomainMembershipFormatter.DescribeState(
                DomainJoinStatus.Workgroup, "WORKGROUP", EntraJoinType.Registered, "Contoso Example"));
    }

    [Theory]
    [InlineData(DomainJoinStatus.Domain, EntraJoinType.None, DomainMembershipFormatter.TypeDomain)]
    [InlineData(DomainJoinStatus.Workgroup, EntraJoinType.None, DomainMembershipFormatter.TypeWorkgroup)]
    [InlineData(DomainJoinStatus.Unjoined, EntraJoinType.None, DomainMembershipFormatter.TypeNone)]
    [InlineData(DomainJoinStatus.Unknown, EntraJoinType.None, DomainMembershipFormatter.TypeNone)]
    [InlineData(DomainJoinStatus.Unjoined, EntraJoinType.Joined, DomainMembershipFormatter.TypeEntra)]
    [InlineData(DomainJoinStatus.Unjoined, EntraJoinType.Registered, DomainMembershipFormatter.TypeNone)]
    [InlineData(DomainJoinStatus.Domain, EntraJoinType.Joined, DomainMembershipFormatter.TypeHybrid)]
    [InlineData(DomainJoinStatus.Unjoined, EntraJoinType.Unknown, DomainMembershipFormatter.TypeUnknown)]
    [InlineData(DomainJoinStatus.Domain, EntraJoinType.Unknown, DomainMembershipFormatter.TypeDomain)]
    public void Membership_type_attribute_matches_the_join_status(
        DomainJoinStatus status, EntraJoinType entraJoinType, string expected)
    {
        Assert.Equal(expected, DomainMembershipFormatter.DescribeType(status, entraJoinType));
    }

    [Theory]
    [InlineData(EntraJoinType.None, DomainMembershipFormatter.EntraJoinTypeNone)]
    [InlineData(EntraJoinType.Registered, DomainMembershipFormatter.EntraJoinTypeRegistered)]
    [InlineData(EntraJoinType.Joined, DomainMembershipFormatter.EntraJoinTypeJoined)]
    [InlineData(EntraJoinType.Unknown, DomainMembershipFormatter.EntraJoinTypeUnknown)]
    public void Entra_join_type_attribute_matches_the_join_status(EntraJoinType entraJoinType, string expected)
    {
        Assert.Equal(expected, DomainMembershipFormatter.DescribeEntraJoinType(entraJoinType));
    }
}
