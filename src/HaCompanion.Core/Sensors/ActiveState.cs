namespace HaCompanion.Core.Sensors;

/// <summary>
/// The composite of OS conditions that determine whether the machine is actively in
/// use, mirroring the macOS companion's model. Pure data: deciding what counts as
/// "active" lives here so it can be tested without any Windows API.
/// </summary>
public readonly record struct ActiveState(
    bool Idle = false,
    bool Locked = false,
    bool Screensaver = false,
    bool Sleeping = false,
    bool FastUserSwitched = false)
{
    /// <summary>Active means none of the reasons for being away are true.</summary>
    public bool IsActive => !Idle && !Locked && !Screensaver && !Sleeping && !FastUserSwitched;

    /// <summary>
    /// Each sub-state is exposed so an automation can distinguish "locked" from
    /// "just idle", which the single boolean cannot express.
    /// </summary>
    public IDictionary<string, object> ToAttributes() => new Dictionary<string, object>
    {
        ["Idle"] = Idle,
        ["Locked"] = Locked,
        ["Screensaver"] = Screensaver,
        ["Sleeping"] = Sleeping,
        ["Fast User Switched"] = FastUserSwitched
    };
}
