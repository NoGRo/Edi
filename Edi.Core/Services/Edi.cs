using System;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Configuration;
using Edi.Core.Gallery;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device;
using Timer = System.Timers.Timer;
using Edi.Core.Gallery.Definition;

namespace Edi.Core
{
    public class Edi : IEdi
    {
        private readonly IDeviceManager _deviceManager;
        private readonly DefinitionRepository _repository;
        private readonly IConfiguration _configuration;

        public Edi(IDeviceManager deviceManager,  DefinitionRepository repository, IConfiguration configuration)
        {
            _deviceManager = deviceManager;
            _repository = repository;
            _configuration = configuration;

            TimerGalleryStop = new Timer();
            TimerGalleryStop.Elapsed += TimerGalleryStop_ElapsedAsync;

            TimerReactStop = new Timer();
            TimerReactStop.Elapsed += TimerReactStop_ElapsedAsync;

            Config = new EdiConfig();
            _configuration.GetSection(EdiConfig.Seccition).Bind(Config);

        }

        public EdiConfig Config { get; set; }
        private string CurrentFiller { get; set; }
        private DefinitionGallery LastGallery { get; set; }
        private DateTime? GallerySendTime { get; set; }
        private DefinitionGallery? ReactSendGallery { get; set; }
        private Timer TimerGalleryStop { get; set; }
        private Timer TimerReactStop { get; set; }

        public async Task Init()
        {
            await _repository.Init();
            await _deviceManager.Init();
        }

        private async Task SetFiller(DefinitionGallery gallery)
        {
            CurrentFiller = gallery.Name;
            if (LastGallery == null && ReactSendGallery == null)
                await SendFiller(CurrentFiller);
        }

        public async Task Play(string name, long seek = 0)
        {
            var gallery = _repository.Get(name);

            if (gallery == null)
                return;

            switch (gallery.Type)
            {
                case "filler":
                    if (Config.Filler)
                    {
                        await SetFiller(gallery);
                    }
                    break;
                case "gallery":
                    if (Config.Gallery)
                    {
                        await SendGallery(gallery, seek);
                    }
                    break;
                case "reaction":
                    if (Config.Reactive)
                    {
                        await PlayReaction(gallery);
                    }
                    break;
                default:
                    break;
            }

        }

        private async Task PlayReaction(DefinitionGallery gallery)
        {
            ReactSendGallery = gallery;
            if (!gallery.Loop)
            {
                TimerReactStop.Interval = Math.Abs(gallery.Duration);
                TimerReactStop.Start();
            }

            await _deviceManager.PlayGallery(gallery.Name);
        }

        private async void TimerReactStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
            => await StopReaction();
        private async Task StopReaction()
        {
            TimerReactStop.Stop(); 
            if (ReactSendGallery == null)
                return;

            ReactSendGallery = null;

            if (!GallerySendTime.HasValue || LastGallery == null)
                return;

          var seekBack = Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds);
          await SendGallery(LastGallery, seekBack);
        }

        public async Task Stop()
        {
            if(ReactSendGallery != null)
            {
                await StopReaction();
            }
            else
            {
                await SendFiller(CurrentFiller);
            }   
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
            if (string.IsNullOrEmpty(name))
                return;
            await SendGallery( _repository.Get(name), seek);
        }
        private async Task SendGallery(DefinitionGallery gallery, long seek = 0)
        {

            if (gallery == null || gallery.Duration <= 0)
                return;

            ReactSendGallery = null;
            TimerReactStop.Stop();

            // If the seek time is greater than the gallery time And it Repeats, then modulo the seek time by the gallery time to get the correct seek time.
            if (seek != 0 && seek > gallery.Duration) 
            {
                if (gallery.Loop)
                    seek = Convert.ToInt16(seek % gallery.Duration);
                else
                {
                    //seek out of range StopGallery
                    await Stop();
                    return;
                }
            }

            GallerySendTime = DateTime.Now;
            LastGallery = gallery;
            // If the gallery does not repeat, then start a timer to stop the gallery after its duration.
            if (!gallery.Loop)
            {
                TimerGalleryStop.Interval = Math.Abs(gallery.Duration);
                TimerGalleryStop.Start();
            }
            await _deviceManager.PlayGallery(gallery.Name, seek);
        }

        private async void TimerGalleryStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
            => await Stop();

        private async Task SendFiller(string name, long seek = 0)
        {
            if (!Config.Filler || string.IsNullOrEmpty(name))
            {
                await Pause();
                return;
            }



            await SendGallery(name, seek);
        }
    }
}
