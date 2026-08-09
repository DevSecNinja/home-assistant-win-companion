using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class RouteUrlPolicyTests
{
    [Fact]
    public void Empty_address_is_accepted_as_simply_not_configured()
    {
        var result = RouteUrlPolicy.Normalize("  ", RouteKind.External);

        Assert.True(result.Accepted);
        Assert.True(result.IsEmpty);
        Assert.Null(result.Url);
    }

    [Fact]
    public void External_address_over_plain_http_is_rejected()
    {
        var result = RouteUrlPolicy.Normalize("http://ha.example.com", RouteKind.External);

        Assert.False(result.Accepted);
        Assert.Equal(RouteUrlProblem.ExternalMustUseHttps, result.Problem);
        Assert.Null(result.Url);
    }

    [Fact]
    public void External_address_over_https_is_accepted_without_a_warning()
    {
        var result = RouteUrlPolicy.Normalize("ha.example.com", RouteKind.External);

        Assert.True(result.Accepted);
        Assert.Equal("https://ha.example.com/", result.Url);
        Assert.False(result.InsecureTransport);
    }

    [Fact]
    public void Internal_address_over_plain_http_is_accepted_but_flagged()
    {
        var result = RouteUrlPolicy.Normalize("http://homeassistant.local:8123", RouteKind.Internal);

        Assert.True(result.Accepted);
        Assert.Equal("http://homeassistant.local:8123/", result.Url);
        Assert.True(result.InsecureTransport);
        Assert.Contains("plain HTTP", result.Message);
    }

    [Fact]
    public void Unusable_address_is_reported_as_invalid()
    {
        var result = RouteUrlPolicy.Normalize("ftp://ha.example.com", RouteKind.Internal);

        Assert.False(result.Accepted);
        Assert.Equal(RouteUrlProblem.Invalid, result.Problem);
    }

    [Fact]
    public void Redirect_to_a_different_host_is_refused()
    {
        var result = RouteUrlPolicy.ValidateRedirect(
            "https://ha.example.com/", "https://login.hotel-wifi.example/", RouteKind.External);

        Assert.False(result.Accepted);
        Assert.Equal(RouteUrlProblem.RedirectedToDifferentHost, result.Problem);
    }

    [Fact]
    public void Redirect_from_https_down_to_http_is_refused()
    {
        var result = RouteUrlPolicy.ValidateRedirect(
            "https://ha.local:8123/", "http://ha.local:8123/", RouteKind.Internal);

        Assert.False(result.Accepted);
        Assert.Equal(RouteUrlProblem.RedirectDowngradedToHttp, result.Problem);
    }

    [Fact]
    public void Redirect_on_the_same_host_is_accepted()
    {
        var result = RouteUrlPolicy.ValidateRedirect(
            "https://ha.example.com/", "https://ha.example.com/", RouteKind.External);

        Assert.True(result.Accepted);
        Assert.Equal("https://ha.example.com/", result.Url);
        Assert.False(result.InsecureTransport);
    }

    [Fact]
    public void Internal_redirect_staying_on_http_keeps_the_insecure_flag()
    {
        var result = RouteUrlPolicy.ValidateRedirect(
            "http://ha.local:8123/", "http://ha.local:8123/", RouteKind.Internal);

        Assert.True(result.Accepted);
        Assert.True(result.InsecureTransport);
    }

    [Theory]
    [InlineData("http://192.168.1.10:8123/")]
    [InlineData("http://10.0.0.5:8123/")]
    [InlineData("http://172.16.4.1:8123/")]
    [InlineData("http://127.0.0.1:8123/")]
    [InlineData("http://homeassistant.local:8123/")]
    [InlineData("http://homeassistant:8123/")]
    [InlineData("http://ha.internal:8123/")]
    public void Private_looking_addresses_are_recognized(string url) =>
        Assert.True(RouteUrlPolicy.LooksPrivate(url));

    [Theory]
    [InlineData("https://ha.example.com/")]
    [InlineData("https://abc123.ui.nabu.casa/")]
    [InlineData("https://93.184.216.34/")]
    [InlineData("https://172.32.0.1/")]
    public void Public_addresses_are_not_treated_as_private(string url) =>
        Assert.False(RouteUrlPolicy.LooksPrivate(url));
}
