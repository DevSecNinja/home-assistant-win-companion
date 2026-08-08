namespace HaCompanion.Core.App;

public static class ServerUrlNormalizer
{
    public static string Normalize(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Please enter your Home Assistant URL.", nameof(baseUrl));

        baseUrl = baseUrl.Trim();
        if (baseUrl.Contains("://", StringComparison.Ordinal)
            && !baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Home Assistant URL must use HTTP or HTTPS.", nameof(baseUrl));
        }

        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://" + baseUrl;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Home Assistant URL must be an absolute HTTP or HTTPS URL.",
                nameof(baseUrl));
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/";
    }
}
