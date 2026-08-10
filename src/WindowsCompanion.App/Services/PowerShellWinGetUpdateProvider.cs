using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion_App.Services;

public sealed class PowerShellWinGetUpdateProvider : IWinGetUpdateProvider
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(2);

    private const string ModuleName = "Microsoft.WinGet.Client";
    private const string MinimumModuleVersion = "1.29.280";

    public const string InstallCommand =
        "& \"$env:SystemRoot\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" "
        + "-NoLogo -NoProfile -Command \"Install-Module -Name Microsoft.WinGet.Client "
        + "-Repository PSGallery -Scope CurrentUser -Force\"";

    private readonly IPowerShellProcessRunner _runner;
    private readonly ILogger<PowerShellWinGetUpdateProvider>? _log;

    public PowerShellWinGetUpdateProvider(
        ILogger<PowerShellWinGetUpdateProvider>? log = null)
        : this(new WindowsPowerShellProcessRunner(), log)
    {
    }

    internal PowerShellWinGetUpdateProvider(
        IPowerShellProcessRunner runner,
        ILogger<PowerShellWinGetUpdateProvider>? log = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _log = log;
    }

    public async Task<WinGetCapabilityResult> ProbeCapabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var process = await _runner
            .RunAsync(CapabilityScript, ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);

        var capability = process.HostUnavailable
            ? WinGetCapabilityResult.FromStatus(WinGetCapabilityStatus.HostUnavailable)
            : process.TimedOut
                ? WinGetCapabilityResult.FromStatus(WinGetCapabilityStatus.Timeout)
                : process.ExitCode != 0
                    ? WinGetCapabilityResult.FromStatus(WinGetCapabilityStatus.ProbeFailed)
                    : WinGetCapabilityResult.Parse(process.Output.Trim());

        if (!capability.IsReady)
        {
            _log?.LogWarning(
                "WinGet capability probe returned {Status}: {Message}",
                capability.Status,
                capability.Message);
        }

        return capability;
    }

    public async Task<WinGetUpdateResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        var process = await _runner
            .RunAsync(UpdateScript, CheckTimeout, cancellationToken)
            .ConfigureAwait(false);
        var checkedAt = DateTimeOffset.UtcNow;

        var result = process.HostUnavailable
            ? WinGetUpdateResult.Failure(WinGetUpdateStatus.HostUnavailable, checkedAt)
            : process.TimedOut
                ? WinGetUpdateResult.Failure(WinGetUpdateStatus.Timeout, checkedAt)
                : process.ExitCode != 0
                    ? WinGetUpdateResult.Failure(WinGetUpdateStatus.CommandFailed, checkedAt)
                    : WinGetUpdateResult.Parse(process.Output.Trim(), checkedAt);

        if (result.Status != WinGetUpdateStatus.Ready)
        {
            _log?.LogWarning(
                "WinGet update check returned {Status}: {Message}",
                result.Status,
                result.Error);
        }

        return result;
    }

    private static string CapabilityScript =>
        ScriptPreamble + ValidationFunction + """
        $capability = Get-WinGetCapability
        [pscustomobject]@{ Status = $capability.Status } | ConvertTo-Json -Compress
        """;

    private static string UpdateScript =>
        ScriptPreamble + ValidationFunction + """
        $capability = Get-WinGetCapability
        if ($capability.Status -ne 'Ready') {
          [pscustomobject]@{ Status = $capability.Status } | ConvertTo-Json -Compress
          return
        }

        try {
          $updates = @(Get-WinGetPackage -ErrorAction Stop |
            Where-Object IsUpdateAvailable |
            ForEach-Object {
              [pscustomobject]@{
                Name = [string]$_.Name
                Id = [string]$_.Id
                InstalledVersion = [string]$_.InstalledVersion
                AvailableVersion = [string]$_.AvailableVersions[0]
              }
            })
          [pscustomobject]@{ Status = 'Ready'; Packages = $updates } |
            ConvertTo-Json -Depth 4 -Compress
        }
        catch {
          [pscustomobject]@{ Status = 'CommandFailed' } | ConvertTo-Json -Compress
        }
        """;

    private static string ValidationFunction => $$"""
        function Get-WinGetCapability {
          try {
            $modules = @(Get-Module -ListAvailable -Name '{{ModuleName}}')
          }
          catch {
            return [pscustomobject]@{ Status = 'ProbeFailed'; Module = $null }
          }

          if ($modules.Count -eq 0) {
            return [pscustomobject]@{ Status = 'ModuleMissing'; Module = $null }
          }

          $module = $modules |
            Where-Object { $_.Version -ge [Version]'{{MinimumModuleVersion}}' } |
            Sort-Object Version -Descending |
            Select-Object -First 1
          if ($null -eq $module) {
            return [pscustomobject]@{ Status = 'ModuleIncompatible'; Module = $null }
          }

          try {
            $signature = Get-AuthenticodeSignature -FilePath $module.Path -ErrorAction Stop
            if ($signature.Status -ne 'Valid' -or
                $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
              return [pscustomobject]@{ Status = 'ModuleUntrusted'; Module = $null }
            }
          }
          catch {
            return [pscustomobject]@{ Status = 'ModuleUntrusted'; Module = $null }
          }

          try {
            Import-Module -Name $module.Path -Force -ErrorAction Stop
            Get-Command Get-WinGetPackage -Module '{{ModuleName}}' -ErrorAction Stop | Out-Null
          }
          catch {
            return [pscustomobject]@{ Status = 'ImportFailed'; Module = $null }
          }

          return [pscustomobject]@{ Status = 'Ready'; Module = $module }
        }
        """;

    private static string ScriptPreamble => """
        $ErrorActionPreference = 'Stop'
        $OutputEncoding = [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false);
        """;
}

internal interface IPowerShellProcessRunner
{
    Task<PowerShellProcessResult> RunAsync(
        string script,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal readonly record struct PowerShellProcessResult(
    int ExitCode,
    string Output,
    bool TimedOut = false,
    bool HostUnavailable = false);

internal sealed class WindowsPowerShellProcessRunner : IPowerShellProcessRunner
{
    private readonly string _powerShellPath;
    private readonly Func<string> _modulePath;

    public WindowsPowerShellProcessRunner()
        : this(
            ResolvePowerShellPath(Environment.SystemDirectory),
            BuildCurrentModulePath)
    {
    }

    internal WindowsPowerShellProcessRunner(
        string powerShellPath,
        Func<string> modulePath)
    {
        _powerShellPath = powerShellPath;
        _modulePath = modulePath;
    }

    public async Task<PowerShellProcessResult> RunAsync(
        string script,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_powerShellPath))
            return new(-1, string.Empty, HostUnavailable: true);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(_powerShellPath, script, _modulePath())
        };

        try
        {
            if (!process.Start())
                return new(-1, string.Empty, HostUnavailable: true);

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                return new(-1, string.Empty, TimedOut: true);
            }

            _ = await error.ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                   or IOException
                                   or InvalidOperationException)
        {
            return new(-1, string.Empty, HostUnavailable: true);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string powerShellPath,
        string script,
        string modulePath)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellPath,
            Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["PSModulePath"] = modulePath;
        return startInfo;
    }

    internal static string ResolvePowerShellPath(string systemDirectory) =>
        Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    internal static string BuildModulePath(
        string documentsPath,
        string programFilesPath,
        string systemDirectory,
        string? freshUserModulePath,
        string? freshMachineModulePath,
        string? inheritedModulePath)
    {
        var candidates = new[]
            {
                Path.Combine(documentsPath, "WindowsPowerShell", "Modules"),
                Path.Combine(documentsPath, "PowerShell", "Modules"),
                Path.Combine(programFilesPath, "WindowsPowerShell", "Modules"),
                Path.Combine(programFilesPath, "PowerShell", "Modules"),
                Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "Modules")
            }
            .Concat(SplitPaths(freshUserModulePath))
            .Concat(SplitPaths(freshMachineModulePath))
            .Concat(SplitPaths(inheritedModulePath));

        return string.Join(
            Path.PathSeparator,
            candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Environment.ExpandEnvironmentVariables)
                .Where(Path.IsPathFullyQualified)
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildCurrentModulePath() =>
        BuildModulePath(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.SystemDirectory,
            Environment.GetEnvironmentVariable(
                "PSModulePath",
                EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable(
                "PSModulePath",
                EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PSModulePath"));

    private static IEnumerable<string> SplitPaths(string? paths) =>
        (paths ?? string.Empty).Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
