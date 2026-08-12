using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public sealed class SensorPreviewCancellationTests
{
    [Fact]
    public void Beginning_list_preview_cancels_previous_preview()
    {
        var previews = new SensorPreviewCancellation();
        using var first = previews.BeginList();
        using var second = previews.BeginList();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void Ending_stale_list_preview_preserves_current_preview()
    {
        var previews = new SensorPreviewCancellation();
        using var first = previews.BeginList();
        using var second = previews.BeginList();

        previews.EndList(first);
        using var third = previews.BeginList();

        Assert.True(second.IsCancellationRequested);
        Assert.False(third.IsCancellationRequested);
    }

    [Fact]
    public void Beginning_row_preview_only_cancels_same_sensor()
    {
        var previews = new SensorPreviewCancellation();
        using var firstBattery = previews.BeginRow("battery");
        using var network = previews.BeginRow("network");
        using var secondBattery = previews.BeginRow("battery");

        Assert.True(firstBattery.IsCancellationRequested);
        Assert.False(network.IsCancellationRequested);
        Assert.False(secondBattery.IsCancellationRequested);
    }

    [Fact]
    public void Ending_stale_row_preview_preserves_current_preview()
    {
        var previews = new SensorPreviewCancellation();
        using var first = previews.BeginRow("battery");
        using var second = previews.BeginRow("battery");

        previews.EndRow("battery", first);
        using var third = previews.BeginRow("battery");

        Assert.True(second.IsCancellationRequested);
        Assert.False(third.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_all_cancels_list_and_each_row_preview()
    {
        var previews = new SensorPreviewCancellation();
        using var list = previews.BeginList();
        using var battery = previews.BeginRow("battery");
        using var network = previews.BeginRow("network");

        previews.CancelAll();

        Assert.True(list.IsCancellationRequested);
        Assert.True(battery.IsCancellationRequested);
        Assert.True(network.IsCancellationRequested);
    }
}
