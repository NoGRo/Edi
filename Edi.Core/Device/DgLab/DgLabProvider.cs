using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Services;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.DgLab;

public sealed class DgLabProvider : IDeviceProvider
{
    private readonly RepositoryManager repositoryManager;
    private readonly DeviceCollector deviceCollector;
    private readonly IDgLabDiscovery discovery;
    private readonly ILogger logger;
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly object initLock = new();
    private readonly Dictionary<string, IDgLabController> controllers =
        new();
    private readonly Dictionary<string, DgLabDevice[]> devices = new();
    private readonly Timer reconnectTimer;
    private Task initTask = Task.CompletedTask;

    public DgLabProvider(
        RepositoryManager repositoryManager,
        ConfigurationManager configuration,
        DeviceCollector deviceCollector,
        IDgLabDiscovery discovery,
        ILogger<DgLabProvider> logger)
    {
        this.repositoryManager = repositoryManager;
        this.deviceCollector = deviceCollector;
        this.discovery = discovery;
        this.logger = logger;
        Config = configuration.Get<DgLabConfig>();
        reconnectTimer = new Timer
        {
            AutoReset = false
        };
        reconnectTimer.Elapsed += ReconnectTimer_Elapsed;
    }

    public DgLabConfig Config { get; }

    public Task Init()
    {
        lock (initLock)
        {
            if (!initTask.IsCompleted)
                return initTask;

            initTask = Initialize();
            return initTask;
        }
    }

    public async Task Disconnect()
    {
        reconnectTimer.Stop();
        await lifecycleLock.WaitAsync();
        try
        {
            await RemoveAll();
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task Refresh()
    {
        await Disconnect();
        await Init();
    }

    private async Task Initialize()
    {
        if (!Config.Enabled)
            return;

        reconnectTimer.Stop();
        await lifecycleLock.WaitAsync();
        try
        {
            await ConnectAvailable();
        }
        finally
        {
            lifecycleLock.Release();
        }

        ScheduleReconnect();
    }

    private async Task ConnectAvailable()
    {
        var timeout = TimeSpan.FromSeconds(
            Math.Clamp(Config.DiscoverySeconds, 1, 30));
        var discovered = await discovery.DiscoverAsync(
            timeout,
            CancellationToken.None);
        foreach (var controller in discovered)
        {
            lock (controllers)
            {
                if (controllers.ContainsKey(controller.Id))
                {
                    _ = controller.DisposeAsync();
                    continue;
                }

                controllers[controller.Id] = controller;
            }

            controller.Disconnected += Controller_Disconnected;
            try
            {
                await LoadController(controller);
            }
            catch
            {
                controller.Disconnected -= Controller_Disconnected;
                lock (controllers)
                    controllers.Remove(controller.Id);
                await controller.DisposeAsync();
                throw;
            }
        }
    }

    private async Task LoadController(IDgLabController controller)
    {
        var repository = await repositoryManager
            .GetRepositoryAsync<FunscriptRepository>();
        var channelDevices = new[]
        {
            new DgLabDevice(
                controller,
                DgLabChannel.A,
                repository,
                logger),
            new DgLabDevice(
                controller,
                DgLabChannel.B,
                repository,
                logger)
        };
        lock (devices)
            devices[controller.Id] = channelDevices;

        foreach (var device in channelDevices)
            deviceCollector.LoadDevice(device);
    }

    private void Controller_Disconnected(IDgLabController controller)
        => _ = ObserveDisconnect(controller);

    private async Task ObserveDisconnect(IDgLabController controller)
    {
        await lifecycleLock.WaitAsync();
        try
        {
            lock (controllers)
            {
                if (!controllers.TryGetValue(
                        controller.Id,
                        out var current)
                    || !ReferenceEquals(current, controller))
                {
                    return;
                }

                controllers.Remove(controller.Id);
            }

            controller.Disconnected -= Controller_Disconnected;
            UnloadDevices(controller.Id);
            await controller.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not cleanly unload a disconnected DG-Lab PowerBox.");
        }
        finally
        {
            lifecycleLock.Release();
            ScheduleReconnect();
        }
    }

    private async Task RemoveAll()
    {
        IDgLabController[] controllerSnapshot;
        lock (controllers)
        {
            controllerSnapshot = controllers.Values.ToArray();
            controllers.Clear();
        }

        foreach (var controller in controllerSnapshot)
        {
            controller.Disconnected -= Controller_Disconnected;
            UnloadDevices(controller.Id);
        }

        await Task.WhenAll(
            controllerSnapshot.Select(async controller =>
                await controller.DisposeAsync()));
    }

    private void UnloadDevices(string controllerId)
    {
        DgLabDevice[] removed;
        lock (devices)
        {
            if (!devices.Remove(controllerId, out removed))
                return;
        }

        foreach (var device in removed)
            deviceCollector.UnloadDevice(device);
    }

    private void ScheduleReconnect()
    {
        if (!Config.Enabled)
            return;

        lock (controllers)
        {
            if (controllers.Count > 0)
                return;
        }

        reconnectTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(Config.ReconnectSeconds, 5, 300))
            .TotalMilliseconds;
        reconnectTimer.Stop();
        reconnectTimer.Start();
    }

    private async void ReconnectTimer_Elapsed(
        object sender,
        System.Timers.ElapsedEventArgs e)
    {
        try
        {
            await Init();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "DG-Lab Bluetooth reconnection failed.");
            ScheduleReconnect();
        }
    }
}
