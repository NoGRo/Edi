using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Gallery;
using NAudio.Wave;
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

namespace Edi.Core.Device.EStim
{

    [AddINotifyPropertyChangedInterface]
    public class EStimDevice : DeviceBase<AudioRepository, AudioGallery>
    {
        private readonly AudioRepository _repository;
        private readonly IWavePlayer _wavePlayer;
        private Mp3FileReader _curentAudioFile { get; set; }

        private Dictionary<string, Mp3FileReader> _inMemoryMp3;
        public EStimDevice(AudioRepository repository, WaveOutEvent wavePlayer, ILogger _logger) : base(repository, _logger)
        {
            Name = $"SEstim ({wavePlayer.DeviceNumber})";
            _repository = repository;
            _wavePlayer = wavePlayer;

            _inMemoryMp3 = _repository.GetAll().Select(x => x.AudioPath).Distinct().ToDictionary(x => x, y => new Mp3FileReader(y));

        }
        internal override Task applyRange()
        {
            _wavePlayer.Volume = Max / 100;
            return Task.CompletedTask;
        }

        public override async Task PlayGallery(AudioGallery gallery, long seek = 0)
        {
            _curentAudioFile = _inMemoryMp3[gallery.AudioPath];
            _curentAudioFile.CurrentTime = TimeSpan.FromMilliseconds(gallery.StartTime + seek);

            _wavePlayer.Stop();
            _wavePlayer.Init(_curentAudioFile);
            _wavePlayer.Play();
        }

        public override async Task StopGallery()
        {
            _wavePlayer.Pause();
        }
    }
}
