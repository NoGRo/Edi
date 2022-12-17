using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Services
{
    public class Edi : iEdi
    {
        private readonly IDeviceManager deviceManager;
        private readonly ProviderManager providerManager;
        private readonly IGalleryRepository repository;

        public Edi(IDeviceManager deviceManager, ProviderManager providerManager, IGalleryRepository repository)
        {
            this.deviceManager = deviceManager;
            this.providerManager = providerManager;
            this.repository = repository;
        }

        
        public async Task Init()
        {

            await repository.Init();
            await providerManager.Init(deviceManager); 
        }
        public async Task Play(string Name, long Seek)
        {
            await deviceManager.SendGallery(Name);
        }
        public async Task Pause()
        {
            await deviceManager.Pause();
        }
        public async Task Resume()
        {
            await deviceManager.Resume();
        }


    }
}
