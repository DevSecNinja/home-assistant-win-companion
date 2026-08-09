namespace HaCompanion.Core.Lifecycle;

/// <summary>
/// What the user is told before switching the lifecycle sensor on, and when.
/// </summary>
/// <remarks>
/// The limits below are inherent to how Windows notifies applications, not defects
/// waiting to be fixed. Somebody who enables this sensor expecting a guarantee will
/// eventually see a missed shutdown and report it as a bug, so the warning is shown
/// once, up front, at the only moment it can still change the decision.
///
/// The wording and the rule live here rather than in the window so they can be
/// tested, and so the same text is used wherever the sensor is offered.
/// </remarks>
public static class LifecycleSensorAdvisory
{
    public const string Title = "Best-effort Windows lifecycle detection";

    public const string PrimaryButton = "Enable anyway";

    public const string CloseButton = "Cancel";

    /// <summary>Short label shown next to the sensor in the list.</summary>
    public const string Badge = "best effort";

    public const string Message =
        "Windows does not promise to tell an application that the machine is "
        + "suspending or shutting down, and it can stop the app before anything is "
        + "sent. So a transition may go unnoticed, and the final update may never "
        + "reach Home Assistant - a power cut or a forced shutdown will simply skip "
        + "it.\n\n"
        + "Windows also does not say which transition is coming: sleep cannot be "
        + "told apart from hibernate, and a shutdown cannot be told apart from a "
        + "restart, until the machine is already gone.\n\n"
        + "Anything that was not delivered is written to a local journal and "
        + "reported after the next successful connection, so the history catches up "
        + "even when the live update does not.\n\n"
        + "Do not use this sensor as the only trigger for an automation that "
        + "matters.";

    /// <summary>
    /// Whether flipping <paramref name="uniqueId"/> to <paramref name="turningOn"/>
    /// should be confirmed first. Only the change from off to on qualifies: turning
    /// the sensor off, or re-applying a state it already has, warns about nothing.
    /// </summary>
    public static bool RequiresConfirmation(string uniqueId, bool turningOn, bool currentlyEnabled) =>
        turningOn && !currentlyEnabled && IsAdvisedSensor(uniqueId);

    /// <summary>Whether the sensor carries the caution badge in the list.</summary>
    public static bool IsAdvisedSensor(string uniqueId) =>
        string.Equals(uniqueId, LifecycleSensorSource.SystemStateId, StringComparison.Ordinal);
}
