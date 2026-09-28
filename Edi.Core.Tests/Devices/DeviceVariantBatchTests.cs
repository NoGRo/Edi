using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Services;
using Edi.Core.Tests.Support;
using System.ComponentModel;

namespace Edi.Core.Tests.Devices;

public class DeviceVariantBatchTests
{
    [Fact]
    public async Task TransientPauseResumesAtCurrentGalleryTimeWhileOtherDevicesKeepPlaying()
    {
        await using var rig = await PlayerTestRig.CreateAsync(addDefaultDevice: false);
        var collector = new DeviceCollector(rig.Configuration, null!);
        var first = new BlockingDevice("First", Task.CompletedTask);
        var second = new BlockingDevice("Second", Task.CompletedTask);
        collector.Devices.AddRange([first, second]);
        rig.DevicePlayer.Add(first);
        rig.DevicePlayer.Add(second);
        await Task.WhenAll(first.StopStarted.Task, second.StopStarted.Task)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var service = new DeviceConfiguration(collector, rig.Configuration, rig.DevicePlayer);
        await rig.DevicePlayer.Play("scene", seek: 500);
        await Task.WhenAll(first.PlayStarted.Task, second.PlayStarted.Task)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        first.StopStarted = NewCompletion();
        await service.SelectVariants(new Dictionary<IDevice, string> { [first] = "None" }, persist: false);
        await first.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        second.PlayStarted = NewSeekCompletion();
        await rig.DevicePlayer.Play("scene", seek: 1500);
        await second.PlayStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        first.PlayStarted = NewSeekCompletion();
        await service.SelectVariants(new Dictionary<IDevice, string> { [first] = "secondary" }, persist: false);
        var seek = await first.PlayStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.InRange(seek, 1500, 2500);
        Assert.Equal("primary", second.SelectedVariant);
        rig.DevicePlayer.Remove(first);
        rig.DevicePlayer.Remove(second);
    }

    [Fact]
    public async Task TransientControlsDoNotOverwriteSavedVariantOrRangeOrAwaitAnExtraStop()
    {
        var directory = Path.Combine(Path.GetTempPath(), "edi-variant-batch-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationManager(
                Path.Combine(directory, "EdiConfig.json"), Path.Combine(directory, "UserConfig.json"));
            var collector = new DeviceCollector(configuration, null!);
            var device = new BlockingDevice("First", NewCompletion().Task);
            collector.Devices.Add(device);
            var config = configuration.Get<DevicesConfig>();
            config.Devices[device.Name] = new DeviceConfig { Variant = "primary", Min = 10, Max = 90 };
            var service = new DeviceConfiguration(collector, configuration, null!);

            await service.SelectVariants(new Dictionary<IDevice, string> { [device] = "None" }, persist: false)
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await service.SelectRange(device, 10, 30, persist: false);
            Assert.False(device.StopStarted.Task.IsCompleted);
            Assert.Equal("None", device.SelectedVariant);
            Assert.Equal("primary", config.Devices[device.Name].Variant);
            Assert.Equal(30, device.Max);
            Assert.Equal(90, service.ConfiguredRange(device).Max);

            await service.SelectVariants(new Dictionary<IDevice, string> { [device] = "secondary" }, persist: false);
            Assert.Equal("secondary", device.SelectedVariant);
            Assert.Equal("primary", config.Devices[device.Name].Variant);
            await service.SelectRange(device, 15, 80);
            Assert.Equal(15, service.ConfiguredRange(device).Min);
            Assert.Equal(80, service.ConfiguredRange(device).Max);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

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
    private static TaskCompletionSource<long> NewSeekCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class BlockingDevice(string name, Task releaseStop) : IDevice, IRange, INotifyPropertyChanged
    {
        public TaskCompletionSource StopStarted { get; set; } = NewCompletion();
        public TaskCompletionSource<long> PlayStarted { get; set; } = NewSeekCompletion();
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Channel { get; set; } = string.Empty;
        private string selectedVariant = "primary";
        public string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                selectedVariant = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedVariant)));
            }
        }
        public IEnumerable<string> Variants => ["primary", "secondary", "None"];
        public string Name { get; set; } = name;
        public bool IsReady => true;
        public int Min { get; set; }
        public int Max { get; set; } = 100;
        public string DefaultVariant() => "primary";
        public Task PlayGallery(string name, long seek = 0)
        {
            PlayStarted.TrySetResult(seek);
            return Task.CompletedTask;
        }

        public async Task Stop()
        {
            StopStarted.TrySetResult();
            await releaseStop;
        }
    }
}
