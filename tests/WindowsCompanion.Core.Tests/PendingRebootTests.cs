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
}
