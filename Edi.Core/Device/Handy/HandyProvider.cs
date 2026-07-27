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
        private readonly RepositoryManager _repositoryManager;
        private DeviceCollector _deviceCollector;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly HandyDeviceFactory _deviceFactory;
        private readonly IHandyBluetoothDiscovery _bluetoothDiscovery;

        // Re‑usamos un solo HttpClient por key
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new();
        private readonly ConcurrentDictionary<string, IHandyClient>
            _bluetoothClients = new();
        private readonly ConcurrentDictionary<string, bool> _usesV3Api = new();
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly SemaphoreSlim _offsetApiLock = new(1, 1);
        private readonly object _initTaskLock = new();
        private Task _initTask = Task.CompletedTask;
        private int _pendingOffset;
        private int _lastAppliedOffset = int.MinValue;
        private int _offsetUpdateWorkerActive;

        public HandyProvider(RepositoryManager repositoryManager,
                             ConfigurationManager config,
                             DeviceCollector deviceCollector,
                             IHttpClientFactory httpClientFactory,
                             IHandyBluetoothDiscovery bluetoothDiscovery,
                             ILogger<HandyProvider> logger)
        {
            _logger = logger;
            Config = config.Get<HandyConfig>();
            _repositoryManager = repositoryManager;
            _deviceCollector = deviceCollector;
            _httpClientFactory = httpClientFactory;
            _bluetoothDiscovery = bluetoothDiscovery;
            _deviceFactory = new HandyDeviceFactory(logger);
            timerReconnect.Elapsed += TimerReconnect_Elapsed;
            _pendingOffset = Config.OffsetMS;
            ((INotifyPropertyChanged)Config).PropertyChanged +=
                Config_PropertyChanged;
        }


        public HandyConfig Config { get; set; }

        public Task Init()
        {
            lock (_initTaskLock)
            {
                if (!_initTask.IsCompleted)
                    return _initTask;

                _initTask = Initialize();
                return _initTask;
            }
        }

        private async Task Initialize()
        {
            if (string.IsNullOrEmpty(Config.Key))
            {
                _logger.LogInformation(
                    "No Handy connection key is configured; " +
                    "continuing with Bluetooth discovery.");
            }

            timerReconnect.Stop();
            await _connectLock.WaitAsync();
            try
            {
                await RemoveAll();

                // Give Windows and the Handy time to release the previous
                // GATT session before scanning for the same device again.
                await Task.Delay(500);

                Keys = (Config.Key ?? string.Empty).Split(',')
                                 .Where(x => !string.IsNullOrWhiteSpace(x))
                                 .Select(x => x.Trim())
                                 .ToList();

                _logger.LogInformation(
                    "Starting initialization with {DeviceCount} device keys.",
                    Keys.Count);

                await ConnectAllCore();
            }
            finally
            {
                _connectLock.Release();
                timerReconnect.Start();
            }
        }

        internal async Task ConnectAll()
        {
            await _connectLock.WaitAsync();
            try
            {
                await ConnectAllCore();
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private async Task ConnectAllCore()
        {
            var internetTasks = Keys.Select(Connect);
            await Task.WhenAll(
                internetTasks.Append(ConnectBluetooth()));
        }

        private async Task ConnectBluetooth()
        {
            if (!_bluetoothClients.IsEmpty)
                return;

            var clients = await _bluetoothDiscovery.DiscoverAsync(
                TimeSpan.FromSeconds(8),
                CancellationToken.None);
            foreach (var client in clients)
            {
                if (!_bluetoothClients.TryAdd(client.Id, client))
                {
                    await client.DisposeAsync();
                    continue;
                }

                client.Disconnected += BluetoothClient_Disconnected;
                try
                {
                    await client.SetOffset(
                        Config.OffsetMS,
                        CancellationToken.None);
                    var funscriptRepository =
                        await _repositoryManager
                            .GetRepositoryAsync<FunscriptRepository>();
                    var handyDevice = new HandyV3Device(
                        client,
                        funscriptRepository,
                        _logger);

                    var added = false;
                    lock (devices)
                    {
                        if (!devices.ContainsKey(client.Id))
                        {
                            devices[client.Id] = handyDevice;
                            added = true;
                        }
                    }

                    if (!added)
                    {
                        client.Disconnected -=
                            BluetoothClient_Disconnected;
                        _bluetoothClients.TryRemove(client.Id, out _);
                        await client.DisposeAsync();
                        continue;
                    }

                    _deviceCollector.LoadDevice(handyDevice);
                    _logger.LogInformation(
                        "Loaded {DeviceName} from Bluetooth.",
                        handyDevice.Name);
                }
                catch (Exception ex)
                {
                    client.Disconnected -= BluetoothClient_Disconnected;
                    _bluetoothClients.TryRemove(client.Id, out _);
                    await client.DisposeAsync();
                    _logger.LogWarning(
                        ex,
                        "Could not load {DeviceName} from Bluetooth.",
                        client.DisplayName);
                }
            }
        }

        private async Task Connect(string key)
        {
            _logger.LogInformation(
                "Connecting to a configured Handy over internet.");

            var client = GetOrCreateClient(key);

            HttpResponseMessage resp;
            try
            {
                resp = await client.GetAsync("v2/connected");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "The configured internet Handy could not be reached.");
                Remove(key);
                return;
            }

            if (resp?.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.LogWarning(
                    "The configured internet Handy is not reachable.");
                Remove(key);
                return;
            }

            var status = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
            if (!status.connected)
            {
                _logger.LogWarning(
                    "The configured internet Handy is not connected.");
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
                    _logger.LogInformation(
                        "Creating an internet Handy with the HSP protocol.");
                    var funscriptRepository =
                        await _repositoryManager
                            .GetRepositoryAsync<FunscriptRepository>();
                    handyDevice = new HandyV3Device(
                        new HandyHttpClient(client),
                        funscriptRepository,
                        _logger);
                }
                else
                {
                    var indexRepository =
                        await _repositoryManager
                            .GetRepositoryAsync<IndexRepository>();
                    _ = await client.PutAsync(
                        "v2/mode",
                        new StringContent(
                            JsonConvert.SerializeObject(new ModeRequest(1)),
                            Encoding.UTF8,
                            "application/json"));
                    _logger.LogInformation(
                        "Creating an internet Handy with the legacy HSSP protocol.");
                    handyDevice = new HandyDevice(client, indexRepository, _logger);
                }

                lock (devices)
                {
                    devices[key] = handyDevice;
                    _deviceCollector.LoadDevice(handyDevice);
                    _logger.LogInformation(
                        "Loaded an internet Handy running firmware " +
                        "{FirmwareVersion}.",
                        firmwareVersion);
                }

                _= ServerTimeSync.SyncServerTimeAsync();
            }
        }

        private async Task RemoveAll()
        {
            _logger.LogInformation("Removing all devices.");
            List<IDevice> loadedDevices;
            lock (devices)
            {
                loadedDevices = devices.Values.ToList();
                devices.Clear();
            }

            foreach (var device in loadedDevices)
                _deviceCollector.UnloadDevice(device);

            _clients.Clear();
            _usesV3Api.Clear();
            var bluetoothClients = _bluetoothClients.ToArray();
            _bluetoothClients.Clear();
            foreach (var entry in bluetoothClients)
            {
                entry.Value.Disconnected -= BluetoothClient_Disconnected;
                await entry.Value.DisposeAsync();
            }
        }

        private void Remove(string key)
        {
            _clients.TryRemove(key, out var client);
            _usesV3Api.TryRemove(key, out _);

            lock (devices)
            {
                if (devices.TryGetValue(key, out var device))
                {
                    _deviceCollector.UnloadDevice(device);
                    devices.Remove(key);
                    _logger.LogInformation(
                        "Removed an unavailable internet Handy.");
                }
            }
        }

        private void BluetoothClient_Disconnected(IHandyClient client)
            => _ = ObserveBluetoothDisconnect(client);

        internal async Task ObserveBluetoothDisconnect(IHandyClient client)
        {
            await _connectLock.WaitAsync();
            try
            {
                if (!_bluetoothClients.TryGetValue(
                        client.Id,
                        out var currentClient)
                    || !ReferenceEquals(currentClient, client))
                {
                    return;
                }

                client.Disconnected -= BluetoothClient_Disconnected;
                if (!_bluetoothClients.TryRemove(client.Id, out _))
                    return;

                IDevice device = null;
                lock (devices)
                {
                    if (devices.TryGetValue(client.Id, out device))
                        devices.Remove(client.Id);
                }

                if (device is not null)
                    _deviceCollector.UnloadDevice(device);

                await client.DisposeAsync();
                _logger.LogInformation(
                    "A Bluetooth Handy disconnected; starting discovery again.");
                await ConnectAllCore();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not recover from a Bluetooth Handy disconnect.");
            }
            finally
            {
                _connectLock.Release();
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

        private async void TimerReconnect_Elapsed(
            object sender,
            ElapsedEventArgs e)
        {
            try
            {
                await ConnectAll();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Handy reconnection failed.");
            }
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
                var internetUpdates = clients.Select(entry =>
                    ApplyOffsetForClient(
                        entry.Key,
                        entry.Value,
                        offset));
                var bluetoothUpdates = _bluetoothClients.Values.Select(
                    client => client.SetOffset(
                        offset,
                        CancellationToken.None));
                await Task.WhenAll(
                    internetUpdates.Concat(bluetoothUpdates));

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
