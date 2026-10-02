using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Players.Strategies;
using Edi.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edi.Core.Tests.Services;

public class EdiLaunchTests
{
    [Fact]
    public async Task SelectingPlayerFolderAppliesSparseAutorunConfigToExistingCoreAndWpfConfigObject()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var edi = Create(rig, services);
        var cachedConfig = edi.Config;
        cachedConfig.AutoLaunch = false;
        cachedConfig.ExecuteOnReady = "";
        var cachedGallery = rig.Configuration.Get<GalleryConfig>();
        var playerFolder = Path.Combine(rig.TemporaryDirectory, "IndependentPlayer");
        Directory.CreateDirectory(playerFolder);
        await File.WriteAllTextAsync(Path.Combine(playerFolder, "EdiConfig.json"), """
            { "Edi": { "AutoLaunch": true, "ExecuteOnReady": "http://127.0.0.1:5000/" },
              "Gallery": { "GalleryPath": "./" } }
            """, TestContext.Current.CancellationToken);
        edi.DeviceCollector.LoadDevice(rig.Device);
        var launches = 0;
        edi.StartGameProcess = info => {
            Assert.Equal("http://127.0.0.1:5000/", info.FileName);
            launches++;
        };
        await edi.SelectGame(new("EDI Player", playerFolder));
        Assert.Same(cachedConfig, edi.Config);
        Assert.Same(cachedConfig, rig.Configuration.Get<EdiConfig>());
        Assert.Same(cachedGallery, rig.Configuration.Get<GalleryConfig>());
        Assert.True(edi.Config.AutoLaunch);
        Assert.Equal("http://127.0.0.1:5000/", edi.Config.ExecuteOnReady);
        Assert.True(edi.LaunchGame(automatic: true));
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task CoreHostedServiceLaunchesWhenDeviceBecomesReadyWithoutWpf()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var edi = Create(rig, services);
        rig.Device.IsReady = false;
        edi.DeviceCollector.LoadDevice(rig.Device);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = 0;
        edi.StartGameProcess = _ => { Interlocked.Increment(ref launches); opened.SetResult(); };
        var app = WebApplication.CreateBuilder().Build();
        app.Urls.Add("http://127.0.0.1:0");
        using var hosted = new EdiHostedService(edi, NullLogger<EdiHostedService>.Instance, app);
        var token = TestContext.Current.CancellationToken;
        try
        {
            await hosted.StartAsync(token);
            Assert.False(opened.Task.IsCompleted);
            rig.Device.IsReady = true;
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        finally { await hosted.StopAsync(token); }
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task ReadyDeviceEnablesOneAutomaticLaunchAndManualLaunchSuppressesTheNextAutomaticAttempt()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var edi = Create(rig, services);
        var launches = 0;
        edi.StartGameProcess = info => { Assert.Equal("http://127.0.0.1:5000/", info.FileName); launches++; };
        Assert.False(edi.LaunchGame(automatic: true));
        rig.Device.IsReady = false;
        edi.DeviceCollector.LoadDevice(rig.Device);
        Assert.False(edi.LaunchGame(automatic: true));
        rig.Device.IsReady = true;
        Assert.True(edi.LaunchGame(automatic: true));
        rig.Device.IsReady = false;
        rig.Device.IsReady = true;
        Assert.False(edi.LaunchGame(automatic: true));
        Assert.Equal(1, launches);
        await edi.Init(rig.TemporaryDirectory);
        Assert.True(edi.LaunchGame());
        Assert.False(edi.LaunchGame(automatic: true));
        Assert.Equal(2, launches);
    }

    [Fact]
    public async Task FailedLaunchCanRetryAndConcurrentReadyChecksLaunchOnce()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var edi = Create(rig, services);
        edi.DeviceCollector.LoadDevice(rig.Device);
        edi.StartGameProcess = _ => throw new InvalidOperationException("Shell unavailable");
        Assert.Throws<InvalidOperationException>(() => edi.LaunchGame(automatic: true));
        var launches = 0;
        edi.StartGameProcess = _ => Interlocked.Increment(ref launches);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => edi.LaunchGame(automatic: true))));
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task AutoLaunchOptOutStillAllowsManualLaunch()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var edi = Create(rig, services);
        edi.Config.AutoLaunch = false;
        edi.DeviceCollector.LoadDevice(rig.Device);
        var launches = 0;
        edi.StartGameProcess = _ => launches++;
        Assert.False(edi.LaunchGame(automatic: true));
        Assert.True(edi.LaunchGame());
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task UploadReloadDoesNotRelaunchButSelectingAGameDoes()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var edi = Create(rig, services);
        edi.DeviceCollector.LoadDevice(rig.Device);
        var launches = 0;
        edi.StartGameProcess = _ => launches++;
        Assert.True(edi.LaunchGame(automatic: true));
        await edi.Init(rig.TemporaryDirectory, setGamePath: false);
        Assert.False(edi.LaunchGame(automatic: true));
        await edi.SelectGame(new("Test game", rig.TemporaryDirectory));
        Assert.True(edi.LaunchGame(automatic: true));
        Assert.Equal(2, launches);
    }

    [Fact]
    public async Task ReadinessDuringGameInitializationDoesNotLaunchUntilReloadCompletes()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var edi = Create(rig, services);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        edi.DeviceCollector.Providers.Add(new PendingProvider(async () => {
            edi.DeviceCollector.LoadDevice(rig.Device);
            entered.SetResult();
            await release.Task;
        }));
        edi.StartGameProcess = _ => { };
        var init = edi.Init(rig.TemporaryDirectory);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(edi.LaunchGame(automatic: true));
        release.SetResult();
        await init;
        Assert.True(edi.LaunchGame(automatic: true));
    }

    private static global::Edi.Core.Edi Create(PlayerTestRig rig, IServiceProvider services)
    {
        var collector = new DeviceCollector(rig.Configuration, services);
        var edi = new global::Edi.Core.Edi(collector, new CompositePlayer(), rig.Definitions,
            new RepositoryManager(services, rig.Definitions), rig.Configuration, null!, rig.Logs);
        edi.Config.AutoLaunch = true;
        edi.Config.ExecuteOnReady = "http://127.0.0.1:5000/";
        return edi;
    }

    private sealed class PendingProvider(Func<Task> init) : IDeviceProvider
    {
        public Task Init() => init();
    }
}
