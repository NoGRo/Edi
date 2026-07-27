using Edi.Core.Tests.Support;

namespace Edi.Core.Tests.Players;

public class PlayerGalleryFlowTests
{
    [Fact]
    public async Task FillerPlaysUntilAMainGalleryStarts()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        await rig.Player.Play("ambient");
        await rig.Device.WaitForPlayAsync("ambient", occurrence: 1);
        await rig.Player.Play("scene", seek: 250);
        await rig.Device.WaitForPlayAsync("scene", occurrence: 1);

        var plays = rig.Device.Commands
            .Where(command => command.Kind == PlaybackCommandKind.Play)
            .ToList();

        Assert.Equal(["ambient", "scene"], plays.Select(command => command.GalleryName));
        Assert.Equal(250, plays[^1].Seek);
    }

    [Fact]
    public async Task NonLoopingGalleryReturnsToCurrentFiller()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        await rig.Player.Play("ambient");
        await rig.Device.WaitForPlayAsync("ambient", occurrence: 1);
        await rig.Player.Play("short-scene");

        var resumedFiller = await rig.Device.WaitForPlayAsync("ambient", occurrence: 2);

        Assert.True(
            resumedFiller.Sequence
            > rig.Device.Commands.Single(command => command.GalleryName == "short-scene").Sequence);
    }

    [Fact]
    public async Task ReactionInterruptsAndResumesMainGalleryAtCurrentTime()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        await rig.Player.Play("ambient");
        await rig.Device.WaitForPlayAsync("ambient", occurrence: 1);
        await rig.Player.Play("scene", seek: 200);
        await rig.Device.WaitForPlayAsync("scene", occurrence: 1);
        await rig.Player.Play("hit");
        await rig.Device.WaitForPlayAsync("hit", occurrence: 1);

        var resumedGallery = await rig.Device.WaitForPlayAsync("scene", occurrence: 2);

        Assert.InRange(resumedGallery.Seek, 200, 1500);
        var playNames = rig.Device.Commands
            .Where(command => command.Kind == PlaybackCommandKind.Play)
            .Select(command => command.GalleryName)
            .ToList();
        Assert.Equal(["ambient", "scene", "hit", "scene"], playNames);
    }

    [Fact]
    public async Task ExplicitStopDuringReactionResumesMainGallery()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        await rig.Player.Play("scene", seek: 100);
        await rig.Device.WaitForPlayAsync("scene", occurrence: 1);
        await rig.Player.Play("hit");
        await rig.Device.WaitForPlayAsync("hit", occurrence: 1);
        await rig.Player.Stop();
        await rig.Device.WaitForPlayAsync("scene", occurrence: 2);

        var plays = rig.Device.Commands
            .Where(command => command.Kind == PlaybackCommandKind.Play)
            .ToList();

        Assert.Equal(["scene", "hit", "scene"], plays.Select(command => command.GalleryName));
        Assert.True(plays[^1].Seek >= 100);
    }

    [Fact]
    public async Task StaleReactionTimeoutCannotOverrideANewerGallery()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        await rig.Player.Play("ambient");
        await rig.Device.WaitForPlayAsync("ambient", occurrence: 1);
        await rig.Player.Play("scene");
        await rig.Device.WaitForPlayAsync("scene", occurrence: 1);
        await rig.Player.Play("hit");
        await rig.Device.WaitForPlayAsync("hit", occurrence: 1);
        await rig.Player.Play("short-scene");

        var resumedFiller = await rig.Device.WaitForPlayAsync("ambient", occurrence: 2);
        var commandsAfterShortScene = rig.Device.Commands
            .Where(command => command.Sequence
                              > rig.Device.Commands.Single(
                                  candidate => candidate.GalleryName == "short-scene").Sequence
                              && command.Sequence < resumedFiller.Sequence)
            .ToList();

        Assert.Empty(commandsAfterShortScene);
    }

    [Fact]
    public async Task UnknownGalleryIsIgnoredWithoutChangingPlayback()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        await rig.Player.Play("scene");
        await rig.Device.WaitForPlayAsync("scene", occurrence: 1);
        var countBefore = rig.Device.Commands.Count;

        await rig.Player.Play("does-not-exist");

        Assert.Equal(countBefore, rig.Device.Commands.Count);
        Assert.Contains(
            rig.Logs.GetLogs(),
            message => message.Contains("Ignored not found [does-not-exist]"));
    }
}
