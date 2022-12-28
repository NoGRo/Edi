using System;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Configuration;
using Edi.Core.Gallery;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device;
using Timer = System.Timers.Timer;

namespace Edi.Core.Services
{
    public class Edi : IEdi
    {
        private readonly IDeviceManager _deviceManager;
        private readonly IDeviceProvider _providerManager;
        private readonly IGalleryRepository _repository;
        private readonly IConfiguration _configuration;

        public Edi(IDeviceManager deviceManager, IDeviceProvider providerManager, IGalleryRepository repository, IConfiguration configuration)
        {
            _deviceManager = deviceManager;
            _providerManager = providerManager;
            _repository = repository;
            _configuration = configuration;

            TimerGalleryStop = new Timer();
            TimerGalleryStop.Elapsed += TimerGalleryStop_ElapsedAsync;

            TimerReactStop = new Timer();
            TimerReactStop.Elapsed += TimerReactStop_ElapsedAsync;

            Config = new EdiConfig();
            _configuration.GetSection("Edi").Bind(Config);
        }

        private EdiConfig Config { get; set; }
        private string CurrentFiller { get; set; }
        private string LastGallery { get; set; }
        private string LastFiller { get; set; }
        private DateTime? GallerySendTime { get; set; }
        private DateTime? ReactSendTime { get; set; }
        private Timer TimerGalleryStop { get; set; }
        private Timer TimerReactStop { get; set; }

        public async Task Init()
        {
            await _repository.Init();
            await _providerManager.Init(_deviceManager);
        }

        public async Task SetFiller(string name, bool play = false, long seek = 0)
        {
            CurrentFiller = name;
            if (play)
                await SendFiller(CurrentFiller, seek);
        }

        public async Task StopFiller()
        {
            await SendGallery("Off");
        }

        public async Task PlayGallery(string name, bool play = true, long seek = 0)
        {
            if (Config.Gallery)
            {
                await SendGallery(name, seek);
            }
        }

        public async Task PlayReaction(string name)
        {
            if (!Config.Reactive)
                return;

            var gallery = _repository.Get(name);
            ReactSendTime = DateTime.Now;
            if (!gallery.Repeats)
            {
                TimerReactStop.Interval = Math.Abs(gallery.Duration);
                TimerReactStop.Start();
            }

            await _deviceManager.SendGallery(name);
        }

        private async void TimerReactStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
            => await StopReaction();
        public async Task StopReaction()
        {
            if (!ReactSendTime.HasValue || !GallerySendTime.HasValue)
                return;

            var seekBack = Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds);
            await SendGallery(LastGallery, seekBack);
        }

        public async Task StopGallery()
        {
            await SendFiller(CurrentFiller);
        }

        public async Task Pause()
        {
            await _deviceManager.Pause();
        }

        public async Task Resume()
        {
            await _deviceManager.Resume();
        }

        private async Task SendGallery(string name, long seek = 0)
        {
            if (!Config.Gallery || string.IsNullOrEmpty(name))
                return;

            var gallery = _repository.Get(name);

            if (gallery == null)
                return;

            if (seek != 0 && gallery.Duration > 0)
            {
                var seekTime = TimeSpan.FromMilliseconds(seek);
                var galleryTime = TimeSpan.FromMilliseconds(gallery.Duration);

                if (seekTime > galleryTime)
                    seek =  Convert.ToInt16(seekTime.TotalMilliseconds % galleryTime.TotalMilliseconds);
            }

            GallerySendTime = DateTime.Now;
            LastGallery = name;

            if (!gallery.Repeats)
            {
                TimerGalleryStop.Interval = Math.Abs(gallery.Duration);
                TimerGalleryStop.Start();
            }

            await _deviceManager.SendGallery(name, seek);
        }

        private async void TimerGalleryStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
            => await StopGallery();

        private async Task SendFiller(string name, long seek = 0)
        {
            if (!Config.Filler)
                name = "Off";

            await SendGallery(name, seek);
        }
    }
}
