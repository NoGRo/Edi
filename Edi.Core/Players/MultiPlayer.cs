using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NAudio.CoreAudioApi;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Edi.Core.Players
{
    public class MultiPlayer : ProxyPlayer, IPlayBackChannels
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ChannelManager<IPlayBack> Manager;
        private readonly DeviceCollector deviceCollector;

        public MultiPlayer(IServiceProvider serviceProvider, ChannelManager<IPlayBack> channelManager, DeviceCollector deviceCollector)
            : base(null)
        {
            this.serviceProvider = serviceProvider;
            this.Manager = channelManager;
            this.deviceCollector = deviceCollector;
            Manager.OnFirstCustomChannelCreated += Manager_OnFirstCustomChannelCreated;
            deviceCollector.OnloadDevice += DeviceCollector_OnloadDevice;
            deviceCollector.OnUnloadDevice += DeviceCollector_OnUnloadDevice;
        }

        private void Manager_OnFirstCustomChannelCreated(string obj)
            => deviceChannel.Keys.ToList().ForEach(d => d.Channel = obj);//TODO: algo mal aca dispositivos ya configurados en un cannal
            
        private Dictionary<IDevice, string> deviceChannel = new();

        private void DeviceCollector_OnloadDevice(IDevice device, List<IDevice> devices)
        {
            if (device is not INotifyPropertyChanged notifier)
                throw new Exception($"Device: '{device}' Don't has INotifyPropertyChanged Attribute");

            Manager.WithChannel(device.Channel, c => c.Add(device));
            deviceChannel.Add(device, device.Channel);

            notifier.PropertyChanged +=  (sender, args) =>
            {
                if (sender is not IDevice d || args.PropertyName is not nameof(IDevice.Channel))
                    return;

                var previousChannel = deviceChannel.TryGetValue(device, out var last) ? last : null;
                Manager.WithChannel(previousChannel, c => c.Remove(device));

                deviceChannel[device] = device.Channel;
                Manager.WithChannel(d.Channel, c => c.Add(device));
            };
            
        }

        private void DeviceCollector_OnUnloadDevice(IDevice device, List<IDevice> devices)
        {
            Manager.WithChannel(device.Channel, c => c.Remove(device));
            deviceChannel.Remove(device);
        }


        public Task Play(string name, long seek = 0, string[] channels = null)
            => Manager.WithChannels(channels,c => c.Play(name, seek));

        public Task Stop(string[] channels   = null)
            => Manager.WithChannels(channels, c => c.Stop());

        public Task Pause(bool untilResume = false, string[] channels = null)
            => Manager.WithChannels(channels, c => c.Pause(untilResume));

        public Task Resume(bool atCurrentTime = false, string[] channels = null)
            => Manager.WithChannels(channels, c => c.Resume(atCurrentTime));

        public Task Intensity(int Max, string[] channels = null)
            => Manager.WithChannels(channels, c => c.Intensity(Max));
    }
}
