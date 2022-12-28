using Edi.Core.Device.Interfaces;
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

namespace Edi.Core.Device.EStim
{

    public class EStimDevice : IDevice
    {
        private readonly IGalleryRepository<EStimGallery> _repository;
        private readonly IWavePlayer _wavePlayer;
        private EStimGallery _currentGallery;

        private Mp3FileReader _curentAudioFile { get; set; }

        private Timer _timerGalleryEnds;
        private bool _isPlaying;

        public EStimDevice(IGalleryRepository<EStimGallery> repository, IWavePlayer wavePlayer)
        {
            _repository = repository;
            _wavePlayer = wavePlayer;
            _timerGalleryEnds = new Timer();
            _timerGalleryEnds.Elapsed += OnTimerElapsed;
            _wavePlayer.PlaybackStopped += OnPlaybackStoppedAsync;
        }

        public async Task SendGallery(string name, long seek = 0)
        {
            // Obtener la galería del repositorio
            var gallery = _repository.Get(name);
            if (gallery == null)
            {
                return;
            }

            // Asignar la galería actual
            _currentGallery = gallery;

            // Abrir el archivo de audio
            _curentAudioFile = new Mp3FileReader(gallery.AudioPath);

            // Saltar hasta la posición deseada
            PlayGallery(gallery, seek);
        }

        private void PlayGallery(EStimGallery gallery, long seek= 0 )
        {
            _curentAudioFile.CurrentTime = TimeSpan.FromMilliseconds(gallery.StartTime + seek);
            _timerGalleryEnds.Interval = gallery.Duration;
            _timerGalleryEnds.Start();

            _wavePlayer.Init(_curentAudioFile);
            _wavePlayer.Play();
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            _wavePlayer.Stop();
        }

        public async Task Pause()
        {
            // Pausar la reproducción del archivo de audio
            _wavePlayer.Pause();
            _timerGalleryEnds.Stop();
        }

        public async Task Resume()
        {
            // Reanudar la reproducción del archivo de audio
            _wavePlayer.Play();
            _timerGalleryEnds.Start();
        }

        private async void OnPlaybackStoppedAsync(object sender, StoppedEventArgs e)
        {
            _timerGalleryEnds.Stop();
            if (_currentGallery.Repeats)
            {
                PlayGallery(_currentGallery);
            }
        }


    }
}
