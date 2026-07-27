using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyChanged;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Edi.Core.Device.Simulator
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class RecorderConfig
    {
        public bool Record { get; set; } = false;
        public string[] Recorders { get; set; } = ["Main"];
    }
    public class RecorderProvider : IDeviceProvider
    {
        private readonly ILogger _logger;
        private readonly RepositoryManager repositoryManager;
        private readonly IServiceProvider serviceProvider;
        private readonly List<RecorderDevice> _devices = new List<RecorderDevice>();

        public RecorderProvider(RepositoryManager repositoryManager, ConfigurationManager config, DeviceCollector deviceCollector, ILogger<RecorderProvider> logger, IServiceProvider serviceProvider)
        {
            Config = config.Get<RecorderConfig>();
            DeviceCollector = deviceCollector;
            this.repositoryManager = repositoryManager;
            _logger = logger;
            this.serviceProvider = serviceProvider;
            _logger.LogInformation($"OutputRecorderProvider initialized with Config: Record={Config.Record}");
        }

        public RecorderConfig Config { get; }
        public DeviceCollector DeviceCollector { get; }
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
                await repositoryManager
                    .GetRepositoryAsync<FunscriptRepository>();
                foreach (var recorderName in Config.Recorders)
                {
                    var device = serviceProvider.GetRequiredService<RecorderDevice>();
                    device.Name += $"_{recorderName}";
                    DeviceCollector.LoadDevice(device);
                    _devices.Add(device);
                    _logger.LogInformation($"OutputRecorderDevice loaded successfully: {device}");

                }
                _devices.ForEach(x => x.StartRecording());

            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing OutputRecorderDevice: {ex.Message}");
            }
        }
    }
}
