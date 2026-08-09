namespace HaCompanion.Core.Sensors;

/// <summary>
/// The OS hook that reports network changes. Wrapping it keeps the subscription
/// lifecycle out of the platform code and testable: the companion must hold exactly
/// one subscription while a network sensor is enabled, and none once it is not.
/// </summary>
public interface INetworkChangeWatcher
{
    /// <summary>Subscribes. Called at most once before a matching <see cref="Stop"/>.</summary>
    void Start(Action onChanged);

    /// <summary>Releases the subscription. Called at most once per <see cref="Start"/>.</summary>
    void Stop();
}
