using Edi.Core.Device.Buttplug;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Index;
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

namespace Edi.Core.Device.Handy
{
    public class HandyProvider : IDeviceProvider
    {

        private readonly ILogger _logger;
        private Timer timerReconnect = new Timer(40000);
        private List<string> Keys = new List<string>();
        private Dictionary<string, HandyDevice> devices = new Dictionary<string, HandyDevice>();
        private readonly IServiceProvider _serviceProvider;
        private DeviceCollector _deviceCollector;
        private IndexRepository _repository;
        private IndexRepository repository => _repository ??= _serviceProvider.GetRequiredService<IndexRepository>();
        private readonly IHttpClientFactory _httpClientFactory;

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
            _deviceCollector = deviceCollector;
            _httpClientFactory = httpClientFactory;
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
            await RemoveAll();

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
            Keys.AsParallel().ForAll(async key =>
            {
                await Connect(key);
            });
        }

        private async Task Connect(string key)
        {
            _logger.LogInformation($"Connecting to device with Key: {key}");

            var client = GetOrCreateClient(key);

            HttpResponseMessage resp;
            try
            {
                resp = await client.GetAsync("connected");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Connection failed for Key: {key} - {ex.Message}");
                await Remove(key);
                return;
            }

            if (resp?.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.LogWarning($"Device with Key: {key} not reachable, removing.");
                await Remove(key);
                return;
            }

            var status = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
            if (!status.connected)
            {
                _logger.LogWarning($"Device with Key: {key} not connected, removing.");
                await Remove(key);
                return;
            }

            if (!devices.ContainsKey(key))
            {
                _ = await client.PutAsync("mode", new StringContent(JsonConvert.SerializeObject(new ModeRequest(1)), Encoding.UTF8, "application/json"));

                var handyDevice = new HandyDevice(client, repository, _logger);
                lock (devices)
                {
                    devices[key] = handyDevice;
                    _deviceCollector.LoadDevice(handyDevice);
                    _logger.LogInformation($"Device {handyDevice.Name} loaded with Key: {key}");
                }

                await handyDevice.updateServerTime();
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

        private async Task Remove(string key)
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
                return client;
            });
        }

        private void TimerReconnect_Elapsed(object sender, ElapsedEventArgs e)
        {
            ConnectAll();
        }
    }
}
