using WindowsCompanion.Core.Models;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public class PowerShellWinGetUpdateProviderTests
{
    [Fact]
    public async Task Missing_then_installed_recheck_runs_a_fresh_probe()
    {
        var runner = new FakeRunner(
            new(0, """{"Status":"ModuleMissing"}"""),
            new(0, """{"Status":"Ready"}"""));
        var provider = new PowerShellWinGetUpdateProvider(runner);

        var missing = await provider.ProbeCapabilityAsync();
        var installed = await provider.ProbeCapabilityAsync();

        Assert.Equal(WinGetCapabilityStatus.ModuleMissing, missing.Status);
        Assert.Equal(WinGetCapabilityStatus.Ready, installed.Status);
        Assert.Equal(2, runner.CallCount);
        Assert.All(runner.Scripts, script =>
            Assert.Contains("function Get-WinGetCapability", script));
    }

    [Fact]
    public async Task Every_update_check_invalidates_a_previous_negative_probe()
    {
        var runner = new FakeRunner(
            new(0, """{"Status":"ModuleMissing"}"""),
            new(0, """{"Status":"Ready","Packages":[]}"""));
        var provider = new PowerShellWinGetUpdateProvider(runner);

        var missing = await provider.CheckForUpdatesAsync();
        var ready = await provider.CheckForUpdatesAsync();

        Assert.Equal(WinGetUpdateStatus.ModuleMissing, missing.Status);
        Assert.Equal(WinGetUpdateStatus.Ready, ready.Status);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task Query_process_failure_is_not_reported_as_a_missing_module()
    {
        var runner = new FakeRunner(
            new PowerShellProcessResult(1, string.Empty));
        var provider = new PowerShellWinGetUpdateProvider(runner);

        var result = await provider.CheckForUpdatesAsync();

        Assert.Equal(WinGetUpdateStatus.CommandFailed, result.Status);
        Assert.Contains("package sources", result.Error);
    }

    [Fact]
    public void Module_path_prepends_both_current_user_host_scopes()
    {
        var result = WindowsPowerShellProcessRunner.BuildModulePath(
            @"C:\Users\Example\Documents",
            @"C:\Program Files",
            @"C:\Windows\System32",
            @"C:\Fresh\User",
            @"C:\Fresh\Machine",
            @"C:\Stale\Inherited");
        var paths = result.Split(Path.PathSeparator);

        Assert.Equal(
            @"C:\Users\Example\Documents\WindowsPowerShell\Modules",
            paths[0]);
        Assert.Equal(
            @"C:\Users\Example\Documents\PowerShell\Modules",
            paths[1]);
        Assert.Contains(@"C:\Fresh\User", paths);
        Assert.Contains(@"C:\Fresh\Machine", paths);
        Assert.Contains(@"C:\Stale\Inherited", paths);
    }

    [Fact]
    public void Process_uses_architecture_compatible_host_and_child_only_module_path()
    {
        var host = WindowsPowerShellProcessRunner.ResolvePowerShellPath(
            @"C:\Windows\System32");
        var startInfo = WindowsPowerShellProcessRunner.CreateStartInfo(
            host,
            "'ok'",
            @"C:\Fresh\Modules");

        Assert.Equal(
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            host);
        Assert.Equal(@"C:\Fresh\Modules", startInfo.Environment["PSModulePath"]);
        Assert.DoesNotContain("ExecutionPolicy", startInfo.Arguments);
        Assert.Contains("-NoProfile", startInfo.Arguments);
        Assert.Contains(
            "$env:SystemRoot\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
            PowerShellWinGetUpdateProvider.InstallCommand);
        Assert.Contains("-Scope CurrentUser -Force", PowerShellWinGetUpdateProvider.InstallCommand);
    }

    [Fact]
    public async Task Probe_process_failures_are_classified()
    {
        var cases = new (PowerShellProcessResult Process, WinGetCapabilityStatus Expected)[]
        {
            (new(-1, string.Empty, HostUnavailable: true),
                WinGetCapabilityStatus.HostUnavailable),
            (new(-1, string.Empty, TimedOut: true),
                WinGetCapabilityStatus.Timeout),
            (new(1, string.Empty),
                WinGetCapabilityStatus.ProbeFailed),
            (new(0, "not-json"),
                WinGetCapabilityStatus.ProbeFailed)
        };

        foreach (var (process, expected) in cases)
        {
            var provider = new PowerShellWinGetUpdateProvider(new FakeRunner(process));

            var result = await provider.ProbeCapabilityAsync();

            Assert.Equal(expected, result.Status);
        }
    }

    private sealed class FakeRunner(params PowerShellProcessResult[] results)
        : IPowerShellProcessRunner
    {
        private readonly Queue<PowerShellProcessResult> _results = new(results);

        public int CallCount { get; private set; }
        public List<string> Scripts { get; } = [];

        public Task<PowerShellProcessResult> RunAsync(
            string script,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Scripts.Add(script);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
