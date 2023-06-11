using System;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Configuration;
using Edi.Core.Gallery;
using Edi.Core.Device.Interfaces;
using Timer = System.Timers.Timer;
using Edi.Core.Gallery.Definition;
using NAudio.Wave.SampleProviders;

namespace Edi.Core
{
    public class Edi : IEdi
    {
        public IDeviceManager DeviceManager { get; private set; }
        private readonly DefinitionRepository _repository;
        private readonly IEnumerable<IRepository> repos;
        public static string OutputDir => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/Edi";

        private readonly IConfiguration _configuration;
        public event IEdi.ChangeStatusHandler OnChangeStatus;


        public IEnumerable<IDevice> Devices => DeviceManager.Devices;
        public Edi(IDeviceManager deviceManager, DefinitionRepository repository, IEnumerable<IRepository> repos, IConfiguration configuration)
        {
            if (!Directory.Exists(OutputDir))  
                Directory.CreateDirectory(OutputDir);
                
            DeviceManager = deviceManager;
            _repository = repository;
            this.repos = repos;
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

        public IEnumerable<DefinitionGallery> Definitions => _repository.GetAll();

        public async Task Init()
        {
            //await _repository.Init();
            foreach (var repo in repos)
            {
                await repo.Init();
            }

            await DeviceManager.Init();
        }

        private void changeStatus(string message)
        {
            if (OnChangeStatus is null) return;
            OnChangeStatus($"[{DateTime.Now.ToShortTimeString()}] {message}");
        }


        public async Task Play(string name, long seek = 0)
        {

            var gallery = _repository.Get(name);

            if (gallery == null)
            {
                changeStatus($"Ignored not found [{name}]");
                return;
            }
            changeStatus($"recived [{name}] {gallery.Type}");

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
            changeStatus($"Device Reaction [{gallery.Name}], loop:{gallery.Loop}");
            await DeviceManager.PlayGallery(gallery.Name);
        }
        private async void TimerReactStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
            => await StopReaction();
        private async Task StopReaction()
        {
            TimerReactStop.Stop();
            if (ReactSendGallery == null)
                return;

            ReactSendGallery = null;

            changeStatus($"Stop Reaction");

            if (LastGallery != null)
            {
                var seekBack = Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds);
                await SendGallery(LastGallery, seekBack);

            }
            else if (CurrentFiller != null)
            {
                await SendFiller(CurrentFiller);
            }
            else 
            {
                await Pause();
            }






        }


        public async Task Stop()
        {
            if (ReactSendGallery != null)
            {
                changeStatus("Stop Reaction");
                await StopReaction();

            }
            else
            {
                changeStatus("Stop Galley");
                await SendFiller(CurrentFiller);
            }
        }

        private async Task SetFiller(DefinitionGallery gallery)
        {
            CurrentFiller = gallery.Name;
            if ((LastGallery == null && ReactSendGallery == null)
                || (LastGallery?.Type == "filler" && LastGallery.Name != CurrentFiller))
                await SendFiller(CurrentFiller);
        }
        private async Task SendFiller(string name, long seek = 0)
        {
            if (!Config.Filler || string.IsNullOrEmpty(name))
            {
                LastGallery = null;
                await Pause();
                return;
            }

            await SendGallery(name, seek);
        }



        public async Task Pause()
        {
            changeStatus("Device Pause");
            await DeviceManager.Pause();
        }

        public async Task Resume()
        {
            changeStatus("Device Resume");
            await DeviceManager.Resume();
        }
        private async Task SendGallery(string name, long seek = 0)
        {
            if (string.IsNullOrEmpty(name))
                return;
            await SendGallery(_repository.Get(name), seek);
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
            changeStatus($"Device Play [{gallery.Name}] at {seek}, loop:[{gallery.Loop}]");
            await DeviceManager.PlayGallery(gallery.Name, seek);
        }

        private async void TimerGalleryStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
        {
            TimerGalleryStop.Stop();
            await Stop();
        }
    



    }
}
