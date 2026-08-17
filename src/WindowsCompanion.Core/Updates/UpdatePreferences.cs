using System.Text.Json.Serialization;

namespace WindowsCompanion.Core.Updates;

/// <summary>How the companion behaves when a newer stable release is found.</summary>
public enum UpdateMode
{
    /// <summary>
    /// Check for updates, download and verify the matching setup package in the
    /// background, and offer an explicit "Install now" action once it is verified.
    /// The companion never installs without that explicit action.
    /// </summary>
    AutoInstall,

    /// <summary>
    /// Check for updates and show the toast, tray badge, and banner as today, but
    /// never download anything automatically. The user opens the release page
    /// and installs manually.
    /// </summary>
    NotifyOnly,

    /// <summary>Never contact GitHub to check for updates.</summary>
    Disabled
}

/// <summary>
/// The user's update-check and installation preference. Non-secret, so it is
/// persisted alongside the rest of <see cref="Models.ServerConfig"/>.
/// </summary>
public sealed class UpdatePreferences
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UpdateMode Mode { get; set; } = UpdateMode.AutoInstall;
}
