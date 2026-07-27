using Edi.Core.Tests.Support;

namespace Edi.Core.Tests.Players;

public class DevicePlayerConcurrencyTests
{
    [Fact]
    public async Task LatestPendingCommandReplacesIntermediateCommands()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Device.PlayBehavior = (_, seek) =>
        {
            if (seek == 0)
            {
                firstStarted.TrySetResult();
                releaseFirst.Task.GetAwaiter().GetResult();
            }

            return Task.CompletedTask;
        };

        await rig.DevicePlayer.Play("scene", seek: 0);
        await firstStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        for (var seek = 1; seek < 30; seek++)
            await rig.DevicePlayer.Play("scene", seek);

        releaseFirst.TrySetResult();
        await rig.Device.WaitForPlayAsync("scene", seek: 29);
        var stopsBefore = rig.Device.Commands.Count(
            command => command.Kind == PlaybackCommandKind.Stop);
        await rig.DevicePlayer.Stop();
        await rig.Device.WaitForStopAsync(occurrence: stopsBefore + 1);

        Assert.Equal(
            [0, 29],
            rig.Device.Commands
                .Where(command => command.Kind == PlaybackCommandKind.Play)
                .Select(command => command.Seek));
    }

    [Fact]
    public async Task DeviceTaskFailureIsObservedInPlayerLogs()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var failureObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        rig.Device.PlayBehavior = (_, _) => Task.FromException(
            new InvalidOperationException("simulated transport failure"));
        rig.Logs.OnLogReceived += message =>
        {
            if (message.Contains("simulated transport failure"))
                failureObserved.TrySetResult();
        };

        await rig.DevicePlayer.Play("scene");
        await failureObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.True(failureObserved.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task HardPauseBlocksPlayUntilExplicitResume()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        await rig.DevicePlayer.Pause(untilResume: true);
        var playCountBefore = rig.Device.Commands.Count(
            command => command.Kind == PlaybackCommandKind.Play);

        await rig.DevicePlayer.Play("scene", seek: 300);
        Assert.Equal(
            playCountBefore,
            rig.Device.Commands.Count(command => command.Kind == PlaybackCommandKind.Play));

        await rig.DevicePlayer.Resume(atCurrentTime: false);

        var resumed = await rig.Device.WaitForPlayAsync("scene", occurrence: 1);
        Assert.Equal("scene", resumed.GalleryName);
        Assert.Equal(300, resumed.Seek);
    }

    [Fact]
    public async Task AddingRemovingAndPlayingConcurrentlyDoesNotCorruptDeviceCollection()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var devices = Enumerable.Range(0, 25)
            .Select(index => new RecordingDevice(rig.Funscripts, $"Device {index}"))
            .ToList();

        var mutations = devices.Select(device => Task.Run(async () =>
        {
            rig.DevicePlayer.Add(device);
            await rig.DevicePlayer.Play("scene");
            rig.DevicePlayer.Remove(device);
        }));

        await Task.WhenAll(mutations);
        var stopsBefore = rig.Device.Commands.Count(
            command => command.Kind == PlaybackCommandKind.Stop);
        await rig.DevicePlayer.Stop();
        await rig.Device.WaitForStopAsync(occurrence: stopsBefore + 1);

        Assert.Equal(PlaybackCommandKind.Stop, rig.Device.Commands[^1].Kind);
    }

    [Fact]
    public async Task SlowDeviceInvocationDoesNotDelayAnotherDevice()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        rig.DevicePlayer.Remove(rig.Device);

        var slowStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlow = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var slowDevice = new RecordingDevice(rig.Funscripts, "Slow")
        {
            PlayBehavior = (_, _) =>
            {
                slowStarted.TrySetResult();
                releaseSlow.Task.GetAwaiter().GetResult();
                return Task.CompletedTask;
            }
        };
        var fastDevice = new RecordingDevice(rig.Funscripts, "Fast");
        rig.DevicePlayer.Add(slowDevice);
        rig.DevicePlayer.Add(fastDevice);

        var play = Task.Run(
            () => rig.DevicePlayer.Play("scene"),
            TestContext.Current.CancellationToken);

        try
        {
            await slowStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(3),
                TestContext.Current.CancellationToken);
            await fastDevice.WaitForPlayAsync("scene", occurrence: 1);
        }
        finally
        {
            releaseSlow.TrySetResult();
            await play;
            await rig.DevicePlayer.Stop();
        }
    }
}
