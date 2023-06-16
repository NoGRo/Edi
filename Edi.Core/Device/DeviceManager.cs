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
using NAudio.CoreAudioApi;
using PropertyChanged;

namespace Edi.Core.Device
{
    
    public class DeviceManager : IDeviceManager
    {
        public List<IDevice> Devices { get; private set; } =  new List<IDevice>();    
        private  ParallelQuery<IDevice> DevicesParallel => Devices.Where(x => x != null && x.IsReady).AsParallel();
        private string? lastGallerySend;

        public event IDeviceManager.OnUnloadDeviceHandler OnUnloadDevice;
        public event IDeviceManager.OnloadDeviceHandler OnloadDevice;

        [ActivatorUtilitiesConstructor]
        public DeviceManager(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public DeviceManager(ConfigurationManager configuration ) 
        {
            Config = configuration.Get<DevicesConfig>();
        }

        private DevicesConfig Config;
        public List<IDeviceProvider> Providers { get; set; } =  new List<IDeviceProvider>();

        private IServiceProvider ServiceProvider { get; }
        

        public IEnumerable<string> Variants => throw new NotImplementedException();

        public async Task Init()
        {
            if (!Providers.Any() && ServiceProvider != null)
                Providers.AddRange(ServiceProvider.GetServices<IDeviceProvider>());

            Providers.AsParallel().ForAll(async x => await x.Init());
        }

        public void SelectVariant(string deviceName, string variant)
        {
            var device = Devices.FirstOrDefault(x  => x.Name == deviceName);

            if (Config.DeviceVariant.ContainsKey(deviceName))
            {
                device.SelectedVariant = variant;
                Config.DeviceVariant[deviceName] = variant; ;
            }
            else
                Config.DeviceVariant.Add(deviceName, variant);
        }
        
        public async void LoadDevice(IDevice device)
        {
            UniqueName(device);

            Devices.Add(device);

            if(Config.DeviceVariant.ContainsKey(device.Name))
                device.SelectedVariant = Config.DeviceVariant[device.Name]; 
            else
                Config.DeviceVariant.Add(device.Name, device.SelectedVariant);

            if (OnloadDevice != null)
                OnloadDevice(device);

           
            if (lastGallerySend != null)
                await device.PlayGallery(lastGallerySend);

        }

        private void UniqueName(IDevice device)
        {
            var c = 0;
            var NewName = device.Name;
            while (Devices.Any(x=> x.Name == NewName))
            {
                c++;
                NewName = $"{device.Name} ({c})";
            }
            device.Name = NewName;
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
