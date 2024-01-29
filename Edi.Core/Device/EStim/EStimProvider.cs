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
        private List<EStimDevice> _devices =  new List<EStimDevice>(); 
        public EStimProvider(AudioRepository audioRepository, ConfigurationManager config, DeviceManager deviceManager)
        {
            Config = config.Get<EStimConfig>();
            DeviceManager = deviceManager;
            AudioRepository = audioRepository;
        }

        public EStimConfig Config { get; }
        public DeviceManager DeviceManager { get; }
        public AudioRepository AudioRepository { get; }

        public async Task Init()
        { 
            foreach (var eStimDevice in _devices)
            {
                await DeviceManager.UnloadDevice(eStimDevice);
            }
            _devices.Clear();


            if (Config.DeviceId == -1)
                return;

            var outputDevice = new WaveOutEvent() { DeviceNumber = (Config.DeviceId) };
            var device = new EStimDevice(AudioRepository, outputDevice);

            DeviceManager.LoadDevice(device);
            _devices.Add(device);
            
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
