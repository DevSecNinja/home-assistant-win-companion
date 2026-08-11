using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Windows.Security.Credentials;
using WindowsCompanion.Testing;

namespace WindowsCompanion.UI.Tests.Fixtures;

internal sealed class UiScenarioFixture : IAsyncDisposable
{
    private readonly string _applicationPath;
    private readonly string _evidenceRoot;
    private readonly bool _suppressTrayLeftClick;
    private Application? _application;
    private int _automationDisposed;
    private int _disposed;

    private UiScenarioFixture(
        string applicationPath,
        FakeHaScenario scenario,
        string profileDirectory,
        string credentialResource,
        string instanceIdentity,
        string evidenceRoot,
        bool suppressTrayLeftClick)
    {
        _applicationPath = applicationPath;
        Scenario = scenario;
        ProfileDirectory = profileDirectory;
        CredentialResource = credentialResource;
        InstanceIdentity = instanceIdentity;
        _evidenceRoot = evidenceRoot;
        _suppressTrayLeftClick = suppressTrayLeftClick;
        Automation = new UIA3Automation();
        FailureEvidence = CreateFailureEvidence(window: null);
    }

    internal FakeHaScenario Scenario { get; }
    internal string ProfileDirectory { get; }
    internal string CredentialResource { get; }
    internal string InstanceIdentity { get; }
    internal string TrayIdentity => $"Windows Companion UI Test {InstanceIdentity}";
    internal UIA3Automation Automation { get; }
    internal Window Window { get; private set; } = null!;
    internal UiFailureEvidence FailureEvidence { get; private set; }

    internal static async Task<UiScenarioFixture> StartAsync(
        string scenarioId,
        Action<FakeHaScenario>? configure = null,
        bool suppressTrayLeftClick = false)
    {
        var scenario = await FakeHaScenario.StartAsync(scenarioId);
        UiScenarioFixture? fixture = null;
        var evidenceRoot = Path.Combine(AppContext.BaseDirectory, "ui-evidence");
        string? profile = null;
        string? credentialResource = null;
        string? identity = null;
        try
        {
            var root = FindRepositoryRoot();
            evidenceRoot = Path.Combine(root, "TestResults", "ui-evidence");
            identity = $"ui-{Guid.NewGuid():N}";
            var suffix = Guid.NewGuid().ToString("N");
            profile = Path.Combine(root, "TestResults", "ui-profiles", identity);
            credentialResource = $"WindowsCompanion.Tests.{suffix}";
            configure?.Invoke(scenario);
            fixture = new UiScenarioFixture(
                ResolveApplicationPath(root),
                scenario,
                profile,
                credentialResource,
                identity,
                evidenceRoot,
                suppressTrayLeftClick);
            Directory.CreateDirectory(profile);
            await fixture.LaunchAsync(suffix);
            return fixture;
        }
        catch (Exception exception)
        {
            if (fixture is not null)
            {
                await fixture.CaptureEvidenceBestEffortAsync(
                    "application startup",
                    exception);
                await fixture.DisposeAfterStartupFailureAsync();
            }
            else
            {
                await CaptureDetachedStartupEvidenceAsync(
                    scenario,
                    evidenceRoot,
                    profile,
                    credentialResource,
                    identity,
                    exception);
                await DisposeScenarioAfterStartupFailureAsync(scenario);
            }
            throw;
        }
    }

    internal static async Task RunAsync(
        string scenarioId,
        string step,
        Func<UiScenarioFixture, Task> action,
        Action<FakeHaScenario>? configure = null,
        CancellationToken cancellationToken = default,
        bool suppressTrayLeftClick = false)
    {
        ArgumentNullException.ThrowIfNull(action);
        WindowsCompanion.UI.Tests.UiCapabilities.RequireInteractive();
        var fixture = await StartAsync(scenarioId, configure, suppressTrayLeftClick);
        Exception? scenarioFailure = null;
        try
        {
            await fixture.ExecuteWithEvidenceAsync(
                    step,
                    () => action(fixture),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            scenarioFailure = exception;
        }

        try
        {
            await fixture.DisposeAsync();
        }
        catch (Exception cleanupException) when (scenarioFailure is not null)
        {
            scenarioFailure.Data["UiScenarioCleanupFailure"] = cleanupException.ToString();
        }

        if (scenarioFailure is not null)
            ExceptionDispatchInfo.Capture(scenarioFailure).Throw();
    }

    internal async Task RestartAsync()
    {
        await StopApplicationAsync();
        var suffix = CredentialResource["WindowsCompanion.Tests.".Length..];
        await LaunchAsync(suffix);
    }

    private Task LaunchAsync(string credentialSuffix)
    {
        var argument = EncodeProfileArgument(credentialSuffix);
        var startInfo = new ProcessStartInfo
        {
            FileName = _applicationPath,
            Arguments = argument,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(_applicationPath)!
        };
        _application = Application.Launch(startInfo);
        _application.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(15));
        Window = _application.GetMainWindow(Automation, TimeSpan.FromSeconds(15))
                 ?? throw new InvalidOperationException("The application did not expose a main window.");
        FailureEvidence = CreateFailureEvidence(Window);
        Window.Focus();
        return Task.CompletedTask;
    }

    internal Task ExecuteWithEvidenceAsync(
        string step,
        Func<Task> action,
        CancellationToken cancellationToken = default) =>
        FailureEvidence.CaptureOnFailureAsync(step, action, cancellationToken);

    internal Task<UiFailureEvidenceResult> CaptureFailureEvidenceAsync(
        string step,
        Exception exception,
        CancellationToken cancellationToken = default) =>
        FailureEvidence.CaptureAsync(step, exception, cancellationToken);

    private string EncodeProfileArgument(string credentialSuffix)
    {
        var json = JsonSerializer.Serialize(new
        {
            settingsDirectory = ProfileDirectory,
            credentialResourceSuffix = credentialSuffix,
            instanceIdentity = InstanceIdentity,
            serverUrl = Scenario.BaseUrl!.AbsoluteUri,
            autoAuthorize = true,
            suppressTrayLeftClick = _suppressTrayLeftClick
        });
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"--test-profile={encoded}";
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var failures = new List<Exception>();
        var evidenceCaptured = false;
        await AttemptAsync(StopApplicationAsync, failures);
        evidenceCaptured = await CaptureCleanupFailureIfNeededAsync(
            failures,
            evidenceCaptured);
        Attempt(DisposeAutomation, failures);
        evidenceCaptured = await CaptureCleanupFailureIfNeededAsync(
            failures,
            evidenceCaptured);
        Attempt(ClearCredentials, failures);
        evidenceCaptured = await CaptureCleanupFailureIfNeededAsync(
            failures,
            evidenceCaptured);
        await AttemptAsync(
            async () => await Scenario.DisposeAsync().ConfigureAwait(false),
            failures);
        evidenceCaptured = await CaptureCleanupFailureIfNeededAsync(
            failures,
            evidenceCaptured);
        Attempt(DeleteProfile, failures);
        await CaptureCleanupFailureIfNeededAsync(failures, evidenceCaptured);

        if (failures.Count > 0)
            throw new AggregateException("One or more UI scenario cleanup steps failed.", failures);
    }

    private async Task StopApplicationAsync()
    {
        var application = Interlocked.Exchange(ref _application, null);
        if (application is null) return;

        var failures = new List<Exception>();
        Process? process = null;
        try
        {
            var applicationExited = false;
            try
            {
                applicationExited = application.HasExited;
            }
            catch (ArgumentException)
            {
                applicationExited = true;
            }
            catch (InvalidOperationException)
            {
                applicationExited = true;
            }

            if (!applicationExited)
            {
                try
                {
                    process = Process.GetProcessById(application.ProcessId);
                }
                catch (ArgumentException)
                {
                    applicationExited = true;
                }
                catch (InvalidOperationException)
                {
                    applicationExited = true;
                }
            }

            if (!applicationExited && process is not null && !HasExited(process))
            {
                TryRequestGracefulShutdown(process.Id);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            if (process is not null && !HasExited(process))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or ArgumentException)
                {
                }
                catch (System.ComponentModel.Win32Exception) when (HasExited(process))
                {
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (process is not null) Attempt(process.Dispose, failures);
            Attempt(application.Dispose, failures);
        }

        if (failures.Count > 0)
            throw new AggregateException("Application shutdown cleanup failed.", failures);
    }

    private static void TryRequestGracefulShutdown(int processId)
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(
                    $@"Local\WindowsCompanion.Shutdown.{processId}",
                    out var shutdown))
            {
                using (shutdown) shutdown.Set();
            }
        }
        catch
        {
            // Exact-process termination remains available as the fallback.
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DisposeAutomation()
    {
        if (Interlocked.Exchange(ref _automationDisposed, 1) == 0)
            Automation.Dispose();
    }

    private void DeleteProfile()
    {
        if (Directory.Exists(ProfileDirectory))
            Directory.Delete(ProfileDirectory, recursive: true);
    }

    private async Task DisposeAfterStartupFailureAsync()
    {
        try
        {
            await DisposeAsync();
        }
        catch
        {
            // Preserve the startup exception after every cleanup step has been attempted.
        }
    }

    private static async Task DisposeScenarioAfterStartupFailureAsync(FakeHaScenario scenario)
    {
        try
        {
            await scenario.DisposeAsync();
        }
        catch
        {
            // Preserve the startup exception.
        }
    }

    private async Task<bool> CaptureCleanupFailureIfNeededAsync(
        IReadOnlyCollection<Exception> failures,
        bool evidenceCaptured)
    {
        if (evidenceCaptured || failures.Count == 0) return evidenceCaptured;
        await CaptureEvidenceBestEffortAsync(
            "scenario cleanup",
            new AggregateException("UI scenario cleanup failed.", failures));
        return true;
    }

    private async Task CaptureEvidenceBestEffortAsync(string step, Exception exception)
    {
        try
        {
            await FailureEvidence.CaptureAsync(step, exception).ConfigureAwait(false);
        }
        catch
        {
            // Evidence cannot replace the startup, scenario, or cleanup failure.
        }
    }

    private static async Task CaptureDetachedStartupEvidenceAsync(
        FakeHaScenario scenario,
        string evidenceRoot,
        string? profileDirectory,
        string? credentialResource,
        string? instanceIdentity,
        Exception exception)
    {
        var sensitiveValues = new[]
            {
                profileDirectory,
                credentialResource,
                instanceIdentity
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!);
        var evidence = new UiFailureEvidence(
            window: null,
            scenario,
            evidenceRoot,
            sensitiveValues,
            AppLogPath(profileDirectory));
        try
        {
            await evidence.CaptureAsync("application startup", exception).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the startup exception.
        }
    }

    private static void Attempt(Action action, ICollection<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task AttemptAsync(
        Func<Task> action,
        ICollection<Exception> failures)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void ClearCredentials()
    {
        var vault = new PasswordVault();
        IReadOnlyList<PasswordCredential> credentials;
        try
        {
            credentials = vault.FindAllByResource(CredentialResource);
        }
        catch
        {
            return;
        }

        var failures = new List<Exception>();
        foreach (var credential in credentials)
        {
            try
            {
                vault.Remove(credential);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("Credential cleanup failed.", failures);
    }

    private static string ResolveApplicationPath(string root)
    {
        var configured = Environment.GetEnvironmentVariable("WINDOWS_COMPANION_UI_APP");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var fullPath = Path.GetFullPath(configured);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("WINDOWS_COMPANION_UI_APP does not exist.", fullPath);
            if (!SupportsTestProfile(fullPath))
            {
                throw new InvalidOperationException(
                    "WINDOWS_COMPANION_UI_APP must reference a Debug build with the test-profile contract.");
            }
            return fullPath;
        }

        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "win-arm64"
            : "win-x64";
        var bin = Path.Combine(root, "src", "WindowsCompanion.App", "bin");
        var candidate = Directory.Exists(bin)
            ? Directory.EnumerateFiles(bin, "WindowsCompanion.exe", SearchOption.AllDirectories)
                .Where(path => path.Split(
                    Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries)
                    .Contains("Debug", StringComparer.OrdinalIgnoreCase))
                .Where(path => path.Contains(architecture, StringComparison.OrdinalIgnoreCase))
                .Where(SupportsTestProfile)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        return candidate ?? throw new FileNotFoundException(
            $"A Debug {architecture} WindowsCompanion.exe build is required. "
            + "Set WINDOWS_COMPANION_UI_APP to its full path.");
    }

    private static bool SupportsTestProfile(string applicationPath)
    {
        var assemblyPath = Path.Combine(
            Path.GetDirectoryName(applicationPath)!,
            "WindowsCompanion.dll");
        if (!File.Exists(assemblyPath)) return false;

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return false;
            var metadata = pe.GetMetadataReader();
            return metadata.TypeDefinitions
                .Select(metadata.GetTypeDefinition)
                .Any(type =>
                    metadata.GetString(type.Namespace) == "WindowsCompanion_App"
                    && metadata.GetString(type.Name) == "TestAppLaunchOptions");
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException)
        {
            return false;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WindowsCompanion.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private UiFailureEvidence CreateFailureEvidence(Window? window) =>
        new(
            window,
            Scenario,
            _evidenceRoot,
            [ProfileDirectory, CredentialResource, InstanceIdentity],
            AppLogPath(ProfileDirectory));

    private static string? AppLogPath(string? profileDirectory) =>
        string.IsNullOrWhiteSpace(profileDirectory)
            ? null
            : Path.Combine(profileDirectory, "app.log");
}
