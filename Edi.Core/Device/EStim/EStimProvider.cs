using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace Edi.Core.Device.EStim
{
    public class EStimProvider : IDeviceProvider
    {
        private readonly ILogger _logger;
        private readonly List<EStimDevice> _devices = new List<EStimDevice>();
        private readonly AudioEngine engine;

        public EStimProvider(AudioRepository audioRepository, ConfigurationManager config, DeviceCollector deviceCollector, AudioEngine engine, ILogger<EStimProvider> logger)
        {
            Config = config.Get<EStimConfig>();
            DeviceCollector = deviceCollector;
            AudioRepository = audioRepository;
            this.engine = engine;
            _logger = logger;

            _logger.LogInformation($"EStimProvider initialized with Config: {Config.DeviceId}");
        }

        public EStimConfig Config { get; }
        public DeviceCollector DeviceCollector { get; }
        public AudioRepository AudioRepository { get; }

        public async Task Init()
        {
            _logger.LogInformation("Initialization started.");

            // Unload existing devices
            foreach (var eStimDevice in _devices)
            {
                _logger.LogInformation($"Unloading device: {eStimDevice}");
                DeviceCollector.UnloadDevice(eStimDevice);
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
                var outputDevice = engine.InitializePlaybackDevice(engine.PlaybackDevices[Config.DeviceId], AudioFormat.Dvd);
                var device = new EStimDevice(AudioRepository, outputDevice, _logger);

                DeviceCollector.LoadDevice(device);
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
