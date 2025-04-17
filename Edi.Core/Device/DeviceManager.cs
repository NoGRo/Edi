using Edi.Core.Device.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PropertyChanged;

namespace Edi.Core.Device
{

    [AddINotifyPropertyChangedInterface]
    public class DeviceManager
    {
        
        [ActivatorUtilitiesConstructor]
        public DeviceManager(ConfigurationManager configuration, IServiceProvider serviceProvider)
        {
            Config = configuration.Get<DevicesConfig>();
            this.configuration = configuration;
            this.serviceProvider = serviceProvider;
            
        }

        internal DevicesConfig Config;
        internal readonly ConfigurationManager configuration;

        public List<IDevice> Devices { get; set; } = new List<IDevice>();
        public List<IDeviceProvider> Providers { get; set; } = new List<IDeviceProvider>();
        
        private IServiceProvider serviceProvider { get; }

        public async Task Init()
        {
            if (!Providers.Any() && serviceProvider != null)
                Providers.AddRange(serviceProvider.GetServices<IDeviceProvider>());

            Providers.AsParallel().ForAll(async x => await x.Init());
        }

        public delegate void OnUnloadDeviceHandler(IDevice device, List<IDevice> devices);
        public delegate void OnloadDeviceHandler(IDevice device, List<IDevice> devices);
        public event OnUnloadDeviceHandler? OnUnloadDevice;
        public event OnloadDeviceHandler? OnloadDevice;

        public async void LoadDevice(IDevice device)
        {
            lock (Devices)
            {
                UniqueName(device);
                Devices.Add(device);
                Config.Devices.TryAdd(device.Name, new DeviceConfig());
            }

            var deviceConfig = Config.Devices[device.Name];

            deviceConfig.Variant = device.Variants.Contains(deviceConfig.Variant)
                                    ? deviceConfig.Variant
                                    : device.ResolveDefaultVariant();

            (device as IRange)?.SetRange(deviceConfig);
            device.SelectedVariant = deviceConfig.Variant;
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

        
        private string? lastGallerySend;

        public async Task SelectVariant(IDevice device, string variant)
        {
            if (device.SelectedVariant == variant)
                return;

            var deviceName = Devices.FirstOrDefault(x => x == device)?.Name;

            if (device is null || deviceName is null)
                return;
            if (!Config.Devices.ContainsKey(deviceName))
                Config.Devices.Add(deviceName, new() { Variant = variant });
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

        public async Task SelectRange(IDevice device, int min, int max)
        {
            var deviceName = Devices.FirstOrDefault(x => x == device)?.Name;

            if (device is null || deviceName is null || device is not IRange)
                return;

            Config.Devices[deviceName].SetRange(min, max);


            (device as IRange).SetRange(min, max);

            configuration.Save(Config);
        }

        public void Intensity(int Max)
        {
            foreach (var device in Devices)
            {
                if (device is not IRange)
                    return;
                var range = device as IRange;
                var configRange = Config.Devices[device.Name] as IRange;

                // This line calculates the maximum value of 'range' based on a percentage of the difference between the maximum and minimum values in 'configRange'.
                // It adjusts 'range.Max' proportionally according to 'Max', which represents a percentage (0 to 100). This allows dynamic scaling of 'range' within the bounds of 'configRange'.
                range.Max = configRange.Min + (configRange.Max - configRange.Min) * Max / 100;
            }
        }

        public async Task Stop()
        {
            lastGallerySend = null;
            _ = Devices.Select(device => device.Stop()).ToList();
            //await Task.WhenAll(stopTasks);
        }

        public async Task PlayGallery(string name, long seek = 0)
        {
            lastGallerySend = name;
            _ = Devices.Select(device => device.PlayGallery(name, seek)).ToList();

        }
    }
}
