using System.Text.Json;

namespace HaCompanion.Core.Models;

public enum WinGetUpdateStatus
{
    Ready,
    Checking,
    ModuleMissing,
    Timeout,
    Failed,
    InvalidOutput
}

public sealed record WinGetPackageUpdate(
    string Name,
    string Id,
    string InstalledVersion,
    string AvailableVersion);

public sealed record WinGetUpdateResult(
    WinGetUpdateStatus Status,
    IReadOnlyList<WinGetPackageUpdate> Packages,
    string? Error = null,
    DateTimeOffset? CheckedAt = null)
{
    public static WinGetUpdateResult Checking { get; } =
        new(WinGetUpdateStatus.Checking, []);

    public static WinGetUpdateResult Parse(string json, DateTimeOffset checkedAt)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PowerShellPayload>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload?.Packages is null)
                return Invalid("WinGet returned incomplete update information.", checkedAt);

            if (payload.Packages.Any(item =>
                    string.IsNullOrWhiteSpace(item.Name)
                    || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.InstalledVersion)
                    || string.IsNullOrWhiteSpace(item.AvailableVersion)))
            {
                return Invalid("WinGet returned incomplete package information.", checkedAt);
            }

            var packages = payload.Packages
                .Select(item => new WinGetPackageUpdate(
                    item.Name!,
                    item.Id!,
                    item.InstalledVersion!,
                    item.AvailableVersion!))
                .ToList();

            return new WinGetUpdateResult(
                WinGetUpdateStatus.Ready,
                packages,
                CheckedAt: checkedAt);
        }
        catch (JsonException)
        {
            return Invalid("WinGet returned unreadable update information.", checkedAt);
        }
    }

    private static WinGetUpdateResult Invalid(string error, DateTimeOffset checkedAt) =>
        new(WinGetUpdateStatus.InvalidOutput, [], error, checkedAt);

    private sealed class PowerShellPayload
    {
        public List<PowerShellPackage>? Packages { get; set; }
    }

    private sealed class PowerShellPackage
    {
        public string? Name { get; set; }
        public string? Id { get; set; }
        public string? InstalledVersion { get; set; }
        public string? AvailableVersion { get; set; }
    }
}

public sealed record WinGetModuleInstallResult(bool Success, string? Error = null);
