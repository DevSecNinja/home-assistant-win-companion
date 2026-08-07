namespace HaCompanion.Core.Security;

/// <summary>
/// Helpers to keep secrets out of logs and diagnostic output (Constitution II).
/// </summary>
public static class Redactor
{
    /// <summary>Masks a secret, keeping only a short suffix for correlation.</summary>
    public static string Redact(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return "<none>";
        if (secret.Length <= 4) return "****";
        return string.Concat("****", secret.AsSpan(secret.Length - 4));
    }
}
