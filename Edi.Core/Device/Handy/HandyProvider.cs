using Edi.Core.Device.Buttplug;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.Funscript;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Services;

namespace Edi.Core.Device.Handy
{
    public class HandyProvider : IDeviceProvider
    {

        private readonly ILogger _logger;
        private Timer timerReconnect = new Timer(400000);
        private List<string> Keys = new List<string>();
        private Dictionary<string, IDevice> devices = new Dictionary<string, IDevice>();
        private readonly IServiceProvider _serviceProvider;
        private readonly ConfigurationManager configManager;
        private DeviceCollector _deviceCollector;
        private IndexRepository _indexRepository;
        private FunscriptRepository _funscriptRepository;
        private IndexRepository indexRepository => _indexRepository ??= _serviceProvider.GetRequiredService<IndexRepository>();
        private FunscriptRepository funscriptRepository => _funscriptRepository ??= _serviceProvider.GetRequiredService<FunscriptRepository>();
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly HandyDeviceFactory _deviceFactory;

        // Re‑usamos un solo HttpClient por key
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new();

        public HandyProvider(IServiceProvider serviceProvider,
                             ConfigurationManager config,
                             DeviceCollector deviceCollector,
                             IHttpClientFactory httpClientFactory,
                             ILogger<HandyProvider> logger)
        {
            _logger = logger;
            Config = config.Get<HandyConfig>();
            _serviceProvider = serviceProvider;
            this.configManager = config;
            _deviceCollector = deviceCollector;
            _httpClientFactory = httpClientFactory;
            _deviceFactory = new HandyDeviceFactory(logger);
            timerReconnect.Elapsed += TimerReconnect_Elapsed;
        }


        public HandyConfig Config { get; set; }

        public async Task Init()
        {
            if (string.IsNullOrEmpty(Config.Key))
            {
                _logger.LogWarning("Configuration key is empty; initialization aborted.");
                return;
            }

            await Task.Delay(500);
            RemoveAll();

            Keys = Config.Key.Split(',')
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .Select(x => x.Trim())
                             .ToList();

            _logger.LogInformation($"Starting initialization with {Keys.Count} device keys.");

            timerReconnect.Stop();
            ConnectAll();
            timerReconnect.Start();
        }

        private void ConnectAll()
        {
            lock (Keys)
            {
                Keys.AsParallel().ForAll(async key =>
                {
                    await Connect(key);
                });
            }
        }

        private async Task Connect(string key)
        {
            _logger.LogInformation($"Connecting to device with Key: {key}");

            var client = GetOrCreateClient(key);

            HttpResponseMessage resp;
            try
            {
                resp = await client.GetAsync("v2/connected");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Connection failed for Key: {key} - {ex.Message}");
                Remove(key);
                return;
            }

            if (resp?.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.LogWarning($"Device with Key: {key} not reachable, removing.");
                Remove(key);
                return;
            }

            var status = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
            if (!status.connected)
            {
                _logger.LogWarning($"Device with Key: {key} not connected, removing.");
                Remove(key);
                return;
            }

            if (!devices.ContainsKey(key))
            {
                _ = await client.PutAsync("v2/mode", new StringContent(JsonConvert.SerializeObject(new ModeRequest(1)), Encoding.UTF8, "application/json"));
                _ = await client.PutAsync("v2/hstp/offset", new StringContent(JsonConvert.SerializeObject(new OffsetRequest(Config.OffsetMS)), Encoding.UTF8, "application/json"));

                // Detect firmware version and create appropriate device
                var firmwareVersion = await _deviceFactory.DetectFirmwareVersionAsync(client);
                IDevice handyDevice;

                if (_deviceFactory.ShouldUseHspProtocol(firmwareVersion))
                {
                    _logger.LogInformation($"Creating HandyV3Device (HSP protocol) for Key: {key}");
                    handyDevice = new HandyV3Device(client, indexRepository, configManager, _logger);
                }
                else
                {
                    _logger.LogInformation($"Creating HandyDevice (Legacy HSSP protocol) for Key: {key}");
                    handyDevice = new HandyDevice(client, indexRepository, _logger);
                }

                lock (devices)
                {
                    devices[key] = handyDevice;
                    _deviceCollector.LoadDevice(handyDevice);
                    _logger.LogInformation($"Device {handyDevice.Name} loaded with Key: {key} (Firmware: {firmwareVersion})");
                }

                _= ServerTimeSync.SyncServerTimeAsync();
            }
        }

        private void RemoveAll()
        {
            _logger.LogInformation("Removing all devices.");
            foreach (var key in Keys)
            {
                Remove(key);
            }
        }

        private void Remove(string key)
        {
            _clients.TryRemove(key, out var client);

            if (devices.TryGetValue(key, out var device))
            {
                _deviceCollector.UnloadDevice(device);
                devices.Remove(key);
                _logger.LogInformation($"Device removed with Key: {key}");
            }
        }
        private HttpClient GetOrCreateClient(string key)
        {
            // Thread‑safe cache; creates the client only once per key
            return _clients.GetOrAdd(key, k =>
            {
                var client = _httpClientFactory.CreateClient("HandyAPI");
                client.DefaultRequestHeaders.Remove("X-Connection-Key");
                client.DefaultRequestHeaders.Add("X-Connection-Key", k);
                client.DefaultRequestHeaders.Remove("authorization");
                client.DefaultRequestHeaders.Add("authorization", "Bearer " + Config.ApiKey);
                
                return client;
            });
        }

        private void TimerReconnect_Elapsed(object sender, ElapsedEventArgs e)
        {
            ConnectAll();
        }
    }
}
