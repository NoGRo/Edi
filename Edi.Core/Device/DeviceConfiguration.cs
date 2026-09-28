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
            await SelectVariants(new Dictionary<IDevice, string> { [device] = variant });
        }

        public async Task SelectVariants(IReadOnlyDictionary<IDevice, string> selections)
        {
            var changes = selections
                .Where(selection => selection.Key is not null
                    && deviceCollector.Devices.Contains(selection.Key)
                    && selection.Key.Variants.Contains(selection.Value)
                    && selection.Key.SelectedVariant != selection.Value)
                .ToArray();
            if (changes.Length == 0)
                return;

            await Task.WhenAll(changes.Select(async selection =>
            {
                if (selection.Key.IsReady)
                    await selection.Key.Stop();
                selection.Key.SelectedVariant = selection.Value;
            }));

            foreach (var selection in changes)
            {
                config.Devices.TryAdd(selection.Key.Name, new DeviceConfig());
                config.Devices[selection.Key.Name].Variant = selection.Value;
            }
            configuration.Save(config);
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
