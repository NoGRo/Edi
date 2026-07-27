using Edi.Core.Device;
using Edi.Core.Device.Handy;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edi.Core.Tests.Handy;

public class HandyProviderReconnectTests
{
    [Fact]
    public async Task ConcurrentInitializationsShareOneDiscovery()
    {
        var discovery = new BlockingDiscovery();
        await using var rig = await ProviderRig.CreateAsync(discovery);

        var first = rig.Provider.Init();
        await discovery.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        var second = rig.Provider.Init();

        Assert.Same(first, second);
        Assert.Equal(1, discovery.Calls);

        discovery.Release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, discovery.Calls);
    }

    [Fact]
    public async Task ReconnectWaitsForActiveDiscoveryInsteadOfBeingDropped()
    {
        var firstClient = new FakeHandyClient("same-device");
        var replacementClient = new FakeHandyClient("same-device");
        var discovery = new SequencedDiscovery(
            firstClient,
            replacementClient);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();
        Assert.Single(rig.Collector.Devices);

        firstClient.RaiseDisconnected();
        await discovery.SecondStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        var queuedReconnect = rig.Provider.ConnectAll();

        Assert.False(queuedReconnect.IsCompleted);

        discovery.ReleaseSecond.SetResult();
        await discovery.ThirdStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
        await queuedReconnect.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, discovery.Calls);
        Assert.True(firstClient.WasDisposed);
        Assert.Collection(
            rig.Collector.Devices,
            device => Assert.Equal(
                replacementClient.DisplayName,
                device.Name));
    }

    [Fact]
    public async Task LateDisconnectFromOldClientDoesNotRemoveReplacement()
    {
        var firstClient = new FakeHandyClient("same-device");
        var replacementClient = new FakeHandyClient("same-device");
        var discovery = new QueueDiscovery(
            [firstClient],
            [replacementClient]);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();
        await rig.Provider.Init();

        await rig.Provider.ObserveBluetoothDisconnect(firstClient);

        Assert.False(replacementClient.WasDisposed);
        Assert.Collection(
            rig.Collector.Devices,
            device => Assert.Equal(
                replacementClient.DisplayName,
                device.Name));
    }

    [Fact]
    public async Task ExplicitDisconnectReleasesClientBeforeReconnect()
    {
        var firstClient = new FakeHandyClient("same-device");
        var replacementClient = new FakeHandyClient("same-device");
        var discovery = new QueueDiscovery(
            [firstClient],
            [replacementClient]);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();

        await rig.Provider.Disconnect();

        Assert.True(firstClient.WasDisposed);
        Assert.Empty(rig.Collector.Devices);

        await rig.Provider.Init();

        Assert.False(replacementClient.WasDisposed);
        Assert.Collection(
            rig.Collector.Devices,
            device => Assert.Equal(
                replacementClient.DisplayName,
                device.Name));
    }

    [Fact]
    public async Task ExplicitDisconnectRetriesUntilBluetoothDeviceReturns()
    {
        var firstClient = new FakeHandyClient("same-device");
        var replacementClient = new FakeHandyClient("same-device");
        var discovery = new CountingQueueDiscovery(
            [firstClient],
            [],
            [replacementClient]);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();
        await rig.Provider.Disconnect();
        await rig.Provider.Init();

        Assert.Equal(3, discovery.Calls);
        Assert.True(firstClient.WasDisposed);
        Assert.Collection(
            rig.Collector.Devices,
            device => Assert.Equal(
                replacementClient.DisplayName,
                device.Name));
    }

    private sealed class BlockingDiscovery : IHandyBluetoothDiscovery
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = NewCompletion();
        public TaskCompletionSource Release { get; } = NewCompletion();

        public async Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return [];
        }
    }

    private sealed class SequencedDiscovery(
        IHandyClient firstClient,
        IHandyClient replacementClient)
        : IHandyBluetoothDiscovery
    {
        public int Calls { get; private set; }
        public TaskCompletionSource SecondStarted { get; } =
            NewCompletion();
        public TaskCompletionSource ReleaseSecond { get; } =
            NewCompletion();
        public TaskCompletionSource ThirdStarted { get; } =
            NewCompletion();

        public async Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls++;
            switch (Calls)
            {
                case 1:
                    return [firstClient];
                case 2:
                    SecondStarted.TrySetResult();
                    await ReleaseSecond.Task.WaitAsync(cancellationToken);
                    return [];
                case 3:
                    ThirdStarted.TrySetResult();
                    return [replacementClient];
                default:
                    return [];
            }
        }
    }

    private sealed class QueueDiscovery(
        params IReadOnlyList<IHandyClient>[] results)
        : IHandyBluetoothDiscovery
    {
        private readonly Queue<IReadOnlyList<IHandyClient>> _results =
            new(results);

        public Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
            => Task.FromResult(
                _results.Count > 0
                    ? _results.Dequeue()
                    : []);
    }

    private sealed class CountingQueueDiscovery(
        params IReadOnlyList<IHandyClient>[] results)
        : IHandyBluetoothDiscovery
    {
        private readonly Queue<IReadOnlyList<IHandyClient>> _results =
            new(results);

        public int Calls { get; private set; }

        public Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(
                _results.Count > 0
                    ? _results.Dequeue()
                    : []);
        }
    }

    private sealed class FakeHandyClient(string id) : IHandyClient
    {
        private static readonly HspState State = new(
            stream_id: 1,
            max_points: 200,
            points: 0,
            current_point: 0,
            current_time: 0,
            loop: false,
            playback_rate: 1,
            first_point_time: 0,
            last_point_time: 0,
            play_state: "stopped",
            tail_point_stream_index: 0,
            tail_point_stream_index_threshold: 0);

        public string Id { get; } = id;
        public string Key => string.Empty;
        public string DisplayName => "The Handy 2 Pro (BLE)";
        public int MaxPointsPerRequest => 50;
        public bool WasDisposed { get; private set; }

        public event Action<IHandyClient> Disconnected = delegate { };

        public void RaiseDisconnected() => Disconnected(this);

        public Task<HspState> Setup(
            HspSetupRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(State);

        public Task<HspState> AddPoints(
            HspAddRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(State);

        public Task<HspState> Play(
            HspPlayRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(State);

        public Task Stop(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetStroke(
            SlideRequest request,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetOffset(
            int offset,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProviderRig : IAsyncDisposable
    {
        private readonly PlayerTestRig _playerRig;
        private readonly ServiceProvider _services;

        private ProviderRig(
            PlayerTestRig playerRig,
            ServiceProvider services,
            DeviceCollector collector,
            HandyProvider provider)
        {
            _playerRig = playerRig;
            _services = services;
            Collector = collector;
            Provider = provider;
        }

        public DeviceCollector Collector { get; }
        public HandyProvider Provider { get; }

        public static async Task<ProviderRig> CreateAsync(
            IHandyBluetoothDiscovery discovery)
        {
            var playerRig = await PlayerTestRig.CreateAsync(
                addDefaultDevice: false);
            var services = new ServiceCollection();
            services.AddHttpClient();
            services.AddSingleton(playerRig.Configuration);
            services.AddSingleton(playerRig.Definitions);
            services.AddSingleton<ILogger<FunscriptRepository>>(
                NullLogger<FunscriptRepository>.Instance);
            var serviceProvider = services.BuildServiceProvider();
            var repositoryManager = new RepositoryManager(
                serviceProvider,
                playerRig.Definitions);
            await repositoryManager.ChangePath(
                playerRig.TemporaryDirectory);
            var collector = new DeviceCollector(
                playerRig.Configuration,
                serviceProvider);
            var provider = new HandyProvider(
                repositoryManager,
                playerRig.Configuration,
                collector,
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                discovery,
                NullLogger<HandyProvider>.Instance);

            return new ProviderRig(
                playerRig,
                serviceProvider,
                collector,
                provider);
        }

        public async ValueTask DisposeAsync()
        {
            _services.Dispose();
            await _playerRig.DisposeAsync();
        }
    }

    private static TaskCompletionSource NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
