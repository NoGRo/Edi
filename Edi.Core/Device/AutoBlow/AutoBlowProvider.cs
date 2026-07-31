using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Index;
using Edi.Core.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Timers;

namespace Edi.Core.Device.AutoBlow;

internal sealed record AutoBlowDiscovery(bool IsVacuGlide, HttpClient Client);

public class AutoBlowProvider : IDeviceProvider
{
    private const string DefaultCluster = "https://latency.autoblowapi.com";
    private readonly ILogger _logger;
    private readonly System.Timers.Timer _timer = new(40000);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly Dictionary<string, AutoBlowDevice> _devices = new();
    private readonly RepositoryManager _repositoryManager;
    private readonly DeviceCollector _deviceCollector;
    private List<string> _keys = new();

    public HandyConfig Config { get; }

    public AutoBlowProvider(
        RepositoryManager repositoryManager,
        ConfigurationManager config,
        DeviceCollector deviceCollector,
        ILogger<AutoBlowProvider> logger)
    {
        _repositoryManager = repositoryManager;
        _deviceCollector = deviceCollector;
        _logger = logger;
        Config = config.Get<HandyConfig>();
        _timer.Elapsed += TimerElapsed;
    }

    public async Task Init()
    {
        _timer.Stop();
        _keys = ParseAutoBlowKeys(Config.Key);
        await ConnectAll();
        if (_keys.Count > 0)
            _timer.Start();
    }

    public async Task Disconnect()
    {
        _timer.Stop();
        await _connectLock.WaitAsync();
        try
        {
            RemoveAll();
            _keys.Clear();
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task ConnectAll()
    {
        if (!await _connectLock.WaitAsync(0))
            return;

        try
        {
            await Task.WhenAll(_keys.Select(Connect));
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task Connect(string key)
    {
        AutoBlowDevice current;
        lock (_devices)
            _devices.TryGetValue(key, out current);

        if (current != null && await IsConnected(current.Client))
            return;

        Remove(key);
        var discovery = await DiscoverAsync(key, NewClient);
        if (discovery == null)
            return;

        var repository =
            await _repositoryManager.GetRepositoryAsync<IndexRepository>();
        var device = discovery.IsVacuGlide
            ? new VacuGlide2Device(
                discovery.Client,
                repository,
                _logger,
                Config.OffsetMS)
            : new AutoBlowDevice(
                discovery.Client,
                repository,
                _logger,
                Config.OffsetMS);

        lock (_devices)
        {
            if (_devices.ContainsKey(key))
            {
                discovery.Client.Dispose();
                return;
            }

            _devices.Add(key, device);
        }

        _deviceCollector.LoadDevice(device);
    }

    private static async Task<bool> IsConnected(HttpClient client)
        => (await GetConnected(client))?.connected == true;

    internal static async Task<AutoBlowDiscovery> DiscoverAsync(
        string key,
        Func<string, string, bool, HttpClient> clientFactory)
    {
        foreach (var isVacuGlide in new[] { false, true })
        {
            using var probe = clientFactory(key, null, isVacuGlide);
            var connected = await GetConnected(probe);
            if (connected?.connected == true)
            {
                return new(
                    isVacuGlide,
                    clientFactory(key, connected.cluster, isVacuGlide));
            }
        }

        return null;
    }

    private static async Task<ConnectedResponse> GetConnected(
        HttpClient client)
    {
        try
        {
            using var response = await client.GetAsync("connected");
            if (!response.IsSuccessStatusCode)
                return null;

            return JsonConvert.DeserializeObject<ConnectedResponse>(
                await response.Content.ReadAsStringAsync());
        }
        catch
        {
            return null;
        }
    }

    private void RemoveAll()
    {
        string[] keys;
        lock (_devices)
            keys = _devices.Keys.ToArray();

        foreach (var key in keys)
            Remove(key);
    }

    private void Remove(string key)
    {
        AutoBlowDevice device;
        lock (_devices)
        {
            if (!_devices.Remove(key, out device))
                return;
        }

        _deviceCollector.UnloadDevice(device);
    }

    internal static List<string> ParseAutoBlowKeys(string value)
        => (value ?? string.Empty)
            .Split(',')
            .Select(key => key.Trim())
            .Where(key => key.Length == 12)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static HttpClient NewClient(string key, string cluster = null)
        => NewClient(key, cluster, false, null);

    internal static HttpClient NewClient(
        string key,
        string cluster,
        bool isVacuGlide)
        => NewClient(key, cluster, isVacuGlide, null);

    internal static HttpClient NewClient(
        string key,
        string cluster,
        bool isVacuGlide,
        HttpMessageHandler handler)
    {
        var root = NormalizeCluster(cluster);
        var client = handler == null
            ? new HttpClient()
            : new HttpClient(handler);
        client.BaseAddress = new Uri(
            root,
            isVacuGlide ? "vacuglide/" : "autoblow/");
        client.DefaultRequestHeaders.Add("x-device-token", key);
        return client;
    }

    private static Uri NormalizeCluster(string cluster)
    {
        var value = string.IsNullOrWhiteSpace(cluster)
            ? DefaultCluster
            : cluster.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            uri = new Uri($"https://{value}");

        return new UriBuilder(uri)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    private async void TimerElapsed(object sender, ElapsedEventArgs e)
    {
        try
        {
            await ConnectAll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Autoblow reconnect failed.");
        }
    }
}
