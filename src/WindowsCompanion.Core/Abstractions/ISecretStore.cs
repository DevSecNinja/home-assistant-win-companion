namespace WindowsCompanion.Core.Abstractions;

/// <summary>
/// Secure, platform-specific storage for secrets (tokens, webhook secrets).
/// Implementations MUST NOT persist values in plaintext.
/// </summary>
public interface ISecretStore
{
    void Save(string key, string value);
    string? Get(string key);
    void Delete(string key);
}
