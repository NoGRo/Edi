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
    public async Task RefreshRecreatesDeviceWithoutReleasingBluetoothClient()
    {
        var client = new FakeHandyClient("same-device");
        var discovery = new QueueDiscovery(
            [client],
            []);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();
        var originalDevice = Assert.Single(rig.Collector.Devices);

        await rig.Provider.Refresh();

        var refreshedDevice = Assert.Single(rig.Collector.Devices);
        Assert.NotSame(originalDevice, refreshedDevice);
        Assert.False(client.WasDisposed);
        Assert.Equal([0, 0], discovery.ExpectedDeviceCounts);
    }

    [Fact]
    public async Task RefreshRetainsEveryConnectedBluetoothClient()
    {
        var firstClient = new FakeHandyClient("first-device");
        var secondClient = new FakeHandyClient("second-device");
        var discovery = new QueueDiscovery(
            [firstClient, secondClient],
            []);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();
        await rig.Provider.Refresh();

        Assert.Equal(2, rig.Collector.Devices.Count);
        Assert.False(firstClient.WasDisposed);
        Assert.False(secondClient.WasDisposed);
    }

    [Fact]
    public async Task RefreshReplacesOnlyRetainedClientThatNoLongerResponds()
    {
        var staleClient = new FakeHandyClient(
            "stale-device",
            failOffset: true);
        var healthyClient = new FakeHandyClient("healthy-device");
        var replacementClient = new FakeHandyClient("stale-device");
        var discovery = new QueueDiscovery(
            [staleClient, healthyClient],
            [replacementClient]);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        staleClient.FailOffset = false;
        await rig.Provider.Init();
        staleClient.FailOffset = true;

        await rig.Provider.Refresh();

        Assert.True(staleClient.WasDisposed);
        Assert.False(healthyClient.WasDisposed);
        Assert.False(replacementClient.WasDisposed);
        Assert.Equal(2, rig.Collector.Devices.Count);
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

    [Fact]
    public async Task ExplicitDisconnectReleasesBluetoothClientsConcurrently()
    {
        var releaseDisposals = NewCompletion();
        var firstClient = new FakeHandyClient(
            "first-device",
            releaseDisposals.Task);
        var secondClient = new FakeHandyClient(
            "second-device",
            releaseDisposals.Task);
        var discovery = new QueueDiscovery(
            [firstClient, secondClient]);
        await using var rig = await ProviderRig.CreateAsync(discovery);
        await rig.Provider.Init();

        var disconnect = rig.Provider.Disconnect();
        await Task.WhenAll(
                firstClient.DisposeStarted.Task,
                secondClient.DisposeStarted.Task)
            .WaitAsync(
                TimeSpan.FromSeconds(3),
                TestContext.Current.CancellationToken);

        Assert.False(disconnect.IsCompleted);
        releaseDisposals.SetResult();
        await disconnect;

        Assert.True(firstClient.WasDisposed);
        Assert.True(secondClient.WasDisposed);
    }

    [Fact]
    public async Task IntentionalReconnectWaitsForEveryKnownBluetoothHandy()
    {
        var firstClient = new FakeHandyClient("first-device");
        var secondClient = new FakeHandyClient("second-device");
        var firstReplacement = new FakeHandyClient(
            "first-replacement");
        var secondReplacement = new FakeHandyClient(
            "second-replacement");
        var discovery = new QueueDiscovery(
            [firstClient, secondClient],
            [firstReplacement, secondReplacement]);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();
        await rig.Provider.Disconnect();
        await rig.Provider.Init();

        Assert.Equal([0, 2], discovery.ExpectedDeviceCounts);
        Assert.Equal(2, rig.Collector.Devices.Count);
        Assert.True(firstClient.WasDisposed);
        Assert.True(secondClient.WasDisposed);
        Assert.False(firstReplacement.WasDisposed);
        Assert.False(secondReplacement.WasDisposed);
    }

    [Fact]
    public async Task BluetoothDropRetriesUntilDeviceAdvertisesAgain()
    {
        var firstClient = new FakeHandyClient("same-device");
        var replacementClient = new FakeHandyClient("same-device");
        var discovery = new CountingQueueDiscovery(
            [firstClient],
            [],
            [replacementClient]);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();
        await rig.Provider.ObserveBluetoothDisconnect(firstClient);

        Assert.Equal(3, discovery.Calls);
        Assert.True(firstClient.WasDisposed);
        Assert.Collection(
            rig.Collector.Devices,
            device => Assert.Equal(
                replacementClient.DisplayName,
                device.Name));
    }

    [Fact]
    public async Task DroppingOneBluetoothHandyKeepsAndRecoversTheOthers()
    {
        var firstClient = new FakeHandyClient("first-device");
        var secondClient = new FakeHandyClient("second-device");
        var replacementClient = new FakeHandyClient("first-device");
        var discovery = new QueueDiscovery(
            [firstClient, secondClient],
            [replacementClient]);
        await using var rig = await ProviderRig.CreateAsync(discovery);

        await rig.Provider.Init();
        await rig.Provider.ObserveBluetoothDisconnect(firstClient);

        Assert.True(firstClient.WasDisposed);
        Assert.False(secondClient.WasDisposed);
        Assert.False(replacementClient.WasDisposed);
        Assert.Equal(
            2,
            rig.Collector.Devices.Count);
        Assert.Contains(
            rig.Collector.Devices,
            device => device.Name == secondClient.DisplayName);
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

        public List<int> ExpectedDeviceCounts { get; } = [];

        public Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
            => Task.FromResult(
                _results.Count > 0
                    ? _results.Dequeue()
                    : []);

        public Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
            TimeSpan timeout,
            int expectedDeviceCount,
            CancellationToken cancellationToken)
        {
            ExpectedDeviceCounts.Add(expectedDeviceCount);
            return DiscoverAsync(timeout, cancellationToken);
        }
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

    private sealed class FakeHandyClient : IHandyClient
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

        private readonly Task? _disposeRelease;

        public FakeHandyClient(
            string id,
            Task? disposeRelease = null,
            bool failOffset = false)
        {
            Id = id;
            _disposeRelease = disposeRelease;
            FailOffset = failOffset;
        }

        public string Id { get; }
        public string Key => string.Empty;
        public string DisplayName => "The Handy 2 Pro (BLE)";
        public int MaxPointsPerRequest => 50;
        public bool WasDisposed { get; private set; }
        public bool FailOffset { get; set; }
        public TaskCompletionSource DisposeStarted { get; } =
            NewCompletion();

        public event Action<IHandyClient> Disconnected = delegate { };

        public void RaiseDisconnected() => Disconnected(this);

        public Task SynchronizeClock(CancellationToken cancellationToken)
            => Task.CompletedTask;

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

        public Task<HspState> SyncTime(
            HspSyncTimeRequest request,
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
            => FailOffset
                ? Task.FromException(
                    new InvalidOperationException(
                        "The simulated GATT connection is stale."))
                : Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            WasDisposed = true;
            DisposeStarted.TrySetResult();
            if (_disposeRelease is not null)
                await _disposeRelease;
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
