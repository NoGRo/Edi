using Edi.Core.Device.Interfaces;
using Edi.Core.Players;

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
            deviceCollector.OnloadDevice += DeviceCollector_OnloadDevice;

        }
        private readonly DeviceCollector deviceCollector;
        private readonly ConfigurationManager configuration;
        private readonly DevicePlayer devicePlayer;
        private DevicesConfig config;

        private void DeviceCollector_OnloadDevice(IDevice device, List<IDevice> devices)
        {
            _ = devicePlayer.Sync(device);
        }

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
    }

}
