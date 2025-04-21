using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Players
{
    internal class MultiRfgPlayer : BaseProxyPlayer
    {
        public MultiRfgPlayer(IPlayBack upperLayerPlayer, IServiceProvider serviceProvider) : base(upperLayerPlayer)
        {
            EnsureChannel(DevicePlayer.MAIN_CHANNEL);
            this.serviceProvider = serviceProvider;
            activeChannels = new() { DevicePlayer.MAIN_CHANNEL };
        }
        private readonly ConcurrentDictionary<string, IPlayBack> channels = new();
        private readonly IServiceProvider serviceProvider;
        private List<string> activeChannels;

        private void EnsureChannel(string name)
        {
            if (channels.ContainsKey(name))
                return;

            channels[name] = serviceProvider.GetRequiredService<RfgPlayer>();
        }
        public override void UseChannels(params string[] channelNames)
        {
            lock (activeChannels)
            {
                var namesToUse = (channelNames == null || channelNames.Length == 0)
                    ? channels.Keys.ToList()
                    : channelNames.Distinct().ToList();

                if (!channels.ContainsKey(DevicePlayer.MAIN_CHANNEL) && namesToUse.Count > 0 && namesToUse.First() != DevicePlayer.MAIN_CHANNEL)
                {
                    // cuando se crea un nuevo canal por primera vez
                    // el main y todos los dispositivos sin canal van a parar al canal nuevo 
                    var rfg = channels[DevicePlayer.MAIN_CHANNEL];
                    channels.Clear();

                    var newChannel = namesToUse.First();
                    channels[newChannel] = rfg;
                }
                foreach (var channel in namesToUse)
                {
                    EnsureChannel(channel);
                }

                activeChannels = namesToUse;
                base.UseChannels(channelNames);
            }
        }

        private IEnumerable<IPlayBack> activePlayers
        {
            get
            {
                lock (activeChannels)
                {
                    return activeChannels?.Any() != true
                        ? channels.Values
                        : channels.Where(c => activeChannels.Contains(c.Key)
                                            || activeChannels.Count == 1 && activeChannels.First() == DevicePlayer.MAIN_CHANNEL)
                                .Select(x => x.Value);
                }
            }
        }

        public override Task Play(string name, long seek = 0)
            => Task.WhenAll(activePlayers.Select(c => c.Play(name, seek)));

        public override Task Stop()
            => Task.WhenAll(activePlayers.Select(c=> c.Stop()));

        public override Task Pause(bool untilResume = false)
            => Task.WhenAll(activePlayers.Select(c => c.Pause(untilResume)));

        public override Task Resume(bool atCurrentTime = false)
            => Task.WhenAll(activePlayers.Select(c => c.Resume(atCurrentTime)));

        public override Task Intensity(int Max)
            => Task.WhenAll(activePlayers.Select(c => c.Intensity(Max)));



    }
}
