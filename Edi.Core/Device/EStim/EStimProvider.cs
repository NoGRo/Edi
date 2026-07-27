using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Services;
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

        public EStimProvider(RepositoryManager repositoryManager, ConfigurationManager config, DeviceCollector deviceCollector, ILogger<EStimProvider> logger)
        {
            Config = config.Get<EStimConfig>();
            DeviceCollector = deviceCollector;
            RepositoryManager = repositoryManager;
            _logger = logger;

            _logger.LogInformation($"EStimProvider initialized with Config: {Config.DeviceId}");
        }

        public EStimConfig Config { get; }
        public DeviceCollector DeviceCollector { get; }
        public RepositoryManager RepositoryManager { get; }

        public async Task Init()
        {
            _logger.LogInformation("Initialization started.");

            await Disconnect();

            // Validate configuration
            if (Config.DeviceId == -1)
            {
                _logger.LogWarning("DeviceId is set to -1. Initialization will be skipped.");
                return;
            }

            try
            {
                var audioRepository =
                    await RepositoryManager.GetRepositoryAsync<AudioRepository>();
                var outputDevice = new WaveOutEvent() { DeviceNumber = Config.DeviceId };
                var device = new EStimDevice(audioRepository, outputDevice, _logger);

                DeviceCollector.LoadDevice(device);
                _devices.Add(device);

                _logger.LogInformation($"Device loaded successfully: {device}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing device with DeviceId {Config.DeviceId}: {ex.Message}");
            }
        }

        public async Task Disconnect()
        {
            foreach (var device in _devices.ToArray())
            {
                _logger.LogInformation($"Unloading device: {device}");
                await device.Stop();
                DeviceCollector.UnloadDevice(device);
            }
            _devices.Clear();
        }
    }
}
