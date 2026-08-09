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
    private const string Resource = "WindowsCompanion";

    /// <summary>Resource name used before the product rename.</summary>
    private const string LegacyResource = "HaCompanion";

    private readonly PasswordVault _vault = new();

    public void Save(string key, string value)
    {
        Delete(key);
        _vault.Add(new PasswordCredential(Resource, key, value));
    }

    public string? Get(string key)
    {
        var current = Retrieve(Resource, key);
        if (current is not null) return current;

        // Fall back to the pre-rename resource and adopt the value, so an
        // existing refresh token and webhook id survive the upgrade instead of
        // forcing a re-registration that would duplicate the Home Assistant
        // device. The legacy entry is removed only once the new one is stored.
        var legacy = Retrieve(LegacyResource, key);
        if (legacy is null) return null;

        try
        {
            _vault.Add(new PasswordCredential(Resource, key, legacy));
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
        Remove(Resource, key);
        Remove(LegacyResource, key);
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
