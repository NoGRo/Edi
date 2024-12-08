using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Interfaces;
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

namespace Edi.Core.Device.Handy
{
    public class HandyProvider : IDeviceProvider
    {
        private readonly ILogger _logger;
        private Timer timerReconnect = new Timer(40000);
        private List<string> Keys = new List<string>();
        private Dictionary<string, HandyDevice> devices = new Dictionary<string, HandyDevice>();
        private DeviceManager deviceManager;
        private IndexRepository repository { get; }

        public HandyProvider(IndexRepository repository, ConfigurationManager config, DeviceManager deviceManager, ILogger logger)
        {
            _logger = logger;
            Config = config.Get<HandyConfig>();
            this.repository = repository;
            this.deviceManager = deviceManager;
            timerReconnect.Elapsed += TimerReconnect_Elapsed;

            _logger.LogInformation("HandyProvider initialized.");
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

        private async Task Connect(string Key)
        {
            _logger.LogInformation($"Connecting to device with Key: {Key}");

            HttpClient Client = devices.ContainsKey(Key) ? devices[Key].Client : NewClient(Key);
            HttpResponseMessage resp = null;

            try
            {
                resp = await Client.GetAsync("connected");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Connection failed for Key: {Key} - {ex.Message}");
                await Remove(Key);
                return;
            }

            if (resp?.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.LogWarning($"Device with Key: {Key} not reachable, removing.");
                await Remove(Key);
                return;
            }

            var status = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
            if (!status.connected)
            {
                _logger.LogWarning($"Device with Key: {Key} not connected, removing.");
                await Remove(Key);
                return;
            }

            if (!devices.ContainsKey(Key))
            {
                _ = await Client.PutAsync("mode", new StringContent(JsonConvert.SerializeObject(new ModeRequest(1)), Encoding.UTF8, "application/json"));

                var handyDevice = new HandyDevice(Client, repository, _logger);
                lock (devices)
                {
                    devices[Key] = handyDevice;
                    deviceManager.LoadDevice(handyDevice);
                    _logger.LogInformation($"Device {handyDevice.Name} loaded with Key: {Key}");
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

        private async Task Remove(string Key)
        {
            if (devices.TryGetValue(Key, out var device))
            {
                await deviceManager.UnloadDevice(device);
                devices.Remove(Key);
                _logger.LogInformation($"Device removed with Key: {Key}");
            }
        }

        public static HttpClient NewClient(string Key)
        {
            var Client = new HttpClient { BaseAddress = new Uri("https://www.handyfeeling.com/api/handy/v2/") };
            Client.DefaultRequestHeaders.Remove("X-Connection-Key");
            Client.DefaultRequestHeaders.Add("X-Connection-Key", Key);
            return Client;
        }

        private void TimerReconnect_Elapsed(object? sender, ElapsedEventArgs e)
        {

                ConnectAll();

        }
    }
}
