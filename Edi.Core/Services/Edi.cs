using System;
using System.Threading.Tasks;
using System.Timers;
using Edi.Core.Gallery;
using Edi.Core.Device.Interfaces;
using Timer = System.Timers.Timer;
using Edi.Core.Gallery.Definition;
using NAudio.Wave.SampleProviders;
using CsvHelper;
using CsvHelper.Configuration;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Funscript;

namespace Edi.Core
{
    public class Edi :  IEdi
    {
        public  ConfigurationManager ConfigurationManager { get; set; }
        public DeviceManager DeviceManager { get; private set; }
        private readonly DefinitionRepository _repository;
        private readonly IEnumerable<IRepository> repos;
        private long resumePauseAt;
        private long seekTime;

        public static string OutputDir => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/Edi";

        public event IEdi.ChangeStatusHandler OnChangeStatus;


        public IEnumerable<IDevice> Devices => DeviceManager.Devices;
        public Edi(DeviceManager deviceManager, DefinitionRepository repository, IEnumerable<IRepository> repos, ConfigurationManager configuration)
        {
            if (!Directory.Exists(OutputDir))  
                Directory.CreateDirectory(OutputDir);
                
            DeviceManager = deviceManager;

            deviceManager.OnloadDevice += DeviceManager_OnloadDevice;

            _repository = repository;
            this.repos = repos;
            

            TimerGalleryStop = new Timer();
            TimerGalleryStop.Elapsed += TimerGalleryStop_ElapsedAsync;

            TimerReactStop = new Timer();
            TimerReactStop.Elapsed += TimerReactStop_ElapsedAsync;
            ConfigurationManager = configuration;
            Config = configuration.Get<EdiConfig>();

        }

        private void DeviceManager_OnloadDevice(IDevice device)
        {
            if (LastGallery == null || GallerySendTime == null)
                return;

            var seek = Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds) + seekTime;
            seek = Convert.ToInt64(seek % LastGallery.Duration);

            device.PlayGallery(LastGallery.Name, seek);
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
                var seekBack = Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds + resumePauseAt);
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
                await StopReaction();
            else if (LastGallery?.Type == "gallery")
                await StopGallery();
        }

        private async Task StopGallery()
        {
            LastGallery = null;
            await SendFiller(CurrentFiller);
        }

        private async Task SetFiller(DefinitionGallery gallery)
        {
            CurrentFiller = gallery.Name;
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

            await DeviceManager.Stop();

            if (GallerySendTime is null || LastGallery is null)
            {
                resumePauseAt = -1;
                return;
            }
            
            resumePauseAt += Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds);

            if (resumePauseAt >= LastGallery.Duration && !LastGallery.Loop)
                resumePauseAt = -1;
        }

        public async Task Resume(bool atCurrentTime = false)
        {
            //changeStatus("Device Resume");
             if (resumePauseAt >= 0)
            {
                if(atCurrentTime)
                {
                    var timeFromSent = Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds) + seekTime;
                    await SendGallery(LastGallery, timeFromSent);
                }
                else
                    await SendGallery(LastGallery, resumePauseAt);
            }
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
            TimerGalleryStop.Stop();
            // If the seek time is greater than the gallery time And it Repeats, then modulo the seek time by the gallery time to get the correct seek time.
            if (seek != 0 && seek > gallery.Duration)
            {
                if (gallery.Loop)
                    seek = Convert.ToInt64(seek % gallery.Duration);
                else
                {
                    //seek out of range StopGallery
                    await Stop();
                    return;
                }
            }

            GallerySendTime = DateTime.Now;
            LastGallery = gallery;
            resumePauseAt = seek;
            seekTime = seek;
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



        public Trepo? GetRepo<Trepo>() where Trepo : class, IRepository 
            => repos?.FirstOrDefault(x => x is Trepo) as Trepo;

        public async Task Repack()
        {
            await new Repacker(this).Repack();
        }
    }
}
