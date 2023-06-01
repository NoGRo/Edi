using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Microsoft.Extensions.DependencyInjection;

namespace Edi.Core.Device
{
    public class DeviceManager : IDeviceManager
    {


        public List<IDevice> Devices { get; private set; } =  new List<IDevice>();    
        public ParallelQuery<IDevice> DevicesParallel => Devices.Where(x => x != null).AsParallel();
        private string? lastGallerySend;

        public DeviceManager(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IEnumerable<IDeviceProvider> Providers { get; set; }
        public IServiceProvider ServiceProvider { get; }

        public async Task Init()
        {
            Providers = ServiceProvider.GetServices<IDeviceProvider>();
            Providers.AsParallel().ForAll(async x => await x.Init());
        }


        public void LoadDevice(IDevice device)
        {
            Devices.Add(device); 
            if (lastGallerySend != null)
                device.PlayGallery(lastGallerySend);
            
        }
        public void UnloadDevice(IDevice device)
        {
            Devices.Remove(device);
        }

        public async Task Pause()
        {
            DevicesParallel.ForAll(async x => await x.Pause());
        }

        public async Task Resume()
        {
            DevicesParallel.ForAll(async x => await x.Resume());
        }

        public async Task PlayGallery(string name, long seek = 0)
        {
            lastGallerySend = name;
            DevicesParallel.ForAll(async x => await x.PlayGallery(name, seek));
        }


    }
}
