using Edi.Core.Tests.Support;

namespace Edi.Core.Tests.Players;

public class DevicePlayerConcurrencyTests
{
    [Fact]
    public async Task ConcurrentPlaysFollowedByStopLeaveStopAsLastCommand()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        var plays = Enumerable.Range(0, 50)
            .Select(seek => rig.DevicePlayer.Play("scene", seek))
            .ToArray();

        await Task.WhenAll(plays);
        await rig.DevicePlayer.Stop();

        Assert.Equal(PlaybackCommandKind.Stop, rig.Device.Commands[^1].Kind);
        Assert.Equal(50, rig.Device.Commands.Count(
            command => command.Kind == PlaybackCommandKind.Play));
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

        var resumed = rig.Device.Commands.Last(
            command => command.Kind == PlaybackCommandKind.Play);
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
        await rig.DevicePlayer.Stop();

        Assert.Equal(PlaybackCommandKind.Stop, rig.Device.Commands[^1].Kind);
    }
}
