using System.Text.Json;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public class SilentUpdateInstallerTests
{
    [Fact]
    public void The_relaunch_script_waits_for_this_process_then_installs_silently_and_relaunches()
    {
        var script = SilentUpdateInstaller.BuildRelaunchScript(
            waitForProcessId: 4242,
            setupExePath: @"C:\Temp\extracted\setup.exe",
            exePathToRelaunch: @"C:\Users\me\AppData\Local\WindowsCompanion\WindowsCompanion.exe",
            resultPath: @"C:\Users\me\AppData\Local\WindowsCompanion\Updates\last-install.json",
            version: "1.2.3");

        Assert.Contains("4242", script);
        Assert.Contains("setup.exe", script);
        Assert.Contains("/VERYSILENT", script);
        Assert.Contains("/SP-", script);
        Assert.Contains("/NORESTART", script);
        Assert.Contains("Get-Process -Id $targetPid", script);
        Assert.Contains("last-install.json", script);
        Assert.Contains("WindowsCompanion.exe", script);
        Assert.Contains("1.2.3", script);
        // The helper must relaunch only when the install succeeded, never
        // unconditionally - otherwise a failed install would still restart the
        // (unpatched) app and mask the failure.
        Assert.Contains("if ($success)", script);
    }

    [Fact]
    public void Single_quotes_in_paths_are_escaped_so_the_script_cannot_be_broken_out_of()
    {
        var script = SilentUpdateInstaller.BuildRelaunchScript(
            waitForProcessId: 1,
            setupExePath: "C:\\o'brien\\setup.exe",
            exePathToRelaunch: "C:\\o'brien\\WindowsCompanion.exe",
            resultPath: "C:\\o'brien\\last-install.json",
            version: "1.0.0");

        Assert.Contains("o''brien", script);
    }

    [Fact]
    public void No_previous_install_result_returns_null()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wc-install-{Guid.NewGuid():N}");
        Assert.Null(SilentUpdateInstaller.TakeLastInstallResult(root));
    }

    [Fact]
    public void A_previous_install_result_is_read_once_and_then_deleted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wc-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, SilentUpdateInstaller.ResultFileName);
        try
        {
            File.WriteAllText(
                path,
                """{"success":true,"version":"1.2.3","exitCode":0,"completedAt":"2026-01-01T00:00:00Z"}""");

            var result = SilentUpdateInstaller.TakeLastInstallResult(root);

            Assert.NotNull(result);
            Assert.True(result!.Success);
            Assert.Equal("1.2.3", result.Version);
            Assert.Equal(0, result.ExitCode);
            Assert.False(File.Exists(path));
            Assert.Null(SilentUpdateInstaller.TakeLastInstallResult(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Malformed_result_json_is_ignored_rather_than_thrown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wc-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, SilentUpdateInstaller.ResultFileName);
        try
        {
            File.WriteAllText(path, "not json");

            Assert.Null(SilentUpdateInstaller.TakeLastInstallResult(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
