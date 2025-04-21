using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Players
{
    public class BaseProxyPlayer : IPlayBack
    {
        protected readonly IPlayBack upperLayerPlayer;

        public BaseProxyPlayer(IPlayBack upperLayerPlayer)
        {
            this.upperLayerPlayer = upperLayerPlayer;
        }

        public virtual void UseChannels(params string[] channelNames)
            => upperLayerPlayer.UseChannels(channelNames);

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
