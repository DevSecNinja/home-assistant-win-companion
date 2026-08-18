using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public sealed class SensorPreviewCancellationTests
{
    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    public void Presentation_requires_visible_active_sensors_view(
        bool sensorsSelected,
        bool windowVisible,
        bool minimized,
        bool exiting,
        bool expected)
    {
        Assert.Equal(
            expected,
            SensorPreviewPresentation.IsActive(
                sensorsSelected,
                windowVisible,
                minimized,
                exiting));
    }

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
    public void Try_begin_list_does_not_overlap_current_preview()
    {
        var previews = new SensorPreviewCancellation();
        using var first = previews.TryBeginList();

        var overlapping = previews.TryBeginList();

        Assert.NotNull(first);
        Assert.Null(overlapping);
    }

    [Fact]
    public void Ending_list_allows_next_non_overlapping_preview()
    {
        var previews = new SensorPreviewCancellation();
        using var first = previews.TryBeginList();
        Assert.NotNull(first);
        previews.EndList(first);

        using var second = previews.TryBeginList();

        Assert.NotNull(second);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_list_stops_only_list_preview()
    {
        var previews = new SensorPreviewCancellation();
        using var list = previews.BeginList();
        using var row = previews.BeginRow("battery");

        previews.CancelList();

        Assert.True(list.IsCancellationRequested);
        Assert.False(row.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_list_keeps_owner_until_canceled_preview_ends()
    {
        var previews = new SensorPreviewCancellation();
        using var list = previews.BeginList();

        previews.CancelList();

        Assert.Null(previews.TryBeginList());
        previews.EndList(list);
        using var next = previews.TryBeginList();
        Assert.NotNull(next);
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
