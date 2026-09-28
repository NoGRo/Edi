using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Services;

namespace Edi.Core.Tests.Devices;

public class DeviceVariantBatchTests
{
    [Fact]
    public async Task SelectVariantsStopsDevicesConcurrentlyAndAppliesEverySelection()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "edi-variant-batch-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationManager(
                Path.Combine(directory, "EdiConfig.json"),
                Path.Combine(directory, "UserConfig.json"));
            var collector = new DeviceCollector(configuration, null!);
            var releaseStops = NewCompletion();
            var first = new BlockingDevice("First", releaseStops.Task);
            var second = new BlockingDevice("Second", releaseStops.Task);
            collector.Devices.AddRange([first, second]);
            var service = new DeviceConfiguration(collector, configuration, null!);

            var selection = service.SelectVariants(new Dictionary<IDevice, string>
            {
                [first] = "secondary",
                [second] = "secondary"
            });

            await Task.WhenAll(first.StopStarted.Task, second.StopStarted.Task)
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.False(selection.IsCompleted);

            releaseStops.SetResult();
            await selection;

            Assert.Equal("secondary", first.SelectedVariant);
            Assert.Equal("secondary", second.SelectedVariant);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static TaskCompletionSource NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class BlockingDevice(string name, Task releaseStop) : IDevice
    {
        public TaskCompletionSource StopStarted { get; } = NewCompletion();
        public string Channel { get; set; } = string.Empty;
        public string SelectedVariant { get; set; } = "primary";
        public IEnumerable<string> Variants => ["primary", "secondary", "None"];
        public string Name { get; set; } = name;
        public bool IsReady => true;
        public string DefaultVariant() => "primary";
        public Task PlayGallery(string name, long seek = 0) => Task.CompletedTask;

        public async Task Stop()
        {
            StopStarted.SetResult();
            await releaseStop;
        }
    }
}
