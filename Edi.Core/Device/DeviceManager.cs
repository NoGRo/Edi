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

        public event IDeviceManager.OnUnloadDeviceHandler OnUnloadDevice;
        public event IDeviceManager.OnloadDeviceHandler OnloadDevice;

        [ActivatorUtilitiesConstructor]
        public DeviceManager(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public DeviceManager() { }

        public List<IDeviceProvider> Providers { get; set; } =  new List<IDeviceProvider>();

        public IServiceProvider ServiceProvider { get; }
        public string SelectedVariant { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IEnumerable<string> Variants => throw new NotImplementedException();

        public async Task Init()
        {
            if (!Providers.Any() && ServiceProvider != null)
                Providers.AddRange(ServiceProvider.GetServices<IDeviceProvider>());

            Providers.AsParallel().ForAll(async x => await x.Init());
        }


        public async void LoadDevice(IDevice device)
        {
            Devices.Add(device);

            if(OnloadDevice!= null)
                OnloadDevice(device);
            if (lastGallerySend != null)
                await device.PlayGallery(lastGallerySend);
            
        }
        public async void UnloadDevice(IDevice device)
        {

            Devices.RemoveAll(x => x.Name == device.Name);
            if (OnUnloadDevice != null)
                OnUnloadDevice(device);
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
