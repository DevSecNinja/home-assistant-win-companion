using System.Diagnostics;
using System.Text;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion_App.Services;

public sealed class PowerShellWinGetUpdateProvider : IWinGetUpdateProvider
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(2);

    private const string ModuleName = "Microsoft.WinGet.Client";
    // Compatibility floor, not a release pin. Newer Microsoft-signed versions are accepted.
    private const string MinimumModuleVersion = "1.29.280";
    private static string PowerShellPath { get; } = Path.Combine(
        Environment.SystemDirectory,
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");

    public async Task<bool> IsModuleInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        var script = ValidationFunction
            + "try { Get-ValidatedWinGetModule | Out-Null; "
            + "[Console]::Out.Write('true') } "
            + "catch { [Console]::Out.Write('false') }";

        var result = await RunAsync(script, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);
        return result.ExitCode == 0
               && string.Equals(result.Output.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WinGetUpdateResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await IsModuleInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            return new(
                WinGetUpdateStatus.ModuleMissing,
                [],
                $"Install the official {ModuleName} PowerShell module to enable this sensor.",
                DateTimeOffset.UtcNow);
        }

        var script =
            "$ErrorActionPreference = 'Stop'; "
            + "$OutputEncoding = [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false); "
            + ValidationFunction
            + "$module = Get-ValidatedWinGetModule; "
            + "Import-Module -Name $module.Path -Force -ErrorAction Stop; "
            + "$updates = @(Get-WinGetPackage -ErrorAction Stop "
            + "| Where-Object IsUpdateAvailable "
            + "| ForEach-Object { [pscustomobject]@{ "
            + "Name = [string]$_.Name; Id = [string]$_.Id; "
            + "InstalledVersion = [string]$_.InstalledVersion; "
            + "AvailableVersion = [string]$_.AvailableVersions[0] } }); "
            + "[pscustomobject]@{ Packages = $updates } "
            + "| ConvertTo-Json -Depth 4 -Compress";

        var result = await RunAsync(script, CheckTimeout, cancellationToken)
            .ConfigureAwait(false);
        var checkedAt = DateTimeOffset.UtcNow;

        if (result.TimedOut)
            return new(WinGetUpdateStatus.Timeout, [], "The WinGet update check timed out.", checkedAt);
        if (result.ExitCode != 0)
            return new(
                WinGetUpdateStatus.Failed,
                [],
                "WinGet could not query its configured package sources.",
                checkedAt);

        return WinGetUpdateResult.Parse(result.Output.Trim(), checkedAt);
    }

    private static async Task<ProcessResult> RunAsync(
        string script,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(PowerShellPath))
            return new(-1, string.Empty, TimedOut: false);

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = PowerShellPath,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        try
        {
            if (!process.Start()) return new(-1, string.Empty, TimedOut: false);

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCancellation.Token);

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
            return new(process.ExitCode, await output.ConfigureAwait(false), TimedOut: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new(-1, string.Empty, TimedOut: false);
        }
        catch (IOException)
        {
            return new(-1, string.Empty, TimedOut: false);
        }
        catch (InvalidOperationException)
        {
            return new(-1, string.Empty, TimedOut: false);
        }
    }

    private readonly record struct ProcessResult(int ExitCode, string Output, bool TimedOut);

    private static string ValidationFunction { get; } = $$"""
        function Get-ValidatedWinGetModule {
          $module = Get-Module -ListAvailable -Name '{{ModuleName}}' |
            Where-Object { $_.Version -ge [Version]'{{MinimumModuleVersion}}' } |
            Sort-Object Version -Descending |
            Select-Object -First 1
          if ($null -eq $module) {
            throw 'A supported WinGet client module version is not installed.'
          }

          $manifestSignature = Get-AuthenticodeSignature -FilePath $module.Path
          if ($manifestSignature.Status -ne 'Valid' -or
              $manifestSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw 'The WinGet client module manifest is not signed by Microsoft.'
          }

          return $module
        }
        """;
}
