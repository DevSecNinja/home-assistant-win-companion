using System.Text.Json;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ha-session-{Guid.NewGuid():N}.json");

    private sealed class FakeSecrets : ISecretStore
    {
        public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
        public void Save(string key, string value) => Values[key] = value;
        public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Delete(string key) => Values.Remove(key);
    }

    private (SessionStore Store, FakeSecrets Secrets, SettingsStore Settings) Create()
    {
        var settings = new SettingsStore(_path);
        var secrets = new FakeSecrets();
        return (new SessionStore(settings, secrets), secrets, settings);
    }

    [Fact]
    public void Webhook_credentials_go_to_the_secret_store_not_the_file()
    {
        var (store, secrets, _) = Create();

        store.Save(new ServerConfig
        {
            BaseUrl = "https://ha.local:8123/",
            DeviceId = "dev-1",
            WebhookId = "super-secret-webhook",
            CloudhookUrl = "https://hooks.nabu.casa/super-secret-webhook"
        });

        var json = File.ReadAllText(_path);

        // Anyone who can read the webhook id can post sensor data and receive this
        // user's notifications, so it must never reach disk in the clear.
        Assert.DoesNotContain("super-secret-webhook", json);
        Assert.Equal("super-secret-webhook", secrets.Values[SessionStore.WebhookIdKey]);
        Assert.Equal("https://hooks.nabu.casa/super-secret-webhook", secrets.Values[SessionStore.CloudhookUrlKey]);
    }

    [Fact]
    public void Round_trip_restores_the_webhook_id()
    {
        var (store, secrets, _) = Create();
        store.Save(new ServerConfig { BaseUrl = "https://ha.local:8123/", DeviceId = "dev-1", WebhookId = "wh-1" });

        var reloaded = new SessionStore(new SettingsStore(_path), secrets).Load();

        Assert.NotNull(reloaded);
        Assert.Equal("wh-1", reloaded!.WebhookId);
        Assert.True(reloaded.Registered);
    }

    [Fact]
    public void Legacy_plaintext_webhook_id_is_migrated_and_erased()
    {
        // A settings.json written by a version that stored the webhook id in the clear.
        File.WriteAllText(_path, """
            {
              "BaseUrl": "https://ha.local:8123/",
              "DeviceId": "dev-1",
              "WebhookId": "legacy-webhook",
              "CloudhookUrl": "https://hooks.nabu.casa/legacy-webhook",
              "RemoteUiUrl": null
            }
            """);

        var (store, secrets, _) = Create();
        var config = store.Load();

        // Without migration the install would look unregistered, register again and
        // leave a duplicate device in Home Assistant.
        Assert.NotNull(config);
        Assert.Equal("legacy-webhook", config!.WebhookId);
        Assert.True(config.Registered);

        Assert.Equal("legacy-webhook", secrets.Values[SessionStore.WebhookIdKey]);
        Assert.Equal("https://hooks.nabu.casa/legacy-webhook", secrets.Values[SessionStore.CloudhookUrlKey]);

        // Loading must also remove the plaintext copy from disk.
        Assert.DoesNotContain("legacy-webhook", File.ReadAllText(_path));
    }

    [Fact]
    public void The_secret_store_wins_over_a_stale_plaintext_value()
    {
        File.WriteAllText(_path, """
            { "BaseUrl": "https://ha.local:8123/", "DeviceId": "dev-1", "WebhookId": "stale" }
            """);

        var (store, secrets, _) = Create();
        secrets.Values[SessionStore.WebhookIdKey] = "current";

        Assert.Equal("current", store.Load()!.WebhookId);
    }

    [Fact]
    public void Delete_removes_the_file_and_the_secrets()
    {
        var (store, secrets, _) = Create();
        store.Save(new ServerConfig { BaseUrl = "https://ha.local:8123/", DeviceId = "d", WebhookId = "wh" });

        store.Delete();

        Assert.False(File.Exists(_path));
        Assert.Empty(secrets.Values);
    }

    [Fact]
    public void Sensor_preferences_still_persist_in_the_file()
    {
        var (store, _, _) = Create();
        var config = new ServerConfig { BaseUrl = "https://ha.local:8123/", DeviceId = "d", WebhookId = "wh" };
        config.Sensors.Set("ip_address", true);
        config.Sensors.IdleThresholdSeconds = 900;

        store.Save(config);
        var reloaded = store.Load();

        Assert.True(reloaded!.Sensors.Enabled["ip_address"]);
        Assert.Equal(900, reloaded.Sensors.IdleThresholdSeconds);
    }

    [Fact]
    public void Legacy_property_is_not_written_back_out()
    {
        var (store, _, _) = Create();
        store.Save(new ServerConfig { BaseUrl = "https://ha.local:8123/", DeviceId = "d", WebhookId = "wh" });

        using var document = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.False(document.RootElement.TryGetProperty("WebhookId", out _));
        Assert.False(document.RootElement.TryGetProperty("CloudhookUrl", out _));
    }

    [Fact]
    public void An_install_from_the_single_url_era_stays_in_default_mode()
    {
        File.WriteAllText(_path, """
            { "BaseUrl": "https://ha.example.com/", "DeviceId": "dev-1" }
            """);

        var (store, _, _) = Create();
        var config = store.Load();

        Assert.NotNull(config);
        Assert.False(config!.RouteAssignmentPending);
        Assert.False(config.UseSeparateUrls);
        Assert.Equal("https://ha.example.com/", config.BaseUrl);
        Assert.Null(config.InternalUrl);
        Assert.Null(config.ExternalUrl);
        Assert.Equal(ConnectionMode.Automatic, config.ConnectionMode);

        Assert.DoesNotContain("\"UseSeparateUrls\":true", File.ReadAllText(_path));
    }

    [Fact]
    public void An_install_that_already_has_routes_is_loaded_unchanged()
    {
        var (store, _, _) = Create();
        var config = new ServerConfig
        {
            BaseUrl = "http://ha.local:8123/",
            DeviceId = "dev-1",
            WebhookId = "wh",
            InternalUrl = "http://ha.local:8123/",
            ExternalUrl = "https://ha.example.com/",
            UseSeparateUrls = true,
            ConnectionMode = ConnectionMode.PreferInternal,
            InstanceDeviceId = "hass-dev-9"
        };
        config.TrustedNetworks.Ssids.Add("HomeNet");
        store.Save(config);

        var reloaded = store.Load()!;

        Assert.False(reloaded.RouteAssignmentPending);
        Assert.True(reloaded.UseSeparateUrls);
        Assert.Equal(ConnectionMode.PreferInternal, reloaded.ConnectionMode);
        Assert.Equal("hass-dev-9", reloaded.InstanceDeviceId);
        Assert.Equal(["HomeNet"], reloaded.TrustedNetworks.Ssids);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
