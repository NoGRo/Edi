using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Gallery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;
using System.Threading;
using PropertyChanged;
using System.Xml.Linq;
using Serilog.Core;
using Microsoft.Extensions.Logging;
using Edi.Core.Device;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Components;
using SoundFlow.Providers;

namespace Edi.Core.Device.EStim
{

    [AddINotifyPropertyChangedInterface]
    public class EStimDevice : DeviceBase<AudioRepository, AudioGallery>
    {
        private readonly AudioRepository _repository;
        private readonly AudioPlaybackDevice playbackDevice;
        private SoundPlayer soundPlayer = null;

        private Dictionary<string, MemoryStream> _inMemoryMp3;
        public EStimDevice(AudioRepository repository, AudioPlaybackDevice playbackDevice, ILogger _logger) : base(repository, _logger)
        {
            Name = $"SEstim ({playbackDevice.Info?.Id})";
            _repository = repository;
            this.playbackDevice = playbackDevice;

            _inMemoryMp3 = _repository.GetAll().Select(x => x.AudioPath).Distinct().ToDictionary(x => x, y =>
            {
                var memoryStream = new MemoryStream();
                using var fs = File.OpenRead(y);
                fs.CopyTo(memoryStream);
                return memoryStream;
            });

        }
        internal override Task applyRange()
        {
            playbackDevice.MasterMixer.Volume = Max / 100.0f;
            return Task.CompletedTask;
        }

        public override async Task PlayGallery(AudioGallery gallery, long seek = 0)
        {
            if (soundPlayer != null)
            {
                soundPlayer.Stop();
                playbackDevice.MasterMixer.RemoveComponent(soundPlayer);
                playbackDevice.Stop();
                soundPlayer.Dispose();
            }

            soundPlayer = new SoundPlayer(
                playbackDevice.Engine,
                playbackDevice.Format,
                new AssetDataProvider(playbackDevice.Engine, playbackDevice.Format, _inMemoryMp3[gallery.AudioPath]));

            playbackDevice.Start();
            playbackDevice.MasterMixer.AddComponent(soundPlayer);
            soundPlayer.Seek(TimeSpan.FromMilliseconds(gallery.StartTime + seek));
            soundPlayer.Play();
        }

        public override async Task StopGallery()
        {
            soundPlayer.Stop();
            playbackDevice.MasterMixer.RemoveComponent(soundPlayer);
            soundPlayer.Dispose();
            playbackDevice.Stop();
        }
    }
}
