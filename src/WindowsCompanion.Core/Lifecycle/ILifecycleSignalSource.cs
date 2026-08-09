namespace WindowsCompanion.Core.Lifecycle;

/// <summary>
/// A platform hook that reports Windows lifecycle notifications. Implemented in the
/// App project; abstracted here so every decision stays testable without Windows.
/// </summary>
public interface ILifecycleSignalSource
{
    /// <summary>
    /// Raised for each observed notification. Handlers must not block: on the
    /// shutdown path this runs inside a window procedure Windows is waiting on.
    /// </summary>
    event Action<LifecycleSignal>? SignalObserved;

    void Start();

    void Stop();
}
