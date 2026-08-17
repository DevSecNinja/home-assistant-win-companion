namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// The standard Windows signals that indicate a restart is needed to finish
/// applying updates or a pending file operation.
/// </summary>
/// <param name="WindowsUpdateRebootRequired">
/// Windows Update's own <c>RebootRequired</c> key, set after installing
/// updates that need a restart.
/// </param>
/// <param name="ComponentBasedServicingRebootPending">
/// Component-Based Servicing's <c>RebootPending</c> key, set while the
/// servicing stack has staged changes awaiting a restart.
/// </param>
/// <param name="PendingFileRenameOperations">
/// Whether the Session Manager has file rename/delete operations queued for
/// the next boot, used by installers that cannot replace an in-use file.
/// </param>
public readonly record struct PendingRebootState(
    bool WindowsUpdateRebootRequired,
    bool ComponentBasedServicingRebootPending,
    bool PendingFileRenameOperations)
{
    /// <summary>No signals observed, e.g. before the first check has run.</summary>
    public static PendingRebootState None => default;

    /// <summary>True if any of the underlying signals indicate a pending restart.</summary>
    public bool IsRebootPending =>
        WindowsUpdateRebootRequired
        || ComponentBasedServicingRebootPending
        || PendingFileRenameOperations;
}

/// <summary>
/// Derives the pending-reboot sensor's icon and diagnostic attributes.
/// </summary>
/// <remarks>
/// The entity itself stays a single boolean so it is trivial to automate on;
/// which specific signal tripped is only useful for diagnosing why, so it goes
/// in attributes instead of separate entities. Everything here is pure and
/// unit tested.
/// </remarks>
public static class PendingRebootFormatter
{
    public static string IconFor(PendingRebootState state) =>
        state.IsRebootPending ? "mdi:restart-alert" : "mdi:restart-off";

    public static IDictionary<string, object> BuildAttributes(PendingRebootState state) =>
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["windows_update_reboot_required"] = state.WindowsUpdateRebootRequired,
            ["component_based_servicing_reboot_pending"] = state.ComponentBasedServicingRebootPending,
            ["pending_file_rename_operations"] = state.PendingFileRenameOperations
        };

    /// <summary>
    /// Whether a new reading is worth a push: only the aggregate on/off state
    /// matters, not which underlying signal tripped. Record equality alone would
    /// notify on a signal swap (e.g. Windows Update clearing while Component-Based
    /// Servicing takes over) even though <see cref="PendingRebootState.IsRebootPending"/>
    /// never changed.
    /// </summary>
    public static bool HasMeaningfullyChanged(PendingRebootState previous, PendingRebootState current) =>
        previous.IsRebootPending != current.IsRebootPending;
}
