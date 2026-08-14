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
    private bool _accessRequested;

    public WindowsLocationProvider(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<LocationResult> GetLocationAsync(
        CancellationToken cancellationToken = default)
    {
        return await RunOnDispatcherAsync(async () =>
        {
            if (!_accessRequested)
            {
                // RequestAccessAsync must run on the UI thread while the app is
                // foregrounded, and only ever prompts once per app lifetime.
                var access = await Geolocator.RequestAccessAsync();
                _accessRequested = true;
                if (access is GeolocationAccessStatus.Denied or GeolocationAccessStatus.Unspecified)
                    return LocationResult.Unavailable(LocationStatus.PermissionDenied);
            }

            if (_geolocator.LocationStatus == PositionStatus.Disabled)
                return LocationResult.Unavailable(LocationStatus.PermissionDenied);

            try
            {
                var position = await _geolocator.GetGeopositionAsync();
                var coordinate = position.Coordinate;
                return LocationResult.Ready(
                    coordinate.Point.Position.Latitude,
                    coordinate.Point.Position.Longitude,
                    coordinate.Accuracy);
            }
            catch (Exception)
            {
                // GetGeopositionAsync throws on missing permission, timeout, or no
                // data source; all of those are "no fix right now", not a crash.
                return LocationResult.Unavailable();
            }
        }).ConfigureAwait(false);
    }

    private Task<LocationResult> RunOnDispatcherAsync(Func<Task<LocationResult>> action)
    {
        var completion = new TaskCompletionSource<LocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    completion.TrySetResult(await action().ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetResult(LocationResult.Unavailable());
        }

        return completion.Task;
    }
}
