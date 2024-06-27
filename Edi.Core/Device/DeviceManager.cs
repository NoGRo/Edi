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

namespace Edi.Core
{

    [AddINotifyPropertyChangedInterface]
    public class DeviceManager
    {
        public List<IDevice> Devices { get; set; } =  new List<IDevice>();    
        private  ParallelQuery<IDevice> DevicesParallel => Devices.Where(x => x != null).AsParallel();
        private string? lastGallerySend;

        public delegate void OnUnloadDeviceHandler(IDevice device, List<IDevice> devices);
        public delegate void OnloadDeviceHandler(IDevice device, List<IDevice> devices);
        public event OnUnloadDeviceHandler OnUnloadDevice;
        public event OnloadDeviceHandler OnloadDevice;

        [ActivatorUtilitiesConstructor]
        public DeviceManager(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public DeviceManager(ConfigurationManager configuration ) 
        {
            Config = configuration.Get<DevicesConfig>();
            this.configuration = configuration;
        }

        private DevicesConfig Config;
        private readonly ConfigurationManager configuration;

        public List<IDeviceProvider> Providers { get; set; } =  new List<IDeviceProvider>();

        private IServiceProvider ServiceProvider { get; }
        
        public async Task Init()
        {
            if (!Providers.Any() && ServiceProvider != null)
                Providers.AddRange(ServiceProvider.GetServices<IDeviceProvider>());

            Providers.AsParallel().ForAll(async x => await x.Init());
        }

        public async Task SelectVariant(IDevice device, string variant)
        {
            var deviceName = Devices.FirstOrDefault(x  => x == device)?.Name;

            if (device is null || deviceName is null)
                return;
            if (!Config.Devices.ContainsKey(deviceName))
                Config.Devices.Add(deviceName, new() { Variant = variant});
            else
            {
                if (lastGallerySend == null && device.IsReady)
                    await device.Stop();

                Config.Devices[deviceName].Variant = variant;
            }


            if (device.SelectedVariant != variant)
                device.SelectedVariant = variant;

            configuration.Save(Config);
        }

        public async Task SelectRange(IDevice device, IRange range)
        {
            var deviceName = Devices.FirstOrDefault(x => x == device)?.Name;

            if (device is null || deviceName is null)
                return;
            if (!Config.Devices.ContainsKey(deviceName))
                Config.Devices.Add(deviceName, new DeviceConfig() { });

            Config.Devices[deviceName].SetRange(range);
            

            (device as IRange)?.SetRange(range);

            configuration.Save(Config);
        }

        public void Intensity(int Max)
        {
            DevicesParallel.ForAll(async x =>
            {
                if (x is not IRange)
                    return;
              
                
            });
        }
        
        public async void LoadDevice(IDevice device)
        {
            lock (Devices)
            {
                UniqueName(device);
                Devices.Add(device);
            }

            string variant = "";
            if (Config.Devices.ContainsKey(device.Name))
            {
                variant = Config.Devices[device.Name].Variant;

                variant = device.Variants.Contains(variant)
                                        ? variant
                                        : device.ResolveDefaultVariant();

                Config.Devices[device.Name].Variant = variant;
            }
            else
            {
                variant = device.ResolveDefaultVariant();
                Config.Devices.Add(device.Name, new DeviceConfig() {Variant = variant });
            }

            device.SelectedVariant = variant;

            configuration.Save(Config);

            if (OnloadDevice != null)
                OnloadDevice(device, Devices);
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

        public async Task UnloadDevice(IDevice device)
        {

            lock (Devices)
            {
                Devices.RemoveAll(x => x.Name == device.Name);

            }
            if (OnUnloadDevice != null)
                OnUnloadDevice(device, Devices);

          
        }

        public async Task Stop()
        {
            lastGallerySend = null;
            DevicesParallel.ForAll(async x => await x.Stop());
        }

        public async Task PlayGallery(string name, long seek = 0)
        {
            lastGallerySend = name;
            DevicesParallel.ForAll(async x => await x.PlayGallery(name, seek));
        }


    }
}
