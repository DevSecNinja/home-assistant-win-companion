using WindowsCompanion.Core.Abstractions;
using Windows.Security.Credentials;

namespace WindowsCompanion_App.Services;

/// <summary>
/// <see cref="ISecretStore"/> backed by the Windows Credential Locker
/// (<see cref="PasswordVault"/>). Values are encrypted per-user by the OS and
/// never written to disk in plaintext (Constitution II).
/// </summary>
public sealed class WindowsSecretStore : ISecretStore
{
    public const string DefaultResource = "WindowsCompanion";

    /// <summary>Resource name used before the product rename.</summary>
    private const string LegacyResource = "HaCompanion";

    private readonly PasswordVault _vault = new();
    private readonly string _resource;
    private readonly bool _migrateLegacy;

    public WindowsSecretStore(string resource = DefaultResource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        _resource = resource;
        _migrateLegacy = string.Equals(resource, DefaultResource, StringComparison.Ordinal);
    }

    /// <summary>The Credential Locker resource owned by this store.</summary>
    public string Resource => _resource;

    public void Save(string key, string value)
    {
        Delete(key);
        _vault.Add(new PasswordCredential(_resource, key, value));
    }

    public string? Get(string key)
    {
        var current = Retrieve(_resource, key);
        if (current is not null) return current;
        if (!_migrateLegacy) return null;

        // Fall back to the pre-rename resource and adopt the value, so an
        // existing refresh token and webhook id survive the upgrade instead of
        // forcing a re-registration that would duplicate the Home Assistant
        // device. The legacy entry is removed only once the new one is stored.
        var legacy = Retrieve(LegacyResource, key);
        if (legacy is null) return null;

        try
        {
            _vault.Add(new PasswordCredential(_resource, key, legacy));
            Remove(LegacyResource, key);
        }
        catch (Exception)
        {
            // Migration is best effort: the caller still gets the value, and the
            // next read retries. Never fail a credential read over bookkeeping.
        }

        return legacy;
    }

    public void Delete(string key)
    {
        Remove(_resource, key);
        if (_migrateLegacy) Remove(LegacyResource, key);
    }

    /// <summary>Removes every credential under this store's exact resource.</summary>
    public void Clear()
    {
        IReadOnlyList<PasswordCredential> credentials;
        try
        {
            credentials = _vault.FindAllByResource(_resource);
        }
        catch
        {
            return;
        }

        foreach (var credential in credentials)
        {
            try
            {
                _vault.Remove(credential);
            }
            catch
            {
                // Cleanup is idempotent and best effort.
            }
        }
    }

    private string? Retrieve(string resource, string key)
    {
        try
        {
            var credential = _vault.Retrieve(resource, key);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            // Not found or inaccessible.
            return null;
        }
    }

    private void Remove(string resource, string key)
    {
        try
        {
            _vault.Remove(_vault.Retrieve(resource, key));
        }
        catch
        {
            // Nothing to remove.
        }
    }
}
