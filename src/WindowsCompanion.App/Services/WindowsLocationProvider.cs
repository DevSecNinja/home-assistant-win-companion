using Microsoft.UI.Dispatching;
using Windows.Devices.Geolocation;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reads the device's current position through
/// <see cref="Windows.Devices.Geolocation.Geolocator"/>. Not unit tested
/// directly - it wraps a live WinRT API and is validated manually per
/// specs/013-location-sensor/quickstart.md.
/// </summary>
public sealed class WindowsLocationProvider : ILocationProvider
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Geolocator _geolocator = new();
    private bool _accessGranted;

    public WindowsLocationProvider(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task<LocationResult> GetLocationAsync(
        CancellationToken cancellationToken = default) =>
        RunOnDispatcherAsync(
            token => QueryAsync(token),
            cancellationToken);

    private async Task<LocationResult> QueryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accessGranted)
        {
            // RequestAccessAsync must run on the UI thread while the app is
            // foregrounded. Only cache success: if the window is hidden (e.g.
            // during startup restore) the request can come back Denied or
            // Unspecified without ever showing the prompt, so retry on the
            // next poll once the window is foregrounded instead of latching
            // a false permanent denial.
            var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken)
                .ConfigureAwait(false);
            if (access != GeolocationAccessStatus.Allowed)
                return LocationResult.Unavailable(LocationStatus.PermissionDenied);
            _accessGranted = true;
        }

        if (_geolocator.LocationStatus == PositionStatus.Disabled)
            return LocationResult.Unavailable(LocationStatus.PermissionDenied);

        try
        {
            var position = await _geolocator.GetGeopositionAsync().AsTask(cancellationToken)
                .ConfigureAwait(false);
            var coordinate = position.Coordinate;
            return LocationResult.Ready(
                coordinate.Point.Position.Latitude,
                coordinate.Point.Position.Longitude,
                coordinate.Accuracy);
        }
        catch (UnauthorizedAccessException)
        {
            // Permission can be revoked between the status check above and
            // this call; report it distinctly so the UI still points at the
            // Windows location settings remediation instead of a generic
            // "unavailable".
            _accessGranted = false;
            return LocationResult.Unavailable(LocationStatus.PermissionDenied);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Any other failure (timeout, no data source) is "no fix right
            // now", not a crash.
            return LocationResult.Unavailable();
        }
    }

    private async Task<LocationResult> RunOnDispatcherAsync(
        Func<CancellationToken, Task<LocationResult>> action,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<LocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // Keep the registration alive until completion.Task settles: disposing
        // it as soon as this method returns (as a synchronous `using` would)
        // would drop cancellation delivered after TryEnqueue succeeds but
        // before the queued delegate runs, hanging the caller indefinitely.
        using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        if (!_dispatcher.TryEnqueue(async () =>
            {
                if (cancellationToken.IsCancellationRequested) return;

                try
                {
                    completion.TrySetResult(await action(cancellationToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetResult(LocationResult.Unavailable());
        }

        return await completion.Task.ConfigureAwait(false);
    }
}
