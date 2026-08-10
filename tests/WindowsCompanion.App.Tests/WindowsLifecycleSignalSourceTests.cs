using System.Runtime.Versioning;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowsLifecycleSignalSourceCollection
{
    public const string Name = "Windows lifecycle signal source";
}

[Collection(WindowsLifecycleSignalSourceCollection.Name)]
public sealed class WindowsLifecycleSignalSourceTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Repeated_start_stop_generations_release_window_thread_and_event_hooks()
    {
        using var source = new WindowsLifecycleSignalSource();

        for (var generation = 0; generation < 100; generation++)
        {
            source.Start();

            Assert.True(source.HasRunningPump);
            Assert.True(source.HasWindow);
            Assert.True(source.EventsSubscribed);

            source.Stop();

            Assert.False(source.HasRunningPump);
            Assert.False(source.HasWindow);
            Assert.False(source.EventsSubscribed);
        }
    }
}
