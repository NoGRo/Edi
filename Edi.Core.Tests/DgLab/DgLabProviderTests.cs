using Edi.Core.Device;
using Edi.Core.Device.DgLab;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edi.Core.Tests.DgLab;

public class DgLabProviderTests
{
    [Fact]
    public async Task OnePowerBoxLoadsIndependentAAndBDevices()
    {
        var controller = new FakeController();
        await using var rig = await ProviderRig.Create(controller);

        await rig.Provider.Init();

        Assert.Collection(
            rig.Collector.Devices.OrderBy(device => device.Name),
            device => Assert.EndsWith(" A", device.Name),
            device => Assert.EndsWith(" B", device.Name));
        Assert.All(
            rig.Collector.Devices,
            device => Assert.IsType<DgLabDevice>(device));

        await rig.Provider.Disconnect();

        Assert.Empty(rig.Collector.Devices);
        Assert.True(controller.Disposed);
    }

    [Fact]
    public async Task DeviceObservesItsFamilyConfigurationProperty()
    {
        var controller = new FakeController();
        await using var rig = await ProviderRig.Create(controller);
        await rig.Provider.Init();
        var device = Assert.IsType<DgLabDevice>(
            rig.Collector.Devices.First());
        var configuration = rig.Configuration
            .Get<DgLabDevicesConfig>()
            .Devices[device.Name];

        configuration.PowerMin = 3000;

        Assert.Equal(
            DgLabChannelConfig.MaximumPower,
            device.DgLabConfiguration.PowerMin);
    }

    private sealed class FakeDiscovery(IDgLabController controller)
        : IDgLabDiscovery
    {
        private bool returned;

        public Task<IReadOnlyList<IDgLabController>> DiscoverAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (returned)
                return Task.FromResult<IReadOnlyList<IDgLabController>>([]);

            returned = true;
            return Task.FromResult<IReadOnlyList<IDgLabController>>(
                [controller]);
        }
    }

    private sealed class FakeController : IDgLabController
    {
        public string Id => "fake-powerbox";
        public string Name => "DG-Lab PowerBox 2.0";
        public bool IsConnected => !Disposed;
        public bool Disposed { get; private set; }
        public event Action<IDgLabController> Disconnected = delegate { };

        public Task SetPower(
            DgLabChannel channel,
            int power,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteWaveform(
            DgLabChannel channel,
            DgLabWaveform waveform,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task Stop(
            DgLabChannel channel,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProviderRig : IAsyncDisposable
    {
        private readonly PlayerTestRig playerRig;
        private readonly ServiceProvider services;

        private ProviderRig(
            PlayerTestRig playerRig,
            ServiceProvider services,
            DeviceCollector collector,
            DgLabProvider provider)
        {
            this.playerRig = playerRig;
            this.services = services;
            Collector = collector;
            Provider = provider;
        }

        public DeviceCollector Collector { get; }
        public DgLabProvider Provider { get; }
        public global::Edi.Core.Services.ConfigurationManager Configuration =>
            playerRig.Configuration;

        public static async Task<ProviderRig> Create(
            IDgLabController controller)
        {
            var playerRig = await PlayerTestRig.CreateAsync(
                addDefaultDevice: false);
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(playerRig.Configuration);
            serviceCollection.AddSingleton(playerRig.Definitions);
            serviceCollection.AddSingleton<ILogger<FunscriptRepository>>(
                NullLogger<FunscriptRepository>.Instance);
            var services = serviceCollection.BuildServiceProvider();
            var repositoryManager = new RepositoryManager(
                services,
                playerRig.Definitions);
            await repositoryManager.ChangePath(
                playerRig.TemporaryDirectory);
            var collector = new DeviceCollector(
                playerRig.Configuration,
                services);
            var provider = new DgLabProvider(
                repositoryManager,
                playerRig.Configuration,
                collector,
                new FakeDiscovery(controller),
                NullLogger<DgLabProvider>.Instance);
            provider.Config.ReconnectSeconds = 300;
            return new ProviderRig(
                playerRig,
                services,
                collector,
                provider);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.Disconnect();
            services.Dispose();
            await playerRig.DisposeAsync();
        }
    }
}
