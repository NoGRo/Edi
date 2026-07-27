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
using System.ComponentModel;
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
        private DeviceCollector _deviceCollector;
        private IndexRepository _indexRepository;
        private FunscriptRepository _funscriptRepository;
        private IndexRepository indexRepository => _indexRepository ??= _serviceProvider.GetRequiredService<IndexRepository>();
        private FunscriptRepository funscriptRepository => _funscriptRepository ??= _serviceProvider.GetRequiredService<FunscriptRepository>();
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly HandyDeviceFactory _deviceFactory;

        // Re‑usamos un solo HttpClient por key
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new();
        private readonly ConcurrentDictionary<string, bool> _usesV3Api = new();
        private readonly SemaphoreSlim _offsetApiLock = new(1, 1);
        private int _pendingOffset;
        private int _lastAppliedOffset = int.MinValue;
        private int _offsetUpdateWorkerActive;

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
            _deviceFactory = new HandyDeviceFactory(logger);
            timerReconnect.Elapsed += TimerReconnect_Elapsed;
            _pendingOffset = Config.OffsetMS;
            ((INotifyPropertyChanged)Config).PropertyChanged +=
                Config_PropertyChanged;
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
                // Detect firmware version and create appropriate device
                var firmwareVersion = await _deviceFactory.DetectFirmwareVersionAsync(client);
                IDevice handyDevice;
                var usesV3Api =
                    _deviceFactory.ShouldUseHspProtocol(firmwareVersion);
                _usesV3Api[key] = usesV3Api;

                await TryApplyOffset(
                    client,
                    usesV3Api,
                    () => Config.OffsetMS);

                if (usesV3Api)
                {
                    _logger.LogInformation($"Creating HandyV3Device (HSP protocol) for Key: {key}");
                    handyDevice = new HandyV3Device(
                        client,
                        funscriptRepository,
                        _logger);
                }
                else
                {
                    _ = await client.PutAsync(
                        "v2/mode",
                        new StringContent(
                            JsonConvert.SerializeObject(new ModeRequest(1)),
                            Encoding.UTF8,
                            "application/json"));
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
            _usesV3Api.TryRemove(key, out _);

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

        private void Config_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(HandyConfig.OffsetMS))
                return;

            Volatile.Write(ref _pendingOffset, Config.OffsetMS);
            StartOffsetUpdateWorker();
        }

        private void StartOffsetUpdateWorker()
        {
            if (Interlocked.CompareExchange(
                    ref _offsetUpdateWorkerActive,
                    1,
                    0) != 0)
            {
                return;
            }

            _ = ObserveOffsetUpdateWorker();
        }

        private async Task ObserveOffsetUpdateWorker()
        {
            try
            {
                await ApplyPendingOffsetUpdates();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error while applying the Handy user offset.");
            }
            finally
            {
                Interlocked.Exchange(ref _offsetUpdateWorkerActive, 0);

                if (Volatile.Read(ref _pendingOffset)
                    != Volatile.Read(ref _lastAppliedOffset))
                {
                    StartOffsetUpdateWorker();
                }
            }
        }

        private async Task ApplyPendingOffsetUpdates()
        {
            while (true)
            {
                var offset = Volatile.Read(ref _pendingOffset);

                // Coalesce quick arrow presses into the latest requested value.
                await Task.Delay(100);
                if (offset != Volatile.Read(ref _pendingOffset))
                    continue;

                var clients = _clients.ToArray();
                await Task.WhenAll(clients.Select(entry =>
                    ApplyOffsetForClient(entry.Key, entry.Value, offset)));

                Volatile.Write(ref _lastAppliedOffset, offset);
                if (offset == Volatile.Read(ref _pendingOffset))
                    return;
            }
        }

        private async Task ApplyOffsetForClient(
            string key,
            HttpClient client,
            int offset)
        {
            if (!_usesV3Api.TryGetValue(key, out var usesV3Api))
                return;

            await TryApplyOffset(client, usesV3Api, () => offset);
        }

        private async Task TryApplyOffset(
            HttpClient client,
            bool usesV3Api,
            Func<int> getOffset)
        {
            await _offsetApiLock.WaitAsync();
            try
            {
                await ApplyOffset(
                    client,
                    usesV3Api,
                    getOffset());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "The Handy user offset could not be applied.");
            }
            finally
            {
                _offsetApiLock.Release();
            }
        }

        internal static async Task ApplyOffset(
            HttpClient client,
            bool usesV3Api,
            int offset,
            CancellationToken cancellationToken = default)
        {
            var apiVersion = usesV3Api ? "v3" : "v2";
            using var response = await client.PutAsync(
                $"{apiVersion}/hstp/offset",
                new StringContent(
                    JsonConvert.SerializeObject(
                        new OffsetRequest(
                            HandyConfig.NormalizeOffset(offset))),
                    Encoding.UTF8,
                    "application/json"),
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }
}
