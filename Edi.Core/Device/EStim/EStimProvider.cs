using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.EStimAudio;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.EStim
{
    public class EStimProvider : IDeviceProvider
    {
        public EStimProvider(AudioRepository audioRepository, ConfigurationManager config, DeviceManager deviceManager)
        {
            Config = config.Get<EStimConfig>();
            DeviceManager = deviceManager;
            AudioRepository = audioRepository;
        }

        public EStimConfig Config { get; }
        public DeviceManager DeviceManager { get; }
        public AudioRepository AudioRepository { get; }

        public Task Init()
        {
            if (Config.DeviceId == -1)
                return Task.CompletedTask;

            var outputDevice = new WaveOutEvent() { DeviceNumber = (Config.DeviceId) };
            var device = new EStimDevice(AudioRepository, outputDevice);
            
            DeviceManager.LoadDevice(device);
            
            return Task.CompletedTask;
        }
        //private int DescriptonToDeviceNumber(string deviceId)
        //{
        //    int deviceNumber = -1;
        //    for (int i = 0; i < WaveOut.DeviceCount; i++)
        //    {
        //        var capabilities = WaveOut.GetCapabilities(i);
        //        if (capabilities.ProductName == deviceId)
        //        {
        //            deviceNumber = i;
        //            break;
        //        }
        //    }
        //    return deviceNumber;
        //}
    }
}
