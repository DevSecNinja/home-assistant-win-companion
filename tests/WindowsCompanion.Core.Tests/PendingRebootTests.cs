using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

/// <summary>
/// Truth-table, icon and attribute rules for the pending-reboot sensor. All of
/// it is deterministic Core logic, so it is verified without touching the
/// registry.
/// </summary>
public class PendingRebootTests
{
    [Fact]
    public void No_signals_means_no_reboot_pending()
    {
        Assert.False(PendingRebootState.None.IsRebootPending);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void Any_signal_means_reboot_pending(
        bool windowsUpdate, bool componentBasedServicing, bool pendingFileRename)
    {
        var state = new PendingRebootState(windowsUpdate, componentBasedServicing, pendingFileRename);

        Assert.True(state.IsRebootPending);
    }

    [Fact]
    public void Icon_reflects_whether_a_reboot_is_pending()
    {
        Assert.Equal(
            "mdi:restart-alert",
            PendingRebootFormatter.IconFor(new PendingRebootState(true, false, false)));
        Assert.Equal(
            "mdi:restart-off",
            PendingRebootFormatter.IconFor(PendingRebootState.None));
    }

    [Fact]
    public void Attributes_expose_each_underlying_signal()
    {
        var state = new PendingRebootState(true, false, true);

        var attributes = PendingRebootFormatter.BuildAttributes(state);

        Assert.Equal(true, attributes["windows_update_reboot_required"]);
        Assert.Equal(false, attributes["component_based_servicing_reboot_pending"]);
        Assert.Equal(true, attributes["pending_file_rename_operations"]);
    }

    [Fact]
    public void Swapping_which_signal_is_set_while_still_pending_is_not_a_meaningful_change()
    {
        // Record equality alone would report this as changed (different field
        // values), but the aggregate on/off state - the only thing the entity
        // and its notifications expose - stays true throughout.
        var windowsUpdateOnly = new PendingRebootState(true, false, false);
        var componentBasedServicingOnly = new PendingRebootState(false, true, false);

        Assert.False(
            PendingRebootFormatter.HasMeaningfullyChanged(windowsUpdateOnly, componentBasedServicingOnly));
    }

    [Fact]
    public void Flipping_the_aggregate_state_is_a_meaningful_change()
    {
        Assert.True(PendingRebootFormatter.HasMeaningfullyChanged(
            PendingRebootState.None,
            new PendingRebootState(true, false, false)));
        Assert.True(PendingRebootFormatter.HasMeaningfullyChanged(
            new PendingRebootState(true, false, false),
            PendingRebootState.None));
    }
}
