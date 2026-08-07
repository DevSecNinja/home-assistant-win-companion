using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;

namespace HaCompanion.Core.App;

/// <summary>
/// Loads and saves the session, keeping non-secret configuration in settings.json
/// and the webhook credentials in the platform secret store.
/// </summary>
/// <remarks>
/// This exists so the split - and the migration off plaintext - is testable without
/// Windows. Getting the migration wrong would make an existing install look
/// unregistered, register a second time and leave a duplicate device in Home
/// Assistant, which is exactly the sort of failure that only shows up on upgrade.
/// </remarks>
public sealed class SessionStore
{
    public const string WebhookIdKey = "webhook_id";
    public const string CloudhookUrlKey = "cloudhook_url";

    private readonly SettingsStore _settings;
    private readonly ISecretStore _secrets;

    public SessionStore(SettingsStore settings, ISecretStore secrets)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    /// <summary>
    /// Loads the session, transparently migrating a webhook id left in plaintext by
    /// an older version.
    /// </summary>
    public ServerConfig? Load()
    {
        var config = _settings.Load();
        if (config is null) return null;

        config.WebhookId = _secrets.Get(WebhookIdKey);
        config.CloudhookUrl = _secrets.Get(CloudhookUrlKey);

        var migrated = false;

        if (string.IsNullOrEmpty(config.WebhookId) && !string.IsNullOrEmpty(config.LegacyWebhookId))
        {
            config.WebhookId = config.LegacyWebhookId;
            migrated = true;
        }

        if (string.IsNullOrEmpty(config.CloudhookUrl) && !string.IsNullOrEmpty(config.LegacyCloudhookUrl))
        {
            config.CloudhookUrl = config.LegacyCloudhookUrl;
            migrated = true;
        }

        // Re-saving is what actually removes the plaintext copy from disk.
        if (migrated) Save(config);

        return config;
    }

    public void Save(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        StoreOrDelete(WebhookIdKey, config.WebhookId);
        StoreOrDelete(CloudhookUrlKey, config.CloudhookUrl);

        // Ensure a migrated install stops carrying the plaintext values.
        config.LegacyWebhookId = null;
        config.LegacyCloudhookUrl = null;

        _settings.Save(config);
    }

    /// <summary>Removes both the configuration and the webhook credentials.</summary>
    public void Delete()
    {
        _secrets.Delete(WebhookIdKey);
        _secrets.Delete(CloudhookUrlKey);
        _settings.Delete();
    }

    private void StoreOrDelete(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) _secrets.Delete(key);
        else _secrets.Save(key, value);
    }
}
