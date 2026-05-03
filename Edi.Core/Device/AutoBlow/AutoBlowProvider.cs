using Edi.Core.Device.Buttplug;
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
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Edi.Core.Device;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Services;

namespace Edi.Core.Device.AutoBlow
{
    public class AutoBlowProvider : IDeviceProvider
    {
        private readonly ILogger _logger;
        private Timer timerReconnect = new Timer(40000);
        public HandyConfig Config { get; set; }
        private List<string> Keys = new List<string>();
        private Dictionary<string, IDevice> devices = new Dictionary<string, IDevice>();
        private readonly IServiceProvider serviceProvider;
        private DeviceCollector deviceCollector;
        private IndexRepository _repository;
        private IndexRepository repository => _repository ??= serviceProvider.GetRequiredService<IndexRepository>();
        
        // Cache de cluster por clave de dispositivo
        private readonly ConcurrentDictionary<string, string> _clusterCache = new();

        public AutoBlowProvider(IServiceProvider serviceProvider, ConfigurationManager config, DeviceCollector deviceCollector, ILogger<AutoBlowProvider> logger)
        {
            _logger = logger;
            Config = config.Get<HandyConfig>();
            this.serviceProvider = serviceProvider;
            this.deviceCollector = deviceCollector;

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
            RemoveAll();

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

            HttpClient Client = null;
            HttpResponseMessage resp = null;

            try
            {
                // Intentar primero como AutoBlow
                Client = NewClient(Key);
                resp = await Client.GetAsync("connected");
                
                if (resp?.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var connected = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
                    if (connected.connected)
                    {
                        await CreateAutoBlowDevice(Key, connected.cluster, Client);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"AutoBlow connection attempt failed for Key: {Key}. Attempting Vacuglide: {ex.Message}");
            }

            // Si AutoBlow falló, intentar como Vacuglide
            try
            {
                if (Client != null)
                {
                    Client.Dispose();
                }

                var clusterResult = await DetectVacuglideCluster(Key);
                if (!string.IsNullOrEmpty(clusterResult.cluster) && clusterResult.isVacuglide)
                {
                    Client = NewVacuglideClient(Key, clusterResult.cluster);
                    await CreateVacuglideDevice(Key, clusterResult.cluster, Client);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Vacuglide connection attempt failed for Key: {Key}. Exception: {ex.Message}");
            }

            _logger.LogWarning($"Device with Key: {Key} not responding as AutoBlow or Vacuglide. Removing from active devices.");
            Remove(Key);
        }

        private async Task CreateAutoBlowDevice(string Key, string cluster, HttpClient Client)
        {
            if (devices.ContainsKey(Key))
            {
                _logger.LogInformation($"Device with Key: {Key} is already connected.");
                return;
            }

            try
            {
                HttpResponseMessage resp = await Client.GetAsync("state");
                var status = JsonConvert.DeserializeObject<Status>(await resp.Content.ReadAsStringAsync());

                var device = new AutoBlowDevice(Client, repository, _logger);

                lock (devices)
                {
                    if (devices.ContainsKey(Key))
                    {
                        _logger.LogInformation($"Device with Key: {Key} is already registered in the devices list.");
                        return;
                    }

                    devices.Add(Key, device);
                    deviceCollector.LoadDevice(device);
                    _logger.LogInformation($"AutoBlow device with Key: {Key} successfully connected and loaded (Cluster: {cluster}).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating AutoBlow device for Key: {Key} - {ex.Message}");
                Remove(Key);
            }
        }

        private async Task CreateVacuglideDevice(string Key, string cluster, HttpClient Client)
        {
            if (devices.ContainsKey(Key))
            {
                _logger.LogInformation($"Device with Key: {Key} is already connected.");
                return;
            }

            try
            {
                HttpResponseMessage resp = await Client.GetAsync("/vacuglide/info");
                var deviceInfo = JsonConvert.DeserializeObject<VacuglideDeviceInfoResponse>(await resp.Content.ReadAsStringAsync());

                var device = new VacuglideDevice(Client, repository, _logger);

                lock (devices)
                {
                    if (devices.ContainsKey(Key))
                    {
                        _logger.LogInformation($"Device with Key: {Key} is already registered in the devices list.");
                        return;
                    }

                    devices.Add(Key, device);
                    deviceCollector.LoadDevice(device);
                    _logger.LogInformation($"Vacuglide device with Key: {Key} successfully connected and loaded (Cluster: {cluster}, Firmware: {deviceInfo?.firmwareVersion}).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating Vacuglide device for Key: {Key} - {ex.Message}");
                Remove(Key);
            }
        }

        private async Task<(string cluster, bool isVacuglide)> DetectVacuglideCluster(string Key)
        {
            try
            {
                using (var latencyClient = new HttpClient { BaseAddress = new Uri("https://latency.autoblowapi.com") })
                {
                    latencyClient.DefaultRequestHeaders.Add("x-device-token", Key);
                    var resp = await latencyClient.GetAsync("/vacuglide/connected");

                    if (resp?.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        var connectedStatus = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
                        return (connectedStatus?.cluster, connectedStatus?.connected ?? false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to detect Vacuglide cluster for Key: {Key}: {ex.Message}");
            }

            return (null, false);
        }

        private void RemoveAll()
        {
            _logger.LogInformation("Removing all devices.");
            foreach (var key in Keys)
            {
                Remove(key);
            }
        }

        private void Remove(string Key)
        {
            if (devices.ContainsKey(Key))
            {
                _logger.LogInformation($"Removing device with Key: {Key}");
                deviceCollector.UnloadDevice(devices[Key]);
                devices.Remove(Key);
                _clusterCache.TryRemove(Key, out _);
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

        public static HttpClient NewVacuglideClient(string Key, string Cluster)
        {
            var clusterUrl = $"https://{Cluster}.autoblowapi.com";
            var Client = new HttpClient { BaseAddress = new Uri(clusterUrl) };
            Client.DefaultRequestHeaders.Remove("x-device-token");
            Client.DefaultRequestHeaders.Add("x-device-token", Key);
            return Client;
        }

        private void TimerReconnect_Elapsed(object sender, ElapsedEventArgs e)
        {
            ConnectAll();
        }
    }
}
