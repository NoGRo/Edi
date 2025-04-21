using Edi.Core.Device.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropertyChanged;
using System;
using System.Threading.Channels;

namespace Edi.Core.Device
{

    [AddINotifyPropertyChangedInterface]
    public class DeviceCollector(ConfigurationManager configuration, IServiceProvider serviceProvider)
    {
        public List<IDeviceProvider> Providers { get; set; } = new List<IDeviceProvider>();
        public async Task Init()
        {
            if (!Providers.Any() && serviceProvider != null)
                Providers.AddRange(serviceProvider.GetServices<IDeviceProvider>());

            Providers.AsParallel().ForAll(async x => await x.Init());
        }

        public List<IDevice> Devices { get; set; } = new List<IDevice>();
        public delegate void OnUnloadDeviceHandler(IDevice device, List<IDevice> devices);
        public delegate void OnloadDeviceHandler(IDevice device, List<IDevice> devices);
        public event OnUnloadDeviceHandler? OnUnloadDevice;
        public event OnloadDeviceHandler? OnloadDevice;
        public async void LoadDevice(IDevice device)
        {

            DevicesConfig Config = configuration.Get<DevicesConfig>();
            lock (Devices)
            {
                UniqueName(device);
                Devices.Add(device);
                Config.Devices.TryAdd(device.Name, new DeviceConfig());
            }

            var deviceConfig = Config.Devices[device.Name];

            deviceConfig.Variant = device.Variants.Contains(deviceConfig.Variant)
                                    ? deviceConfig.Variant
                                    : device.DefaultVariant();

            (device as IRange)?.SetRange(deviceConfig);
            device.SelectedVariant = deviceConfig.Variant;
            device.Channel = deviceConfig.Channel;
            configuration.Save(Config);
            OnloadDevice?.Invoke(device, Devices);
        }

        private void UniqueName(IDevice device)
        {
            var c = 0;
            var NewName = device.Name;
            while (Devices.Any(x => x.Name == NewName))
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
            OnUnloadDevice?.Invoke(device, Devices);


        }
    }

}
