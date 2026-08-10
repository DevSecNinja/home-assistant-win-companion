#if DEBUG
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowsCompanion_App;

internal sealed partial record TestAppLaunchOptions(
    string SettingsDirectory,
    string CredentialResourceSuffix,
    string InstanceIdentity,
    Uri ServerUrl,
    bool AutoAuthorize)
{
    internal const string ArgumentPrefix = "--test-profile=";

    internal string CredentialResource => $"WindowsCompanion.Tests.{CredentialResourceSuffix}";
    internal string MutexName => $@"Local\WindowsCompanion.Test.{InstanceIdentity}";
    internal string TrayIdentity => $"Windows Companion UI Test {InstanceIdentity}";

    internal static TestAppLaunchOptions? Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var profileArguments = arguments
            .Where(argument => argument.StartsWith(ArgumentPrefix, StringComparison.Ordinal))
            .ToArray();
        if (profileArguments.Length == 0) return null;
        if (profileArguments.Length != 1)
            throw new ArgumentException("Exactly one test profile argument is allowed.", nameof(arguments));

        LaunchProfile? profile;
        try
        {
            var encoded = profileArguments[0][ArgumentPrefix.Length..];
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(FromBase64Url(encoded)));
            profile = JsonSerializer.Deserialize<LaunchProfile>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new ArgumentException("The test profile argument is malformed.", nameof(arguments), ex);
        }

        if (profile is null)
            throw new ArgumentException("The test profile argument is empty.", nameof(arguments));
        if (!Uri.TryCreate(profile.ServerUrl, UriKind.Absolute, out var serverUrl)
            || serverUrl.Scheme != Uri.UriSchemeHttp
            || !serverUrl.IsLoopback
            || !string.IsNullOrEmpty(serverUrl.UserInfo))
        {
            throw new ArgumentException("The test server URL must be an HTTP loopback URL.", nameof(arguments));
        }

        if (string.IsNullOrWhiteSpace(profile.SettingsDirectory)
            || !Path.IsPathFullyQualified(profile.SettingsDirectory))
        {
            throw new ArgumentException("The test settings directory must be an absolute path.", nameof(arguments));
        }

        if (!InstanceIdentityPattern().IsMatch(profile.InstanceIdentity ?? string.Empty))
            throw new ArgumentException("The test instance identity is invalid.", nameof(arguments));
        if (!GuidSuffixPattern().IsMatch(profile.CredentialResourceSuffix ?? string.Empty))
            throw new ArgumentException("The test credential resource suffix is invalid.", nameof(arguments));

        var settingsDirectory = Path.GetFullPath(profile.SettingsDirectory);
        if (!string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(settingsDirectory)),
                profile.InstanceIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The test settings directory must be owned by the instance identity.",
                nameof(arguments));
        }

        return new TestAppLaunchOptions(
            settingsDirectory,
            profile.CredentialResourceSuffix!,
            profile.InstanceIdentity!,
            serverUrl,
            profile.AutoAuthorize);
    }

    private static string FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
    }

    [GeneratedRegex("^ui-[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceIdentityPattern();

    [GeneratedRegex("^[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GuidSuffixPattern();

    private sealed class LaunchProfile
    {
        public string? SettingsDirectory { get; init; }
        public string? CredentialResourceSuffix { get; init; }
        public string? InstanceIdentity { get; init; }
        public string? ServerUrl { get; init; }
        public bool AutoAuthorize { get; init; }
    }
}
#endif
