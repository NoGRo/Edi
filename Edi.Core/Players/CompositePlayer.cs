using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Edi.Core.Players
{
    public class CompositePlayer : ProxyPlayer
    {
        private readonly List<IPlayBack> players = new();

        public CompositePlayer() : base(null) { }

        public  void Add(IPlayBack player) => players.Add(player);

        public void Remove(IPlayBack player) => players.Add(player);


        public override void Add(IDevice device)
            => players.ForEach(p => p.Add(device));

        public override void Remove(IDevice device)
            => players.ForEach(p => p.Remove(device));


        public override Task Play(string name, long seek = 0)
            => Task.WhenAll(players.Select(p => p.Play(name, seek)));

        public override Task Stop()
            => Task.WhenAll(players.Select(p => p.Stop()));

        public override Task Pause(bool untilResume = false)
            => Task.WhenAll(players.Select(p => p.Pause(untilResume)));

        public override Task Resume(bool atCurrentTime = false)
            => Task.WhenAll(players.Select(p => p.Resume(atCurrentTime)));

        public override Task Intensity(int Max)
            => Task.WhenAll(players.Select(p => p.Intensity(Max)));
    }
}
