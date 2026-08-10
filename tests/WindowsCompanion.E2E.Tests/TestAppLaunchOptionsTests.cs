using System.Text;
using System.Text.Json;
using WindowsCompanion_App;

namespace WindowsCompanion.E2E.Tests;

public sealed class TestAppLaunchOptionsTests
{
#if DEBUG
    [Fact]
    public void Valid_loopback_profile_is_accepted()
    {
        var identity = $"ui-{Guid.NewGuid():N}";
        var settingsDirectory = Path.Combine(
            Path.GetFullPath("ui-test-state"),
            identity);

        var options = TestAppLaunchOptions.Parse(
            [Encode(settingsDirectory, Guid.NewGuid().ToString("N"), identity, "http://127.0.0.1:8123/")]);

        Assert.NotNull(options);
        Assert.Equal(settingsDirectory, options.SettingsDirectory);
        Assert.True(options.ServerUrl.IsLoopback);
        Assert.True(options.AutoAuthorize);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://192.0.2.10:8123/")]
    [InlineData("file:///C:/test/")]
    public void Non_loopback_or_non_http_servers_are_rejected(string serverUrl)
    {
        var identity = $"ui-{Guid.NewGuid():N}";
        var argument = Encode(
            Path.Combine(Path.GetFullPath("ui-test-state"), identity),
            Guid.NewGuid().ToString("N"),
            identity,
            serverUrl);

        Assert.Throws<ArgumentException>(() => TestAppLaunchOptions.Parse([argument]));
    }

    [Fact]
    public void Relative_or_non_owned_profile_directories_are_rejected()
    {
        var identity = $"ui-{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");

        Assert.Throws<ArgumentException>(() => TestAppLaunchOptions.Parse(
            [Encode(Path.Combine("relative", identity), suffix, identity, "http://localhost:8123/")]));
        Assert.Throws<ArgumentException>(() => TestAppLaunchOptions.Parse(
            [Encode(Path.Combine(Path.GetFullPath("ui-test-state"), "other"), suffix, identity,
                "http://localhost:8123/")]));
    }

    [Theory]
    [InlineData("--test-profile=not-base64")]
    [InlineData("--test-profile=e30")]
    public void Malformed_profiles_are_rejected(string argument) =>
        Assert.Throws<ArgumentException>(() => TestAppLaunchOptions.Parse([argument]));

    [Fact]
    public void Duplicate_profiles_are_rejected()
    {
        var identity = $"ui-{Guid.NewGuid():N}";
        var argument = Encode(
            Path.Combine(Path.GetFullPath("ui-test-state"), identity),
            Guid.NewGuid().ToString("N"),
            identity,
            "http://localhost:8123/");

        Assert.Throws<ArgumentException>(() => TestAppLaunchOptions.Parse([argument, argument]));
    }

    private static string Encode(
        string settingsDirectory,
        string credentialResourceSuffix,
        string instanceIdentity,
        string serverUrl)
    {
        var json = JsonSerializer.Serialize(new
        {
            settingsDirectory,
            credentialResourceSuffix,
            instanceIdentity,
            serverUrl,
            autoAuthorize = true
        });
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return TestAppLaunchOptions.ArgumentPrefix + encoded;
    }
#else
    [Fact]
    public void Release_assembly_excludes_test_profile_contract()
    {
        Assert.Null(typeof(App).Assembly.GetType("WindowsCompanion_App.TestAppLaunchOptions"));
        Assert.Null(typeof(App).Assembly.GetType("WindowsCompanion_App.TestAppComposition"));
    }
#endif
}
