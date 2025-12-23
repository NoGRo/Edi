using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NAudio.CoreAudioApi;
using PropertyChanged;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Edi.Core.Players
{
    [AddINotifyPropertyChangedInterface]
    public class MultiChannelPlayer : ProxyPlayer, IPlayerChannels
    {
        private readonly ChannelManager<IPlayer> Manager;

        public MultiChannelPlayer(IServiceProvider serviceProvider, ChannelManager<IPlayer> channelManager, DeviceCollector deviceCollector)
            : base(null)
        {
            this.Manager = channelManager;
            Manager.OnFirstCustomChannelCreated += Manager_OnFirstCustomChannelCreated;
            deviceCollector.OnloadDevice += DeviceCollector_OnloadDevice;
            deviceCollector.OnUnloadDevice += DeviceCollector_OnUnloadDevice;
            // Redirige el evento del manager al evento expuesto por la interfaz
            Manager.ChannelsChanged += (channels) => ChannelsChanged?.Invoke(channels);
        }

        // Implementación del evento de la interfaz
        public event Action<List<string>> ChannelsChanged;

        private void Manager_OnFirstCustomChannelCreated(string newChannel)
            => deviceChannel.Keys.ToList().ForEach(d => d.Channel = newChannel);
            
        private Dictionary<IDevice, string> deviceChannel = new();

        public List<string> Channels => Manager.Channels; 

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

        public void ResetChannels( List<string> channels = null)
        {
            Manager.Reset();

            foreach (var device in deviceChannel.Keys)
            {
                Manager.WithChannel(null, c => c.Add(device));
            }
            deviceChannel.Clear();
            
            Manager.UseChannels(channels?.ToArray());


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
