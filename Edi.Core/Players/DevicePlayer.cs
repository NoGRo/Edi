using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace Edi.Core.Players
{
    public class DevicePlayer : IPlayBack
    {
        private readonly SyncPlaybackFactory syncFactory;
        private readonly DevicesConfig config;
        public DevicePlayer(
            SyncPlaybackFactory syncFactory,
            ConfigurationManager configuration)
        {
            this.syncFactory = syncFactory;
            config = configuration.Get<DevicesConfig>();

        }

        private List<IDevice> Devices = new();
        private bool isHardPause;
        private bool isPause;
        private SyncPlayback syncPlayback;

        public void Add(IDevice device)
        {
            Devices.Add(device);
            _ = Sync(device);

            if (device is not INotifyPropertyChanged notifier)
                return;
            
            notifier.PropertyChanged += async (sender, args) =>
            {
                if (sender is not IDevice d 
                        || args.PropertyName is not nameof(IDevice.SelectedVariant) 
                                             and not nameof(IDevice.IsReady)
                                            // and not nameof(IRange.Max)
                        || !Devices.Contains(d))
                    return;

                await Sync(d);
            };

        }
        public void Remove(IDevice device)
        {
            Devices.Remove(device);
        }

        private bool isStopState(IDevice device)
        {
            return device.SelectedVariant == "None"
                || device is IRange r && r.Min == r.Max;
        }


        public async Task Sync(IDevice device = null, bool atCurrentTime = true)
        {
            var targets = device != null ? new List<IDevice> { device } : Devices;
            foreach (var d in targets.Where(x=> x.IsReady))
            {
                if (!isHardPause && !isPause && syncPlayback?.IsFinished == false && !isStopState(d))
                    _ = d.PlayGallery(syncPlayback.GalleryName, atCurrentTime ? syncPlayback.CurrentTime : syncPlayback.Seek);
                else
                    _ = d.Stop();
            }
        }

        public async Task Stop()
        {
            syncPlayback = null;

            _ = Devices.Select(d => d.Stop()).ToList();
        }

        public async Task Play(string name, long seek = 0)
        {
            syncPlayback = syncFactory.Create(name, seek);
            if (isHardPause)
                return;

            isPause = false;
            _ = Devices.Where(d =>
                    !isStopState(d))
                .Select(d => d.PlayGallery(name, seek)).ToList();
        }

        public async Task Pause(bool untilResume = false)
        {
            isHardPause = untilResume;
            isPause = true;
            if (syncPlayback != null)
                syncPlayback = syncFactory.Create(syncPlayback.GalleryName, syncPlayback.CurrentTime);


            _ = Devices.Select(d => d.Stop()).ToList();
        }

        public async Task Resume(bool atCurrentTime = false)
        {
            isHardPause = false;
            isPause = false;
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
