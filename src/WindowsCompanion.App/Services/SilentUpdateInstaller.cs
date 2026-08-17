using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>
/// The result of a silent update install, written by the detached relaunch
/// helper and read once at the next startup. Contains no Home Assistant URLs,
/// tokens or other secrets - only the outcome of running the installer.
/// </summary>
internal sealed record LastInstallResult(
    bool Success,
    string Version,
    int ExitCode,
    DateTimeOffset CompletedAt);

/// <summary>
/// Extracts a verified setup package and hands off to a detached PowerShell
/// helper that waits for this process to exit, runs the installer silently,
/// and relaunches the app. The installer's own <c>PrepareToInstall</c> step
/// closes our running process via Restart Manager, so this process cannot run
/// the installer synchronously and expect to relaunch itself afterwards.
/// </summary>
internal sealed class SilentUpdateInstaller : IUpdatePackageInstaller
{
    internal const string ResultFileName = "last-install.json";

    public Task InstallAsync(
        string packagePath,
        SemanticVersion version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        var updatesDirectory = Path.Combine(AppDataPaths.Resolve(), "Updates");
        var extractDirectory = Path.Combine(updatesDirectory, version.ToString(), "extracted");
        if (Directory.Exists(extractDirectory)) Directory.Delete(extractDirectory, recursive: true);
        Directory.CreateDirectory(extractDirectory);

        ZipFile.ExtractToDirectory(packagePath, extractDirectory);

        var setupExePath = Directory
            .EnumerateFiles(extractDirectory, "*-setup.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (setupExePath is null)
        {
            throw new FileNotFoundException(
                "The downloaded update package does not contain a *-setup.exe.");
        }

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The running executable's path could not be determined.");
        var resultPath = Path.Combine(updatesDirectory, ResultFileName);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"wc-update-{Guid.NewGuid():N}.ps1");
        var script = BuildRelaunchScript(
            Environment.ProcessId,
            setupExePath,
            exePath,
            resultPath,
            version.ToString());

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        using var helper = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        if (helper is null)
        {
            throw new InvalidOperationException(
                "The detached relaunch helper (powershell.exe) could not be started.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads and deletes the result of the previous silent install, if any, so
    /// the caller can show a one-time success or failure banner at startup.
    /// </summary>
    internal static LastInstallResult? TakeLastInstallResult(string? updatesRootOverride = null)
    {
        var updatesDirectory = updatesRootOverride
            ?? Path.Combine(AppDataPaths.Resolve(), "Updates");
        var path = Path.Combine(updatesDirectory, ResultFileName);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LastInstallResult>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Builds the PowerShell 5.1-compatible relaunch script. Kept as a pure
    /// string-building function so its contents can be asserted in tests
    /// without actually running PowerShell.
    /// </summary>
    internal static string BuildRelaunchScript(
        int waitForProcessId,
        string setupExePath,
        string exePathToRelaunch,
        string resultPath,
        string version)
    {
        static string Escape(string value) => value.Replace("'", "''");

        return $$"""
            $ErrorActionPreference = 'Stop'
            $targetPid = {{waitForProcessId}}
            $setup = '{{Escape(setupExePath)}}'
            $exe = '{{Escape(exePathToRelaunch)}}'
            $resultPath = '{{Escape(resultPath)}}'
            $version = '{{Escape(version)}}'

            while (Get-Process -Id $targetPid -ErrorAction SilentlyContinue) {
                Start-Sleep -Milliseconds 250
            }

            $exitCode = -1
            $success = $false
            try {
                $process = Start-Process -FilePath $setup -ArgumentList '/VERYSILENT', '/SP-', '/NORESTART' -PassThru -Wait
                $exitCode = $process.ExitCode
                $success = ($exitCode -eq 0)
            } catch {
                $exitCode = -1
                $success = $false
            }

            $result = @{
                success = $success
                version = $version
                exitCode = $exitCode
                completedAt = (Get-Date).ToUniversalTime().ToString('o')
            }
            New-Item -Path (Split-Path -Parent $resultPath) -ItemType Directory -Force | Out-Null
            $result | ConvertTo-Json | Set-Content -Path $resultPath -Encoding utf8

            if ($success) {
                Start-Process -FilePath $exe
            }

            Remove-Item -Path $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
            """;
    }
}
