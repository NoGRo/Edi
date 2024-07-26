using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device
{
    public class DevicePlayer
    {
        private DeviceManager DeviceManager { get; }
        public string ChannelName { get; set; }

        private IEnumerable<IDevice> Devices => DeviceManager.Devices.Where(x => string.IsNullOrEmpty(ChannelName));// ||  x.Channels.Contains(ChannelName));

        private DevicesConfig Config => DeviceManager.Config;
        private ConfigurationManager configuration => DeviceManager.configuration;


        public DevicePlayer(DeviceManager deviceManager)
        {
            DeviceManager = deviceManager;
        }
        private string? lastGallerySend;
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


        public async Task SelectVariant(IDevice device, string variant)
        {
            if (device == null || device.SelectedVariant == variant)
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

    }
}
