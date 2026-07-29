using Edi.Core.Device;
using Edi.Core.Gallery;
using Edi.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edi.Core.Tests.Devices;

public class DeviceBaseConcurrencyTests
{
    [Fact]
    public async Task NewerPlayWinsWhenPreviousLookupIsStillRunning()
    {
        var firstLookupStarted = NewSignal();
        var releaseFirstLookup = NewSignal();
        var repository = CreateRepository("first", "second");
        repository.Resolve = name =>
        {
            if (name == "first")
            {
                firstLookupStarted.TrySetResult();
                releaseFirstLookup.Task.GetAwaiter().GetResult();
            }

            return repository.Find(name);
        };
        var device = new TestDevice(repository);

        var firstPlayback = Task.Run(
            () => device.PlayGallery("first"),
            TestContext.Current.CancellationToken);
        await WaitAsync(firstLookupStarted.Task);

        var secondPlayback = device.PlayGallery("second");
        releaseFirstLookup.TrySetResult();

        await device.WaitForCommandAsync(
            command => command is { Kind: DeviceCommandKind.Play, GalleryName: "second" });
        await device.Stop();
        await Task.WhenAll(firstPlayback, secondPlayback);

        Assert.Equal(
            ["second"],
            device.Commands
                .Where(command => command.Kind == DeviceCommandKind.Play)
                .Select(command => command.GalleryName));
    }

    [Fact]
    public async Task StopWinsWhenGalleryLookupIsStillRunning()
    {
        var lookupStarted = NewSignal();
        var releaseLookup = NewSignal();
        var repository = CreateRepository("scene");
        repository.Resolve = name =>
        {
            lookupStarted.TrySetResult();
            releaseLookup.Task.GetAwaiter().GetResult();
            return repository.Find(name);
        };
        var device = new TestDevice(repository);

        var playback = Task.Run(
            () => device.PlayGallery("scene"),
            TestContext.Current.CancellationToken);
        await WaitAsync(lookupStarted.Task);

        var stop = device.Stop();
        releaseLookup.TrySetResult();
        await Task.WhenAll(playback, stop);

        Assert.Equal(DeviceCommandKind.Stop, device.Commands[^1].Kind);
        Assert.DoesNotContain(
            device.Commands,
            command => command.Kind == DeviceCommandKind.Play);
    }

    [Fact]
    public async Task ConcretePlaybackFailureIsReturnedToCaller()
    {
        var repository = CreateRepository("scene", duration: 50);
        var device = new TestDevice(repository)
        {
            PlayBehavior = (_, _, _) => Task.FromException(
                new InvalidOperationException("transport failed"))
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => device.PlayGallery("scene"));

        Assert.Equal("transport failed", error.Message);
    }

    [Fact]
    public async Task NewerPlayDoesNotWaitForCancelledPlaybackToFinish()
    {
        var firstStarted = NewSignal();
        var firstCancelled = NewSignal();
        var releaseFirst = NewSignal();
        var device = new TestDevice(CreateRepository("first", "second"));
        device.PlayBehavior = async (gallery, _, token) =>
        {
            if (gallery.Name != "first")
                return;

            using var registration = token.Register(
                () => firstCancelled.TrySetResult());
            firstStarted.TrySetResult();
            await releaseFirst.Task;
            token.ThrowIfCancellationRequested();
        };

        var first = device.PlayGallery("first");
        await WaitAsync(firstStarted.Task);
        var second = device.PlayGallery("second");

        try
        {
            await WaitAsync(firstCancelled.Task);
            await device.WaitForCommandAsync(
                command => command is
                    { Kind: DeviceCommandKind.Play, GalleryName: "second" });
            Assert.False(releaseFirst.Task.IsCompleted);
        }
        finally
        {
            releaseFirst.TrySetResult();
            await Task.WhenAll(first, second);
            await device.Stop();
        }
    }

    [Theory]
    [InlineData(1325, 125)]
    [InlineData(1200, 0)]
    [InlineData(1199, 0)]
    [InlineData(1100, 0)]
    public async Task LoopUsesZeroBeforeBoundaryAndOvershootAfterBoundary(
        int elapsedMilliseconds,
        int expectedSeek)
    {
        var now = new DateTime(
            2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);
        var firstDelayStarted = NewSignal();
        var releaseFirstDelay = NewSignal();
        var delayCalls = 0;

        async Task ControlledDelay(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref delayCalls) == 1)
            {
                Assert.Equal(TimeSpan.FromMilliseconds(1200), delay);
                firstDelayStarted.TrySetResult();
                await releaseFirstDelay.Task.WaitAsync(cancellationToken);
                return;
            }

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
        }

        var device = new TestDevice(
            CreateRepository("filler", duration: 1200, loop: true))
        {
            UtcNowBehavior = () => now,
            PlaybackDelayBehavior = ControlledDelay
        };

        try
        {
            await device.PlayGallery("filler");
            await WaitAsync(firstDelayStarted.Task);

            now = now.AddMilliseconds(elapsedMilliseconds);
            releaseFirstDelay.TrySetResult();

            var loop = await device.WaitForCommandAsync(
                command => command.Kind == DeviceCommandKind.Play,
                occurrence: 2);

            Assert.Equal(expectedSeek, loop.Seek);
        }
        finally
        {
            releaseFirstDelay.TrySetResult();
            await device.Stop();
        }
    }

    [Fact]
    public async Task StopGalleryReceivesUsableToken()
    {
        var device = new TestDevice(CreateRepository("scene"));

        await device.Stop();

        var stop = Assert.Single(
            device.Commands,
            command => command.Kind == DeviceCommandKind.Stop);
        Assert.False(stop.TokenWasCancelled);
    }

    [Fact]
    public void NoneVariantDoesNotApplyVariant()
    {
        var device = new TestDevice(CreateRepository("scene"));

        device.SelectedVariant = "None";

        Assert.Equal(0, device.SetVariantCalls);
    }

    [Fact]
    public async Task RangeIsAppliedBeforePlaybackResumes()
    {
        var applyRangeStarted = NewSignal();
        var releaseApplyRange = NewSignal();
        var device = new TestDevice(CreateRepository("scene", loop: true));
        var playback = device.PlayGallery("scene");
        await device.WaitForCommandAsync(
            command => command.Kind == DeviceCommandKind.Play);

        device.Min = 50;
        device.Max = 50;
        await device.WaitForCommandAsync(
            command => command.Kind == DeviceCommandKind.Stop);

        device.ApplyRangeBehavior = async () =>
        {
            applyRangeStarted.TrySetResult();
            await releaseApplyRange.Task;
        };

        try
        {
            device.Min = 0;
            device.Max = 100;
            await WaitAsync(applyRangeStarted.Task);

            Assert.Single(
                device.Commands,
                command => command.Kind == DeviceCommandKind.Play);

            releaseApplyRange.TrySetResult();
            await device.WaitForCommandAsync(
                command => command.Kind == DeviceCommandKind.Play,
                occurrence: 2);
        }
        finally
        {
            releaseApplyRange.TrySetResult();
            await device.Stop();
            await playback;
        }
    }

    private static TestGalleryRepository CreateRepository(
        string name,
        int duration = 10_000,
        bool loop = false)
        => CreateRepository([name], duration, loop);

    private static TestGalleryRepository CreateRepository(
        string first,
        string second,
        int duration = 10_000)
        => CreateRepository([first, second], duration, loop: false);

    private static TestGalleryRepository CreateRepository(
        IEnumerable<string> names,
        int duration,
        bool loop)
        => new(names.Select(name => new TestGallery
        {
            Name = name,
            Variant = "default",
            Duration = duration,
            Loop = loop
        }));

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitAsync(Task task)
        => await task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
}

internal enum DeviceCommandKind
{
    Play,
    Stop,
    ApplyRange
}

internal sealed record DeviceCommand(
    DeviceCommandKind Kind,
    string? GalleryName = null,
    long Seek = 0,
    bool TokenWasCancelled = false);

internal sealed class TestDevice(TestGalleryRepository repository)
    : DeviceBase<TestGalleryRepository, TestGallery>(
        repository,
        NullLogger.Instance)
{
    private readonly object commandLock = new();
    private readonly List<DeviceCommand> commands = [];
    private readonly SemaphoreSlim commandChanged = new(0);

    public Func<TestGallery, long, CancellationToken, Task>? PlayBehavior { get; set; }
    public Func<Task>? ApplyRangeBehavior { get; set; }
    public Func<DateTime>? UtcNowBehavior { get; set; }
    public Func<TimeSpan, CancellationToken, Task>? PlaybackDelayBehavior { get; set; }
    public int SetVariantCalls { get; private set; }

    public IReadOnlyList<DeviceCommand> Commands
    {
        get
        {
            lock (commandLock)
            {
                return commands.ToList();
            }
        }
    }

    public override Task PlayGallery(TestGallery gallery, long seek = 0)
        => PlayGallery(gallery, seek, playCancelTokenSource.Token);

    protected override Task PlayGallery(
        TestGallery gallery,
        long seek,
        CancellationToken cancellationToken)
    {
        Record(new(
            DeviceCommandKind.Play,
            gallery.Name,
            seek,
            cancellationToken.IsCancellationRequested));
        return PlayBehavior?.Invoke(gallery, seek, cancellationToken)
               ?? Task.CompletedTask;
    }

    public override Task StopGallery()
    {
        Record(new(
            DeviceCommandKind.Stop,
            TokenWasCancelled: playCancelTokenSource.Token.IsCancellationRequested));
        return Task.CompletedTask;
    }

    internal override Task applyRange()
    {
        Record(new(DeviceCommandKind.ApplyRange));
        return ApplyRangeBehavior?.Invoke() ?? Task.CompletedTask;
    }

    internal override void SetVariant()
        => SetVariantCalls++;

    internal override DateTime GetUtcNow()
        => UtcNowBehavior?.Invoke() ?? base.GetUtcNow();

    internal override Task PlaybackDelay(
        TimeSpan delay,
        CancellationToken cancellationToken)
        => PlaybackDelayBehavior?.Invoke(delay, cancellationToken)
           ?? base.PlaybackDelay(delay, cancellationToken);

    public async Task<DeviceCommand> WaitForCommandAsync(
        Func<DeviceCommand, bool> predicate,
        int occurrence = 1)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        while (true)
        {
            lock (commandLock)
            {
                var matches = commands.Where(predicate).ToList();
                if (matches.Count >= occurrence)
                    return matches[occurrence - 1];
            }

            await commandChanged.WaitAsync(timeout.Token);
        }
    }

    private void Record(DeviceCommand command)
    {
        lock (commandLock)
        {
            commands.Add(command);
        }

        commandChanged.Release();
    }
}

internal sealed class TestGalleryRepository(IEnumerable<TestGallery> galleries)
    : IGalleryRepository<TestGallery>
{
    private readonly Dictionary<string, TestGallery> galleries =
        galleries.ToDictionary(gallery => gallery.Name);

    public Func<string, TestGallery?>? Resolve { get; set; }
    public bool IsInitialized => true;
    public IEnumerable<string> Accept => [];

    public TestGallery Get(string name, string? variant = null)
        => Resolve?.Invoke(name) ?? Find(name)!;

    public TestGallery? Find(string name)
        => galleries.GetValueOrDefault(name);

    public List<TestGallery> GetAll() => galleries.Values.ToList();
    public List<string> GetVariants() => ["default"];
    public Task Init(string path) => Task.CompletedTask;
}

internal sealed class TestGallery : IGallery
{
    public required string Name { get; set; }
    public bool Loop { get; set; }
    public int Duration { get; set; }
    public required string Variant { get; set; }
}
