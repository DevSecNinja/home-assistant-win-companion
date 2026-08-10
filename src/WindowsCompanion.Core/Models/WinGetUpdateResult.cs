using System.Text.Json;

namespace WindowsCompanion.Core.Models;

public enum WinGetUpdateStatus
{
    Ready,
    Checking,
    ModuleMissing,
    ModuleIncompatible,
    ModuleUntrusted,
    HostUnavailable,
    ImportFailed,
    ProbeFailed,
    Timeout,
    CommandFailed,
    InvalidOutput
}

public enum WinGetCapabilityStatus
{
    Ready,
    ModuleMissing,
    ModuleIncompatible,
    ModuleUntrusted,
    HostUnavailable,
    ImportFailed,
    ProbeFailed,
    Timeout
}

public sealed record WinGetCapabilityResult(
    WinGetCapabilityStatus Status,
    string Message)
{
    public bool IsReady => Status == WinGetCapabilityStatus.Ready;

    public bool CanInstallOrRepair =>
        Status is WinGetCapabilityStatus.ModuleMissing
            or WinGetCapabilityStatus.ModuleIncompatible
            or WinGetCapabilityStatus.ModuleUntrusted
            or WinGetCapabilityStatus.ImportFailed;

    public static WinGetCapabilityResult Parse(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PowerShellPayload>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return Enum.TryParse<WinGetCapabilityStatus>(
                    payload?.Status,
                    ignoreCase: true,
                    out var status)
                ? FromStatus(status)
                : FromStatus(WinGetCapabilityStatus.ProbeFailed);
        }
        catch (JsonException)
        {
            return FromStatus(WinGetCapabilityStatus.ProbeFailed);
        }
    }

    public static WinGetCapabilityResult FromStatus(WinGetCapabilityStatus status) =>
        new(status, status switch
        {
            WinGetCapabilityStatus.Ready =>
                "Microsoft.WinGet.Client is ready.",
            WinGetCapabilityStatus.ModuleMissing =>
                "Microsoft.WinGet.Client was not found in the current user's standard "
                + "PowerShell module locations.",
            WinGetCapabilityStatus.ModuleIncompatible =>
                "Microsoft.WinGet.Client is installed, but version 1.29.280 or newer is required.",
            WinGetCapabilityStatus.ModuleUntrusted =>
                "The installed Microsoft.WinGet.Client module does not have a valid "
                + "Microsoft signature. Remove that copy and reinstall it from PSGallery.",
            WinGetCapabilityStatus.HostUnavailable =>
                "Windows PowerShell 5.1 is unavailable at its architecture-compatible "
                + "Windows location. Repair Windows PowerShell before enabling this sensor.",
            WinGetCapabilityStatus.ImportFailed =>
                "Microsoft.WinGet.Client was found but Windows PowerShell could not import it. "
                + "Reinstall the module from PSGallery and recheck.",
            WinGetCapabilityStatus.Timeout =>
                "The Windows PowerShell capability check timed out. Recheck after any other "
                + "PowerShell operation has finished.",
            _ =>
                "Windows PowerShell could not inspect Microsoft.WinGet.Client. Check PowerShell "
                + "policy and security logs, then recheck."
        });

    private sealed class PowerShellPayload
    {
        public string? Status { get; set; }
    }
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
            if (!string.IsNullOrWhiteSpace(payload?.Status)
                && (!Enum.TryParse<WinGetUpdateStatus>(
                        payload.Status,
                        ignoreCase: true,
                        out var status)
                    || status != WinGetUpdateStatus.Ready))
            {
                return Enum.TryParse<WinGetUpdateStatus>(
                    payload?.Status,
                    ignoreCase: true,
                    out status)
                    ? Failure(status, checkedAt)
                    : Invalid("WinGet returned an unknown update status.", checkedAt);
            }

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

    public static WinGetUpdateResult Failure(
        WinGetUpdateStatus status,
        DateTimeOffset checkedAt) =>
        new(status, [], status switch
        {
            WinGetUpdateStatus.ModuleMissing =>
                "Microsoft.WinGet.Client is not installed for this Windows account.",
            WinGetUpdateStatus.ModuleIncompatible =>
                "Microsoft.WinGet.Client version 1.29.280 or newer is required.",
            WinGetUpdateStatus.ModuleUntrusted =>
                "Microsoft.WinGet.Client does not have a valid Microsoft signature.",
            WinGetUpdateStatus.HostUnavailable =>
                "Windows PowerShell 5.1 is unavailable.",
            WinGetUpdateStatus.ImportFailed =>
                "Windows PowerShell could not import Microsoft.WinGet.Client.",
            WinGetUpdateStatus.ProbeFailed =>
                "Windows PowerShell could not inspect Microsoft.WinGet.Client.",
            WinGetUpdateStatus.Timeout =>
                "The WinGet update check timed out.",
            WinGetUpdateStatus.CommandFailed =>
                "WinGet could not query its configured package sources.",
            _ =>
                "WinGet update status is unavailable."
        }, checkedAt);

    private static WinGetUpdateResult Invalid(string error, DateTimeOffset checkedAt) =>
        new(WinGetUpdateStatus.InvalidOutput, [], error, checkedAt);

    private sealed class PowerShellPayload
    {
        public string? Status { get; set; }
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
