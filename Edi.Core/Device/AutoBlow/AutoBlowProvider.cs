using Edi.Core.Device.AutoBlow;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Index;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Edi.Core.Device.Handy;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.AutoBlow
{
    public class AutoBlowProvider : IDeviceProvider
    {
        private readonly ILogger _logger;
        private AutoBlowDevice device;
        private Timer timerReconnect = new Timer(40000);
        public HandyConfig Config { get; set; }
        private List<string> Keys = new List<string>();
        private Dictionary<string, AutoBlowDevice> devices = new Dictionary<string, AutoBlowDevice>();
        private DeviceManager deviceManager;
        private IndexRepository repository { get; set; }

        public AutoBlowProvider(IndexRepository repository, ConfigurationManager config, DeviceManager deviceManager, ILogger<AutoBlowProvider> logger)
        {
            _logger = logger;
            this.Config = config.Get<HandyConfig>();
            this.repository = repository;
            this.deviceManager = deviceManager;

            timerReconnect.Elapsed += TimerReconnect_Elapsed;

            _logger.LogInformation("AutoBlowProvider initialized with configuration and device manager.");
        }

        public async Task Init()
        {
            if (string.IsNullOrEmpty(Config.Key))
            {
                _logger.LogWarning("Config.Key is null or empty. Initialization aborted.");
                return;
            }

            _logger.LogInformation("Initializing AutoBlowProvider...");
            await Task.Delay(500);
            await RemoveAll();

            Keys = Config.Key.Split(',')
                             .Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length == 12)
                             .Select(x => x.Trim())
                             .ToList();

            _logger.LogInformation($"Parsed {Keys.Count} keys from Config.Key.");

            timerReconnect.Stop();
            ConnectAll();
            timerReconnect.Start();
            _logger.LogInformation("Initialization completed and reconnection timer started.");
        }

        private void ConnectAll()
        {
            _logger.LogInformation("Connecting all devices...");
            Keys.AsParallel().ForAll(async key =>
            {
                await Connect(key);
            });
        }

        private async Task Connect(string Key)
        {
            _logger.LogInformation($"Attempting to connect to device with Key: {Key}");

            HttpClient Client = devices.ContainsKey(Key) ? devices[Key].Client : NewClient(Key);
            HttpResponseMessage resp = null;

            try
            {
                resp = await Client.GetAsync("connected");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Connection attempt failed for Key: {Key}. Exception: {ex.Message}");
            }

            if (resp?.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.LogWarning($"Device with Key: {Key} not responding. Removing from active devices.");
                await Remove(Key);
                return;
            }

            var connected = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
            if (!connected.connected)
            {
                _logger.LogWarning($"Device with Key: {Key} is not connected. Removing from active devices.");
                await Remove(Key);
                return;
            }

            if (devices.ContainsKey(Key))
            {
                _logger.LogInformation($"Device with Key: {Key} is already connected.");
                return;
            }

            Client.Dispose();
            Client = NewClient(Key, connected.cluster);
            resp = await Client.GetAsync("state");

            var status = JsonConvert.DeserializeObject<Status>(await resp.Content.ReadAsStringAsync());
            var device = new AutoBlowDevice(Client, repository,_logger);

            lock (devices)
            {
                if (devices.ContainsKey(Key))
                {
                    _logger.LogInformation($"Device with Key: {Key} is already registered in the devices list.");
                    return;
                }

                devices.Add(Key, device);
                deviceManager.LoadDevice(device);
                _logger.LogInformation($"Device with Key: {Key} successfully connected and loaded.");
            }
        }

        private async Task RemoveAll()
        {
            _logger.LogInformation("Removing all devices.");
            foreach (var key in Keys)
            {
                await Remove(key);
            }
        }

        private async Task Remove(string Key)
        {
            if (devices.ContainsKey(Key))
            {
                _logger.LogInformation($"Removing device with Key: {Key}");
                await deviceManager.UnloadDevice(devices[Key]);
                devices.Remove(Key);
            }
        }

        public static HttpClient NewClient(string Key, string Cluster = null)
        {
            Cluster ??= "us-east-1.autoblowapi.com";
            var Client = new HttpClient { BaseAddress = new Uri($"https://{Cluster}/autoblow/") };
            Client.DefaultRequestHeaders.Remove("x-device-token");
            Client.DefaultRequestHeaders.Add("x-device-token", Key);
            return Client;
        }

        private async void TimerReconnect_Elapsed(object? sender, ElapsedEventArgs e)
        {
            ConnectAll();
        }
    }
}
