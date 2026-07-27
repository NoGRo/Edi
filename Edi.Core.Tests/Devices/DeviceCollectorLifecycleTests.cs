using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Services;

namespace Edi.Core.Tests.Devices;

public class DeviceCollectorLifecycleTests
{
    [Fact]
    public async Task ReinitializeDisconnectsBeforeReloadAndReconnectsAfterward()
    {
        var events = new List<string>();
        var provider = new RecordingProvider(events);
        using var rig = CreateCollector(provider);

        await rig.Collector.Reinitialize(async () =>
        {
            events.Add("reload");
            await Task.CompletedTask;
        });

        Assert.Equal(
            ["disconnect", "reload", "init"],
            events);
    }

    [Fact]
    public async Task ConcurrentReinitializationsRunAsCompleteSequentialTransitions()
    {
        var events = new List<string>();
        var provider = new RecordingProvider(events);
        using var rig = CreateCollector(provider);
        var firstReloadStarted = NewCompletion();
        var releaseFirstReload = NewCompletion();

        var first = rig.Collector.Reinitialize(async () =>
        {
            events.Add("reload-1");
            firstReloadStarted.SetResult();
            await releaseFirstReload.Task;
        });
        await firstReloadStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        var second = rig.Collector.Reinitialize(() =>
        {
            events.Add("reload-2");
            return Task.CompletedTask;
        });

        Assert.False(second.IsCompleted);
        releaseFirstReload.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(
            [
                "disconnect",
                "reload-1",
                "init",
                "disconnect",
                "reload-2",
                "init"
            ],
            events);
    }

    private static CollectorRig CreateCollector(
        IDeviceProvider provider)
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-device-lifecycle-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var configuration = new ConfigurationManager(
            Path.Combine(temporaryDirectory, "EdiConfig.json"),
            Path.Combine(temporaryDirectory, "UserConfig.json"));
        var collector = new DeviceCollector(configuration, null);
        collector.Providers.Add(provider);
        return new CollectorRig(temporaryDirectory, collector);
    }

    private static TaskCompletionSource NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingProvider(List<string> events)
        : IDeviceProvider
    {
        public Task Init()
        {
            events.Add("init");
            return Task.CompletedTask;
        }

        public Task Disconnect()
        {
            events.Add("disconnect");
            return Task.CompletedTask;
        }
    }

    private sealed class CollectorRig(
        string temporaryDirectory,
        DeviceCollector collector) : IDisposable
    {
        public DeviceCollector Collector { get; } = collector;

        public void Dispose()
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
