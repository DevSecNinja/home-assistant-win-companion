using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

/// <summary>
/// Formatting rules for the domain/workgroup sensor. All deterministic Core
/// logic, so it is verified without calling <c>NetGetJoinInformation</c>.
/// </summary>
public class DomainMembershipFormatterTests
{
    [Fact]
    public void Domain_member_reports_the_domain_name()
    {
        Assert.Equal(
            "contoso.com", DomainMembershipFormatter.DescribeState(DomainJoinStatus.Domain, "contoso.com"));
    }

    [Fact]
    public void Workgroup_member_reports_the_workgroup_name()
    {
        Assert.Equal(
            "WORKGROUP", DomainMembershipFormatter.DescribeState(DomainJoinStatus.Workgroup, "WORKGROUP"));
    }

    [Fact]
    public void Unjoined_pc_reports_not_joined_rather_than_a_blank_name()
    {
        Assert.Equal(
            DomainMembershipFormatter.NotJoined,
            DomainMembershipFormatter.DescribeState(DomainJoinStatus.Unjoined, null));
    }

    [Theory]
    [InlineData(DomainJoinStatus.Domain, null)]
    [InlineData(DomainJoinStatus.Workgroup, "   ")]
    [InlineData(DomainJoinStatus.Unknown, "contoso.com")]
    public void Missing_or_unusable_data_reports_unknown(DomainJoinStatus status, string? name)
    {
        Assert.Equal(DomainMembershipFormatter.Unknown, DomainMembershipFormatter.DescribeState(status, name));
    }

    [Fact]
    public void Name_is_trimmed()
    {
        Assert.Equal(
            "contoso.com", DomainMembershipFormatter.DescribeState(DomainJoinStatus.Domain, "  contoso.com  "));
    }

    [Theory]
    [InlineData(DomainJoinStatus.Domain, DomainMembershipFormatter.TypeDomain)]
    [InlineData(DomainJoinStatus.Workgroup, DomainMembershipFormatter.TypeWorkgroup)]
    [InlineData(DomainJoinStatus.Unjoined, DomainMembershipFormatter.TypeNone)]
    [InlineData(DomainJoinStatus.Unknown, DomainMembershipFormatter.TypeNone)]
    public void Membership_type_attribute_matches_the_join_status(DomainJoinStatus status, string expected)
    {
        Assert.Equal(expected, DomainMembershipFormatter.DescribeType(status));
    }
}
