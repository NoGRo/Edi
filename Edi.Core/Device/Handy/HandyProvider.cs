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
        private Timer timerReconnect = new Timer(40000);
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
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly object _initTaskLock = new();
        private Task _initTask = Task.CompletedTask;
        private int _expectedBluetoothDeviceCount;

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

        public async Task Disconnect()
        {
            timerReconnect.Stop();
            await _connectLock.WaitAsync();
            try
            {
                await RemoveAll();
            }
            finally
            {
                _connectLock.Release();
                timerReconnect.Stop();
            }
        }

        public async Task Refresh()
        {
            timerReconnect.Stop();
            await _connectLock.WaitAsync();
            try
            {
                await RemoveDeviceWrappers();
                LoadConfiguredKeys();

                var internetTasks = Keys.Select(Connect);
                await Task.WhenAll(
                    internetTasks.Append(RefreshBluetooth()));
            }
            finally
            {
                _connectLock.Release();
                timerReconnect.Start();
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
                LoadConfiguredKeys();

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

        private async Task RefreshBluetooth()
        {
            var retainedClients = _bluetoothClients.Values.ToArray();
            await Task.WhenAll(
                retainedClients.Select(RefreshBluetoothClient));

            await ConnectBluetooth(scanForNewDevices: true);
        }

        private async Task RefreshBluetoothClient(IHandyClient client)
        {
            try
            {
                await LoadBluetoothDevice(client);
            }
            catch (Exception ex)
            {
                if (_bluetoothClients.TryGetValue(
                        client.Id,
                        out var currentClient)
                    && ReferenceEquals(currentClient, client))
                {
                    _bluetoothClients.TryRemove(client.Id, out _);
                }

                await DisposeBluetoothClient(client);
                _logger.LogWarning(
                    ex,
                    "The retained Bluetooth connection for {DeviceName} " +
                    "is no longer usable; rediscovering it.",
                    client.DisplayName);
            }
        }

        private async Task ConnectBluetooth(
            bool scanForNewDevices = false)
        {
            var missingDeviceCount = GetMissingBluetoothDeviceCount();
            if (missingDeviceCount == 0
                && !_bluetoothClients.IsEmpty
                && !scanForNewDevices)
            {
                return;
            }

            var attempts = missingDeviceCount > 0 ? 3 : 1;
            var discoveryTimeout =
                scanForNewDevices && missingDeviceCount == 0
                    ? TimeSpan.FromSeconds(2)
                    : TimeSpan.FromSeconds(8);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                var clients = await _bluetoothDiscovery.DiscoverAsync(
                    discoveryTimeout,
                    missingDeviceCount,
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
                        await LoadBluetoothDevice(client);
                    }
                    catch (Exception ex)
                    {
                        client.Disconnected -=
                            BluetoothClient_Disconnected;
                        _bluetoothClients.TryRemove(client.Id, out _);
                        await client.DisposeAsync();
                        _logger.LogWarning(
                            ex,
                            "Could not load {DeviceName} from Bluetooth.",
                            client.DisplayName);
                    }
                }

                missingDeviceCount = GetMissingBluetoothDeviceCount();
                if (missingDeviceCount == 0
                    && !_bluetoothClients.IsEmpty)
                {
                    return;
                }

                if (attempt < attempts)
                {
                    _logger.LogInformation(
                        "The previous Bluetooth Handy has not resumed " +
                        "advertising yet; retrying discovery ({Attempt}/{Attempts}).",
                        attempt + 1,
                        attempts);
                }
            }
        }

        private async Task LoadBluetoothDevice(IHandyClient client)
        {
            lock (devices)
            {
                if (devices.ContainsKey(client.Id))
                    return;
            }

            var funscriptRepository =
                await _repositoryManager
                    .GetRepositoryAsync<FunscriptRepository>();
            var handyDevice = new HandyV3Device(
                client,
                funscriptRepository,
                _logger,
                defaultOffset: Config.OffsetMS);

            lock (devices)
            {
                if (devices.ContainsKey(client.Id))
                    return;

                devices[client.Id] = handyDevice;
            }

            _deviceCollector.LoadDevice(handyDevice);
            await handyDevice.OffsetUpdate;
            RememberBluetoothDeviceCount();
            _logger.LogInformation(
                "Loaded {DeviceName} from Bluetooth.",
                handyDevice.Name);
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
                        _logger,
                        defaultOffset: Config.OffsetMS);
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
                    handyDevice = new HandyDevice(
                        client,
                        indexRepository,
                        _logger,
                        defaultOffset: Config.OffsetMS);
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

                _ = ServerTimeSync.SyncServerTimeAsync();
            }
        }

        private async Task RemoveAll()
        {
            _logger.LogInformation("Removing all devices.");
            await RemoveDeviceWrappers();

            _clients.Clear();
            var bluetoothClients = _bluetoothClients.ToArray();
            _bluetoothClients.Clear();
            await Task.WhenAll(
                bluetoothClients.Select(
                    entry => DisposeBluetoothClient(entry.Value)));
        }

        private async Task RemoveDeviceWrappers()
        {
            _logger.LogInformation(
                "Recreating device wrappers while retaining transports.");
            List<IDevice> loadedDevices;
            lock (devices)
            {
                loadedDevices = devices.Values.ToList();
                devices.Clear();
            }

            foreach (var device in loadedDevices)
            {
                try
                {
                    await device.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Could not stop {DeviceName} before disconnecting.",
                        device.Name);
                }
                _deviceCollector.UnloadDevice(device);
            }
        }

        private void LoadConfiguredKeys()
        {
            Keys = (Config.Key ?? string.Empty).Split(',')
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .Select(x => x.Trim())
                             .ToList();
        }

        private async Task DisposeBluetoothClient(IHandyClient client)
        {
            client.Disconnected -= BluetoothClient_Disconnected;
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not release {DeviceName}.",
                    client.DisplayName);
            }
        }

        private void Remove(string key)
        {
            _clients.TryRemove(key, out var client);

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

        private int GetMissingBluetoothDeviceCount()
            => Math.Max(
                0,
                Volatile.Read(ref _expectedBluetoothDeviceCount)
                    - _bluetoothClients.Count);

        private void RememberBluetoothDeviceCount()
        {
            var connectedCount = _bluetoothClients.Count;
            var expectedCount =
                Volatile.Read(ref _expectedBluetoothDeviceCount);
            while (connectedCount > expectedCount)
            {
                var observed = Interlocked.CompareExchange(
                    ref _expectedBluetoothDeviceCount,
                    connectedCount,
                    expectedCount);
                if (observed == expectedCount)
                    return;

                expectedCount = observed;
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
