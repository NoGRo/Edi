using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Players;
using Edi.Core.Services;
using System.ComponentModel;

namespace Edi.Core.Tests.Players;

public class MultiChannelPlayerTests
{
    [Fact]
    public void ResetChannelsKeepsConnectedDevicesAssigned()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-multi-channel-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var configuration = new ConfigurationManager(
                Path.Combine(temporaryDirectory, "EdiConfig.json"),
                Path.Combine(temporaryDirectory, "UserConfig.json"));
            var collector = new DeviceCollector(configuration, null);
            var manager = new ChannelManager<IPlayer>(
                () => new TrackingPlayer());
            var player = new MultiChannelPlayer(
                null,
                manager,
                collector);
            var device = new TestDevice();
            configuration.Get<DevicesConfig>().Devices[device.Name] =
                new DeviceConfig
                {
                    Variant = "default",
                    Channel = "secondary"
                };

            collector.LoadDevice(device);
            var previousSecondary =
                Assert.IsType<TrackingPlayer>(manager.Get("secondary"));

            player.ResetChannels(["main", "secondary"]);
            player.ResetChannels(["main", "secondary"]);

            var secondary =
                Assert.IsType<TrackingPlayer>(manager.Get("secondary"));
            Assert.Contains(device, secondary.Devices);
            Assert.DoesNotContain(device, previousSecondary.Devices);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class TrackingPlayer : IPlayer
    {
        public List<IDevice> Devices { get; } = [];

        public void Add(IDevice device) => Devices.Add(device);
        public void Remove(IDevice device) => Devices.Remove(device);
        public Task Intensity(int max) => Task.CompletedTask;
        public Task Play(string name, long seek = 0) => Task.CompletedTask;
        public Task Stop() => Task.CompletedTask;
        public Task Pause(bool untilResume = false) => Task.CompletedTask;
        public Task Resume(bool atCurrentTime = false) => Task.CompletedTask;
    }

    private sealed class TestDevice : IDevice, INotifyPropertyChanged
    {
        private string channel = string.Empty;
        private string selectedVariant = string.Empty;

        public string Channel
        {
            get => channel;
            set
            {
                if (channel == value)
                    return;

                channel = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(Channel)));
            }
        }

        public string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                if (selectedVariant == value)
                    return;

                selectedVariant = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(SelectedVariant)));
            }
        }

        public IEnumerable<string> Variants => ["default"];
        public string Name { get; set; } = "channel-device";
        public bool IsReady => true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string DefaultVariant() => "default";
        public Task PlayGallery(string name, long seek = 0)
            => Task.CompletedTask;
        public Task Stop() => Task.CompletedTask;
    }
}
