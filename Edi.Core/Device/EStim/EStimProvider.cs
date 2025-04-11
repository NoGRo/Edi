using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.EStimAudio;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Edi.Core.Device.EStim
{
    public class EStimProvider : IDeviceProvider
    {
        private readonly ILogger _logger;
        private readonly List<EStimDevice> _devices = new List<EStimDevice>();

        public EStimProvider(AudioRepository audioRepository, ConfigurationManager config, DeviceManager deviceManager, ILogger<EStimProvider> logger)
        {
            Config = config.Get<EStimConfig>();
            DeviceManager = deviceManager;
            AudioRepository = audioRepository;
            _logger = logger;

            _logger.LogInformation($"EStimProvider initialized with Config: {Config.DeviceId}");
        }

        public EStimConfig Config { get; }
        public DeviceManager DeviceManager { get; }
        public AudioRepository AudioRepository { get; }

        public async Task Init()
        {
            _logger.LogInformation("Initialization started.");

            // Unload existing devices
            foreach (var eStimDevice in _devices)
            {
                _logger.LogInformation($"Unloading device: {eStimDevice}");
                await DeviceManager.UnloadDevice(eStimDevice);
            }
            _devices.Clear();

            // Validate configuration
            if (Config.DeviceId == -1)
            {
                _logger.LogWarning("DeviceId is set to -1. Initialization will be skipped.");
                return;
            }

            try
            {
                var outputDevice = new WaveOutEvent() { DeviceNumber = Config.DeviceId };
                var device = new EStimDevice(AudioRepository, outputDevice, _logger);

                DeviceManager.LoadDevice(device);
                _devices.Add(device);

                _logger.LogInformation($"Device loaded successfully: {device}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing device with DeviceId {Config.DeviceId}: {ex.Message}");
            }
        }
    }
}
