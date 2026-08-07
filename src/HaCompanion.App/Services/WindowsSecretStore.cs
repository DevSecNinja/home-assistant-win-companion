using HaCompanion.Core.Abstractions;
using Windows.Security.Credentials;

namespace HaCompanion_App.Services;

/// <summary>
/// <see cref="ISecretStore"/> backed by the Windows Credential Locker
/// (<see cref="PasswordVault"/>). Values are encrypted per-user by the OS and
/// never written to disk in plaintext (Constitution II).
/// </summary>
public sealed class WindowsSecretStore : ISecretStore
{
    private const string Resource = "HaCompanion";
    private readonly PasswordVault _vault = new();

    public void Save(string key, string value)
    {
        Delete(key);
        _vault.Add(new PasswordCredential(Resource, key, value));
    }

    public string? Get(string key)
    {
        try
        {
            var credential = _vault.Retrieve(Resource, key);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            // Not found or inaccessible.
            return null;
        }
    }

    public void Delete(string key)
    {
        try
        {
            var credential = _vault.Retrieve(Resource, key);
            _vault.Remove(credential);
        }
        catch
        {
            // Nothing to remove.
        }
    }
}
