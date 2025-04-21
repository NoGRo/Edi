using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using MQTTnet.Implementations;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Edi.Core.Players
{

    public class DevicePlayer : IPlayBack
    {
        public DevicePlayer(DeviceCollector deviceCollector, ConfigurationManager configuration, SyncPlaybackFactory syncFactory)
        {
            this.deviceCollector = deviceCollector;
            this.syncFactory = syncFactory;
            config = configuration.Get<DevicesConfig>();
            deviceCollector.OnloadDevice += DeviceCollector_OnloadDevice;
            channels = new();

            EnsureChannel(MAIN_CHANNEL);
            activeChannels = new List<string> { MAIN_CHANNEL };

        }
        public static string MAIN_CHANNEL = "main";
        private class PlaybackState
        {
            public bool isHardPause { get; set; }
            public SyncPlayback syncPlayback { get; set; }
        }
        private readonly DeviceCollector deviceCollector;
        private readonly SyncPlaybackFactory syncFactory;
        private DevicesConfig config;

        private readonly ConcurrentDictionary<string, PlaybackState> channels;
        public List<string> Channels => channels.Keys.ToList();
        private List<string> activeChannels;

        private void EnsureChannel(string name)
        {
            if (channels.ContainsKey(name)) 
                return;

            channels[name] = new();
        }

        public void UseChannels(params string[] channelNames)
        {
            lock (activeChannels)
            {
                var namesToUse = (channelNames == null || channelNames.Length == 0)
                    ? channels.Keys.ToList()
                    : channelNames.Distinct().ToList();

                if (!channels.ContainsKey(MAIN_CHANNEL) && namesToUse.Count > 0 && namesToUse.First() != MAIN_CHANNEL)
                {
                    // cuando se crea un nuevo canal por primera vez
                    // el main y todos los dispositivos sin canal van a parar al canal nuevo 
                    var status = channels[MAIN_CHANNEL];
                    channels.Clear();

                    var newChannel = namesToUse.First();
                    channels[newChannel] = status;
                    deviceCollector.Devices
                        .Where(d=> string.IsNullOrEmpty(d.Channel))
                        .Select(d => d.Channel = newChannel).ToList();
                }

                foreach (var name in namesToUse)
                EnsureChannel(name);

                activeChannels = namesToUse;
            }
        }
        private IEnumerable<IDevice> Devices
        {
            get
            {
                lock (activeChannels)
                {
                    return activeChannels?.Any() != true
                        ? deviceCollector.Devices
                        : deviceCollector.Devices.Where(d => activeChannels.Contains(d.Channel) || activeChannels.Count == 1 && activeChannels.First() == MAIN_CHANNEL);
                }
            }
        }

        private async void DeviceCollector_OnloadDevice(IDevice device, List<IDevice> devices)
        {
            if (device is not INotifyPropertyChanged notifier) 
                return;
            
            notifier.PropertyChanged += async (sender, args) =>
            {
                if (sender is not IDevice d ||
                    args.PropertyName is not nameof(IDevice.SelectedVariant)
                        and not nameof(IDevice.Channel))
                    return;

                await Sync(d);
            };
            device.Channel = channels.Keys.First();
        }

        private bool isStopState(IDevice device)
        {
            var devicRange = device as IRange;
            return device.SelectedVariant == "None"
                || devicRange != null && devicRange.Min == devicRange.Max;
        }

        public async Task Sync(IDevice device = null, bool atCurrentTime = true)
        {
            var devices = device != null ? new[] { device } : Devices;

            foreach (var d in devices)
            {
                EnsureChannel(d.Channel);
                var state = channels[d.Channel];

                if (!state.isHardPause && state.syncPlayback?.IsFinished == false && !isStopState(d))
                    _ = d.PlayGallery(state.syncPlayback.GalleryName, atCurrentTime ? state.syncPlayback.CurrentTime : state.syncPlayback.Seek);
                else
                    _ = d.Stop();
            }
        }

        public async Task Stop()
        {
            foreach (var channel in activeChannels)
                channels[channel].syncPlayback = null;

            _ = Devices.Select(d => d.Stop()).ToList();
        }

        public async Task Play(string name, long seek = 0)
        {
            foreach (var channel in activeChannels)
                channels[channel].syncPlayback = syncFactory.Create(name, seek);

            _ = Devices.Where(d =>
                    !isStopState(d) &&
                    !channels[d.Channel].isHardPause)
                .Select(d => d.PlayGallery(name, seek)).ToList();
        }

        public async Task Pause(bool untilResume = false)
        {
            foreach (var channel in activeChannels)
            {
                var state = channels[channel];
                state.isHardPause = untilResume;
                if (state.syncPlayback != null)
                    state.syncPlayback = syncFactory.Create(state.syncPlayback.GalleryName, state.syncPlayback.CurrentTime);
            }

            _ = Devices.Select(d => d.Stop()).ToList();
        }

        public async Task Resume(bool atCurrentTime = false)
        {
            foreach (var channel in activeChannels)
                channels[channel].isHardPause = false;

            await Sync(atCurrentTime: atCurrentTime);
        }

        public async Task Intensity(int Max)
        {
            foreach (var d in Devices.Where(d => d is IRange))
            {
                var range = (IRange)d;
                if (config.Devices.TryGetValue(d.Name, out var def) && def is IRange defRange)
                    range.Max = defRange.Min + (defRange.Max - defRange.Min) * Max / 100;
            }
        }
    }
 
}
