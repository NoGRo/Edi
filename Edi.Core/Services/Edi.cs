using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using System;
using System.Timers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;
using Microsoft.Extensions.Configuration;

namespace Edi.Core.Services
{
    public class Edi : iEdi
    {
        private readonly IDeviceManager deviceManager;
        private readonly ProviderManager providerManager;
        private readonly IGalleryRepository repository;
        private readonly IConfiguration configuration;

        public Edi(IDeviceManager deviceManager, ProviderManager providerManager, IGalleryRepository repository, IConfiguration configuration)
        {
            this.deviceManager = deviceManager;
            this.providerManager = providerManager;
            this.repository = repository;
            this.configuration = configuration;
            TimerGalleryStop = new Timer();
            TimerGalleryStop.Elapsed += TimerGalleryStop_ElapsedAsync; ;

            config = new EdiConfig();
            configuration.GetSection("Edi").Bind(config);

        }

        private EdiConfig config;
        private string filler;
        private Timer TimerGalleryStop { get; set; }
        public async Task Init()
        {
            await repository.Init();
            await providerManager.Init(deviceManager); 
        }
        public async Task Filler(string name, bool play = false, long seek = 0) 
        {
            filler = name;
            if (play)
                await SendFiller(filler, seek);
        }

        public async Task Play(string name, long seek)
        {
            await SendGallery(name, seek);
        }
        public async Task Pause()
        {
            await deviceManager.Pause();
        }
        public async Task Resume()
        {
            await deviceManager.Resume();
        }
        public async Task Stop()
        {
            await SendFiller(filler);
        }
        private async Task SendFiller(string Name, long seek = 0)
        {
            if (!config.Filler)
                Name = "FillerOff";

            await SendGallery(Name, seek);
        }
            
        private async Task SendGallery(string Name, long Seek = 0)
        {
            if (!config.Gallery)
                return;

            TimerGalleryStop.Stop();
            var task = deviceManager.SendGallery(Name, Seek);
            var gallery = repository.Get(Name);
            if (!gallery.Repeats)
            {
                TimerGalleryStop.Interval = Math.Abs(gallery.Duration - Seek);
                TimerGalleryStop.Start();
            }
            await task;
        }

        private async void TimerGalleryStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
        {
            await Stop();
        }

    }
}
