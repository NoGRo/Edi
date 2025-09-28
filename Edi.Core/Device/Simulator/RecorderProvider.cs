using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Edi.Core.Device.Simulator
{
    public class RecorderProvider : IDeviceProvider
    {
        private readonly ILogger _logger;
        private readonly List<RecorderDevice> _devices = new List<RecorderDevice>();

        public RecorderProvider(FunscriptRepository funscriptRepository, ConfigurationManager config, DeviceCollector deviceCollector, ILogger<RecorderProvider> logger)
        {
            Config = config.Get<RecorderConfig>();
            DeviceCollector = deviceCollector;
            FunscriptRepository = funscriptRepository;
            _logger = logger;
            _logger.LogInformation($"OutputRecorderProvider initialized with Config: Record={Config.Record}");
        }

        public RecorderConfig Config { get; }
        public DeviceCollector DeviceCollector { get; }
        public FunscriptRepository FunscriptRepository { get; }

        public async Task Init()
        {
            _logger.LogInformation("OutputRecorderProvider initialization started.");

            // Unload existing devices
            foreach (var device in _devices)
            {
                _logger.LogInformation($"Unloading device: {device}");
                DeviceCollector.UnloadDevice(device);
            }
            _devices.Clear();

            if (!Config.Record)
            {
                _logger.LogInformation("RecorderConfig.Record is false. No device will be loaded.");
                return;
            }

            try
            {
                var device = new RecorderDevice(FunscriptRepository, _logger);
                DeviceCollector.LoadDevice(device);
                _devices.Add(device);
                _logger.LogInformation($"OutputRecorderDevice loaded successfully: {device}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing OutputRecorderDevice: {ex.Message}");
            }
        }
    }
}
