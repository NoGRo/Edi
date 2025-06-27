using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Players
{
    public abstract class ProxyPlayer : IPlayer
    {
        protected readonly IPlayer upperLayerPlayer;

        public ProxyPlayer(IPlayer upperLayerPlayer)
        {
            this.upperLayerPlayer = upperLayerPlayer;
        }
        public virtual void Add(IDevice device)
            => upperLayerPlayer.Add(device);

        public virtual void Remove(IDevice device)
            => upperLayerPlayer.Remove(device);


        public virtual Task Play(string name, long seek = 0)
            => upperLayerPlayer.Play(name, seek);

        public virtual Task Stop()
            => upperLayerPlayer.Stop();

        public virtual Task Pause(bool untilResume = false)
            => upperLayerPlayer.Pause(untilResume);

        public virtual Task Resume(bool atCurrentTime = false)
            => upperLayerPlayer.Resume(atCurrentTime);

        public virtual Task Intensity(int Max)
            => upperLayerPlayer.Intensity(Max);


    }
}
