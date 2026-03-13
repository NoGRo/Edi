using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Edi.Core.Players.Strategies
{
    public class CompositePlayer : IPlayerChannels
    {
        private readonly List<IPlayerChannels> playerChannels = new();

        public List<string> Channels { get; private set; } = new();

        public event Action<List<string>> ChannelsChanged;

        public void AddPlayerChannel(IPlayerChannels playerChannel)
        {
            playerChannels.Add(playerChannel);
        }

        public void RemovePlayerChannel(IPlayerChannels playerChannel)
        {
            playerChannels.Remove(playerChannel);
        }

        public void ResetChannels(List<string> channels = null)
        {
            Channels = channels ?? new List<string>();
            ChannelsChanged?.Invoke(Channels);

            foreach (var channel in playerChannels)
            {
                channel.ResetChannels(channels);
            }
        }

        public async Task Play(string name, long seek = 0, string[] channels = null)
        {
            foreach (var channel in playerChannels)
            {
                await channel.Play(name, seek, channels);
            }
        }

        public async Task Stop(string[] channels = null)
        {
            foreach (var channel in playerChannels)
            {
                await channel.Stop(channels);
            }
        }

        public async Task Pause(bool untilResume = false, string[] channels = null)
        {
            foreach (var channel in playerChannels)
            {
                await channel.Pause(untilResume, channels);
            }
        }

        public async Task Resume(bool atCurrentTime = false, string[] channels = null)
        {
            foreach (var channel in playerChannels)
            {
                await channel.Resume(atCurrentTime, channels);
            }
        }

        public async Task Intensity(int max, string[] channels = null)
        {
            foreach (var channel in playerChannels)
            {
                await channel.Intensity(max, channels);
            }
        }
    }
}