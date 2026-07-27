using Edi.Core.Device.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropertyChanged;
using System;
using System.Threading.Channels;
using ConfigurationManager = Edi.Core.Services.ConfigurationManager;

namespace Edi.Core.Device
{

    [AddINotifyPropertyChangedInterface]
    public class DeviceCollector(ConfigurationManager configuration, IServiceProvider serviceProvider)
    {
        private readonly SemaphoreSlim lifecycleLock = new(1, 1);
        public List<IDeviceProvider> Providers { get; set; } = new List<IDeviceProvider>();
        public async Task Init()
        {
            await lifecycleLock.WaitAsync();
            try
            {
                EnsureProviders();
                await InitProviders();
            }
            finally
            {
                lifecycleLock.Release();
            }
        }

        public async Task Reinitialize(Func<Task> reload)
        {
            ArgumentNullException.ThrowIfNull(reload);

            await lifecycleLock.WaitAsync();
            try
            {
                EnsureProviders();
                await DisconnectProviders();
                await reload();
                await InitProviders();
            }
            finally
            {
                lifecycleLock.Release();
            }
        }

        private void EnsureProviders()
        {
            if (Providers.Any() || serviceProvider == null)
                return;

            Providers.AddRange(
                serviceProvider.GetServices<IDeviceProvider>());
        }

        private async Task InitProviders()
        {
            var initTasks = Providers.Select(p => p.Init()).ToArray();
            await Task.WhenAll(initTasks);
        }

        private async Task DisconnectProviders()
        {
            var disconnectTasks =
                Providers.Select(provider => provider.Disconnect()).ToArray();
            await Task.WhenAll(disconnectTasks);
        }

        public List<IDevice> Devices { get; set; } = new List<IDevice>();
        public delegate void OnUnloadDeviceHandler(IDevice device, List<IDevice> devices);
        public delegate void OnloadDeviceHandler(IDevice device, List<IDevice> devices);
        public event OnUnloadDeviceHandler OnUnloadDevice;
        public event OnloadDeviceHandler OnloadDevice;
        public void LoadDevice(IDevice device)
        {

            DevicesConfig Config = configuration.Get<DevicesConfig>();
            EdiConfig ediConfig = configuration.Get<EdiConfig>();
            lock (Devices)
            {
                UniqueName(device);
                Devices.Add(device);
                Config.Devices.TryAdd(device.Name, new DeviceConfig());
            }

            var deviceConfig = Config.Devices[device.Name];

            deviceConfig.Variant = device.Variants.Contains(deviceConfig.Variant)  && deviceConfig.Variant != "None"
                                    ? deviceConfig.Variant
                                    : device.DefaultVariant();

            (device as IRange)?.SetRange(deviceConfig);
            device.SelectedVariant = deviceConfig.Variant;
            device.Channel = deviceConfig.Channel;

            if (string.IsNullOrEmpty(device.Channel) && ediConfig.UseChannels)
                device.Channel = ediConfig.Channels.FirstOrDefault();

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

        public void UnloadDevice(IDevice device)
        {
            lock (Devices)
            {
                Devices.RemoveAll(x => x.Name == device.Name);

            }
            _ = StopRemovedDevice(device);
            OnUnloadDevice?.Invoke(device, Devices);
        }

        private static async Task StopRemovedDevice(IDevice device)
        {
            try
            {
                await device.Stop();
            }
            catch
            {
                // Providers may unload devices precisely because their transport disappeared.
            }
        }
    }

}
