using System.Diagnostics;
using System.Text;
using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;

namespace HaCompanion_App.Services;

public sealed class PowerShellWinGetUpdateProvider : IWinGetUpdateProvider
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    private const string ModuleName = "Microsoft.WinGet.Client";
    private const string RequiredModuleVersion = "1.29.280";
    private const string RequiredPackageSha256 =
        "726602001E6137EFFF66AA73C197C6AB6396AE2F9634D0ADAADA17EC5068EE46";
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

    public async Task<WinGetModuleInstallResult> InstallModuleAsync(
        CancellationToken cancellationToken = default)
    {
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            [Net.ServicePointManager]::SecurityProtocol =
              [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

            $moduleRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WindowsPowerShell\Modules'
            $target = Join-Path $moduleRoot '{{ModuleName}}\{{RequiredModuleVersion}}'
            $work = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
            $package = Join-Path $work '{{ModuleName}}.nupkg'
            $expanded = Join-Path $work 'expanded'

            try {
              New-Item -ItemType Directory -Path $expanded -Force | Out-Null
              Invoke-WebRequest `
                -Uri 'https://www.powershellgallery.com/api/v2/package/{{ModuleName}}/{{RequiredModuleVersion}}' `
                -OutFile $package -UseBasicParsing

              $actualHash = (Get-FileHash -Path $package -Algorithm SHA256).Hash
              if ($actualHash -ne '{{RequiredPackageSha256}}') {
                throw 'The WinGet client module package failed integrity verification.'
              }

              Add-Type -AssemblyName System.IO.Compression.FileSystem
              [IO.Compression.ZipFile]::ExtractToDirectory($package, $expanded)

              if (Test-Path $target) {
                Remove-Item $target -Recurse -Force
              }
              New-Item -ItemType Directory -Path $target -Force | Out-Null
              Get-ChildItem $expanded -Force |
                Where-Object { $_.Name -notin '_rels', 'package', '[Content_Types].xml' -and
                               $_.Extension -ne '.nuspec' } |
                Copy-Item -Destination $target -Recurse -Force
              Set-Content -Path (Join-Path $target '.package.sha256') `
                -Value '{{RequiredPackageSha256}}' -NoNewline -Encoding Ascii
            }
            finally {
              Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
            }

            {{ValidationFunction}}
            Get-ValidatedWinGetModule | Out-Null
            [Console]::Out.Write('installed')
            """;

        var result = await RunAsync(script, InstallTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (result.TimedOut)
            return new(false, "Module installation timed out. Check your PowerShell Gallery access.");
        if (result.ExitCode != 0)
            return new(false, "The WinGet client module could not be installed from PowerShell Gallery.");
        if (!await IsModuleInstalledAsync(cancellationToken).ConfigureAwait(false))
            return new(false, "PowerShell completed installation but the WinGet client module is unavailable.");

        return new(true);
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
          $module = Get-Module -ListAvailable -FullyQualifiedName @{
            ModuleName = '{{ModuleName}}'
            RequiredVersion = '{{RequiredModuleVersion}}'
          } | Select-Object -First 1
          if ($null -eq $module) {
            throw 'The required WinGet client module version is not installed.'
          }

          $moduleRoot = Split-Path -Parent $module.Path
          $provenance = Join-Path $moduleRoot '.package.sha256'
          if (-not (Test-Path $provenance) -or
              (Get-Content $provenance -Raw).Trim() -ne '{{RequiredPackageSha256}}') {
            throw 'The WinGet client module was not installed from the audited package.'
          }

          $manifestSignature = Get-AuthenticodeSignature -FilePath $module.Path
          if ($manifestSignature.Status -ne 'Valid' -or
              $manifestSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw 'The WinGet client module manifest is not signed by Microsoft.'
          }

          $invalidFiles = @(Get-ChildItem $moduleRoot -Recurse -File |
            Where-Object { $_.Extension -in '.dll', '.ps1', '.psd1', '.psm1' } |
            Where-Object { (Get-AuthenticodeSignature -FilePath $_.FullName).Status -ne 'Valid' })
          if ($invalidFiles.Count -ne 0) {
            throw 'The WinGet client module contains unsigned executable files.'
          }

          return $module
        }
        """;
}
