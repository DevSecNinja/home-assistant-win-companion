namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// How sensitive a sensor's value is, which drives whether it is enabled by
/// default and whether its value may be logged.
/// </summary>
public enum SensorPrivacy
{
    /// <summary>Reveals nothing about the user's content or whereabouts.</summary>
    Benign,

    /// <summary>Describes the machine or its network; off by default.</summary>
    Sensitive
}

/// <summary>
/// Static metadata describing a sensor the companion is able to report. The
/// catalog is what the Settings UI renders and what the sync service iterates;
/// producing an actual value is the job of an <see cref="ISensorSource"/>.
/// </summary>
public sealed record SensorDefinition(
    string UniqueId,
    string Name,
    string Description,
    SensorPrivacy Privacy,
    bool EnabledByDefault,
    string? ResourceUsage = null,
    string? AutomationIdea = null,
    string? OptInPlaceholder = null)
{
    /// <summary>Privacy-sensitive values must never be written to logs.</summary>
    public bool Loggable => Privacy == SensorPrivacy.Benign;

    /// <summary>Local preview shown without collecting a sensitive disabled value.</summary>
    public string DisabledPreview => OptInPlaceholder ?? "Enable to read this value";
}
