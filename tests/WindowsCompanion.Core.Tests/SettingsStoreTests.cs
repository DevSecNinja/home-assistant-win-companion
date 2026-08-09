using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Save_then_load_round_trips_non_secret_config()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ha-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            var config = new ServerConfig
            {
                BaseUrl = "https://ha.local:8123",
                DeviceId = "dev-1",
                WebhookId = "wh-1"
            };

            store.Save(config);
            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Equal("https://ha.local:8123", loaded!.BaseUrl);
            Assert.Equal("dev-1", loaded.DeviceId);

            // The webhook id is a capability secret and deliberately does NOT round
            // trip through settings.json - SessionStore keeps it in the secret store.
            Assert.Null(loaded.WebhookId);
            Assert.DoesNotContain("wh-1", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Saved_file_never_contains_token_text()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ha-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new ServerConfig { BaseUrl = "https://ha.local:8123", DeviceId = "dev-1" });

            var contents = File.ReadAllText(path);
            // ServerConfig has no token field at all; assert the model can't leak one.
            Assert.DoesNotContain("token", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", contents, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_returns_null_when_file_missing()
    {
        var store = new SettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ha-missing-{Guid.NewGuid():N}.json"));
        Assert.Null(store.Load());
    }

    [Fact]
    public void ServerConfig_validation_rejects_bad_urls()
    {
        Assert.False(new ServerConfig { BaseUrl = "" }.IsValid());
        Assert.False(new ServerConfig { BaseUrl = "not-a-url" }.IsValid());
        Assert.True(new ServerConfig { BaseUrl = "https://ha.local:8123" }.IsValid());
    }
}
