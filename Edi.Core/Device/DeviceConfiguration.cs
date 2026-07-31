using Edi.Core.Device.Interfaces;
using Edi.Core.Players;
using Edi.Core.Services;

namespace Edi.Core.Device
{
    public class DeviceConfiguration
    {
        public DeviceConfiguration(DeviceCollector deviceCollector, ConfigurationManager configuration, DevicePlayer devicePlayer)
        {
            this.deviceCollector = deviceCollector;
            this.configuration = configuration;
            this.devicePlayer = devicePlayer;
            config = configuration.Get<DevicesConfig>();
            
        }
        private readonly DeviceCollector deviceCollector;
        private readonly ConfigurationManager configuration;
        private readonly DevicePlayer devicePlayer;
        private DevicesConfig config;

        public async Task SelectVariant(IDevice device, string variant)
        {
            if (device.SelectedVariant == variant)
                return;

            var deviceName = deviceCollector.Devices.FirstOrDefault(x => x == device)?.Name;

            if (device is null || deviceName is null)  
                return;
            if (!config.Devices.ContainsKey(deviceName))
                config.Devices.Add(deviceName, new() { Variant = variant });
            else
            {
                if (device.IsReady)
                    await device.Stop();

                config.Devices[deviceName].Variant = variant;
            }

            if (!device.Variants.Contains(variant))
                return;

            configuration.Save(config);
            device.SelectedVariant = variant;
        }

        public async Task SelectChannel(IDevice device, string channel)
        {
            var deviceName = deviceCollector.Devices.FirstOrDefault(x => x == device)?.Name;

            if (device is null || deviceName is null)
                return;

            if (config.Devices[deviceName].Channel == channel)
                return;

            config.Devices[deviceName].Channel = channel;

            configuration.Save(config);
            device.Channel = channel;
        }

        public async Task SelectRange(IDevice device, int min, int max)
        {
            var deviceName = deviceCollector.Devices.FirstOrDefault(x => x == device)?.Name;

            if (device is null || deviceName is null || device is not IRange)
                return;

            config.Devices[deviceName].SetRange(min, max);
            (device as IRange).SetRange(min, max);

            configuration.Save(config);
        }

        private DeviceConfig GetConfiguration(IDevice device)
        {
            var deviceName =
                deviceCollector.Devices.FirstOrDefault(x => x == device)?.Name;

            if (deviceName is null)
                return null;

            config.Devices.TryAdd(deviceName, new DeviceConfig());
            return config.Devices[deviceName];
        }

        public Task SelectOffset(IDevice device, int offsetMilliseconds)
        {
            if (device is not IDeviceWithOffsetConfiguration)
            {
                return Task.CompletedTask;
            }

            var deviceConfig = GetConfiguration(device);
            if (deviceConfig is null)
                return Task.CompletedTask;

            deviceConfig.OffsetMS = offsetMilliseconds;
            configuration.Save(config);
            return Task.CompletedTask;
        }
    }

}
