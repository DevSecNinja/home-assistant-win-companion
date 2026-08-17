namespace WindowsCompanion.Core.Updates;

/// <summary>Supported CPU architectures for the published setup packages.</summary>
public enum UpdateInstallPhase
{
    NotStarted,
    Downloading,
    Verifying,
    ReadyToInstall,
    Installing,
    Installed,
    Failed
}

/// <summary>The latest process-wide download/verify/install result.</summary>
public sealed record UpdateInstallState(
    UpdateInstallPhase Phase,
    SemanticVersion Version,
    double DownloadProgress = 0,
    string? ErrorMessage = null,
    long Revision = 0);

/// <summary>Downloads the verified setup package to local disk.</summary>
public interface IUpdatePackageDownloader
{
    /// <summary>Returns the local path of the fully downloaded package.</summary>
    Task<string> DownloadAsync(
        SelectedUpdateAsset asset,
        SemanticVersion version,
        IProgress<double> progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Confirms a downloaded package matches its published checksum and carries a
/// valid GitHub build-provenance attestation for this repository before it may
/// be installed.
/// </summary>
public interface IUpdatePackageVerifier
{
    Task VerifyAsync(
        string packagePath,
        SelectedUpdateAsset asset,
        CancellationToken cancellationToken);
}

/// <summary>Runs the verified setup package silently and relaunches the app.</summary>
public interface IUpdatePackageInstaller
{
    Task InstallAsync(
        string packagePath,
        SemanticVersion version,
        CancellationToken cancellationToken);
}

/// <summary>Thrown by <see cref="IUpdatePackageVerifier"/> when verification fails.</summary>
public sealed class UpdatePackageVerificationException(string message) : Exception(message);

/// <summary>
/// Serializes a download/verify/install run for one available update, cancels
/// superseded work, and publishes only the newest result. Mirrors
/// <see cref="StartupUpdateChecker"/>'s single-flight, revision-guarded pattern.
/// </summary>
public sealed class UpdateInstaller
{
    private const string DownloadFailedMessage =
        "The update could not be downloaded. Check your internet connection and try again.";
    private const string VerificationFailedMessage =
        "The downloaded update could not be verified and was discarded.";
    private const string NoAssetMessage =
        "No downloadable setup package is published for this PC's architecture.";
    private const string InstallFailedMessage =
        "The update could not be installed. You can open the release page instead.";

    private readonly IUpdatePackageDownloader _downloader;
    private readonly IUpdatePackageVerifier _verifier;
    private readonly IUpdatePackageInstaller _installer;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private CancellationTokenSource? _active;
    private UpdateInstallState _state;
    private long _revision;
    private string? _verifiedPackagePath;

    public UpdateInstaller(
        IUpdatePackageDownloader downloader,
        IUpdatePackageVerifier verifier,
        IUpdatePackageInstaller installer)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _state = new(UpdateInstallPhase.NotStarted, SemanticVersion.Zero);
    }

    public UpdateInstallState State
    {
        get { lock (_gate) return _state; }
    }

    public event Action<UpdateInstallState>? StateChanged;

    /// <summary>Downloads and verifies the update matching this architecture.</summary>
    public async Task DownloadAsync(
        AvailableUpdate update,
        UpdateArchitecture architecture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // An install already in progress owns the verified package and is
        // running the silent installer; a new download must not race it by
        // replacing _verifiedPackagePath or advancing the revision out from
        // under it. The next automatic/periodic check will retry once the
        // install finishes (successfully or not).
        lock (_gate)
        {
            if (_state.Phase == UpdateInstallPhase.Installing) return;
        }

        var asset = UpdateAssetSelector.Select(update.AvailableVersion, update.Assets, architecture);
        if (asset is null)
        {
            var noAssetRevision = CancelActiveRunAndAdvanceRevision();
            Publish(new(
                UpdateInstallPhase.Failed,
                update.AvailableVersion,
                ErrorMessage: NoAssetMessage,
                Revision: noAssetRevision));
            return;
        }

        CancellationTokenSource run;
        long revision;
        lock (_gate)
        {
            revision = ++_revision;
            _active?.Cancel();
            run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _active = run;
        }

        var entered = false;
        try
        {
            await _singleFlight.WaitAsync(run.Token).ConfigureAwait(false);
            entered = true;

            if (!PublishIfCurrent(revision, run, new(
                    UpdateInstallPhase.Downloading,
                    update.AvailableVersion,
                    Revision: revision)))
            {
                return;
            }

            var progress = new SynchronousProgress<double>(fraction =>
                PublishIfCurrent(revision, run, new(
                    UpdateInstallPhase.Downloading,
                    update.AvailableVersion,
                    fraction,
                    Revision: revision)));

            string path;
            try
            {
                path = await _downloader
                    .DownloadAsync(asset, update.AvailableVersion, progress, run.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                PublishIfCurrent(revision, run, new(
                    UpdateInstallPhase.Failed,
                    update.AvailableVersion,
                    ErrorMessage: DownloadFailedMessage,
                    Revision: revision));
                return;
            }

            if (!PublishIfCurrent(revision, run, new(
                    UpdateInstallPhase.Verifying,
                    update.AvailableVersion,
                    1,
                    Revision: revision)))
            {
                return;
            }

            try
            {
                await _verifier.VerifyAsync(path, asset, run.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                TryDelete(path);
                PublishIfCurrent(revision, run, new(
                    UpdateInstallPhase.Failed,
                    update.AvailableVersion,
                    ErrorMessage: VerificationFailedMessage,
                    Revision: revision));
                return;
            }

            lock (_gate)
            {
                if (revision == _revision) _verifiedPackagePath = path;
            }

            PublishIfCurrent(revision, run, new(
                UpdateInstallPhase.ReadyToInstall,
                update.AvailableVersion,
                1,
                Revision: revision));
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // Superseded by a newer request or explicit cancellation; nothing to publish.
        }
        finally
        {
            if (entered) _singleFlight.Release();
            lock (_gate)
            {
                if (ReferenceEquals(_active, run)) _active = null;
            }
            run.Dispose();
        }
    }

    /// <summary>
    /// Runs the verified package. The installer is expected to close this
    /// process as part of installing, so this call may never return normally.
    /// </summary>
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        var (path, installing) = BeginInstall();
        StateChanged?.Invoke(installing);

        try
        {
            await _installer.InstallAsync(path, installing.Version, cancellationToken).ConfigureAwait(false);
            Publish(installing with { Phase = UpdateInstallPhase.Installed });
        }
        catch
        {
            Publish(installing with
            {
                Phase = UpdateInstallPhase.Failed,
                ErrorMessage = InstallFailedMessage
            });
            throw;
        }
    }

    /// <summary>
    /// Atomically checks that a verified update is ready and transitions to
    /// <see cref="UpdateInstallPhase.Installing"/> under the same lock as the
    /// check, so a second concurrent <see cref="InstallAsync"/> call observes
    /// the new phase and is rejected instead of also invoking the installer.
    /// </summary>
    private (string Path, UpdateInstallState Installing) BeginInstall()
    {
        lock (_gate)
        {
            var current = _state;
            if (current.Phase != UpdateInstallPhase.ReadyToInstall || _verifiedPackagePath is null)
                throw new InvalidOperationException("No verified update is ready to install.");

            var installing = current with { Phase = UpdateInstallPhase.Installing };
            _state = installing;
            return (_verifiedPackagePath, installing);
        }
    }

    public Task CancelAsync()
    {
        lock (_gate)
        {
            _revision++;
            _active?.Cancel();
        }

        return Task.CompletedTask;
    }

    private long CancelActiveRunAndAdvanceRevision()
    {
        lock (_gate)
        {
            _active?.Cancel();
            _active = null;
            return ++_revision;
        }
    }

    private bool PublishIfCurrent(long revision, CancellationTokenSource run, UpdateInstallState state)
    {
        if (run.IsCancellationRequested) return false;
        return Publish(state, revision);
    }

    private bool Publish(UpdateInstallState state) => Publish(state, state.Revision);

    private bool Publish(UpdateInstallState state, long revision)
    {
        Action<UpdateInstallState>? changed;
        lock (_gate)
        {
            if (revision != _revision) return false;
            _state = state;
            changed = StateChanged;
        }

        changed?.Invoke(state);
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Reports progress synchronously on the calling thread, instead of
    /// <see cref="System.Progress{T}"/>'s marshaling via <c>SynchronizationContext.Post</c>.
    /// Without this, a queued download-progress callback can execute after a
    /// later phase (Verifying, ReadyToInstall) was already published on the
    /// same revision, silently reverting the published state back to
    /// Downloading.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
