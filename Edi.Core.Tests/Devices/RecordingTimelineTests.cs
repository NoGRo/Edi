using Edi.Core.Device.Simulator;
using Edi.Core.Funscript.Command;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Tests.Devices;

public class RecordingTimelineTests
{
    [Fact]
    public void SnapshotProjectsOnlyThePartOfTheCommandThatAlreadyPlayed()
    {
        var timeline = new RecordingTimeline();
        timeline.StartSegment(Gallery(loop: false), seek: 0, min: 0, max: 100, recordingTime: 0);

        var actions = timeline.Snapshot(recordingTime: 500);

        Assert.Collection(
            actions,
            action => AssertAction(action, at: 0, pos: 0),
            action => AssertAction(action, at: 500, pos: 50));
    }

    [Fact]
    public void PeriodicSnapshotsDoNotDuplicateAnActiveSegment()
    {
        var timeline = new RecordingTimeline();
        timeline.StartSegment(Gallery(loop: false), seek: 0, min: 0, max: 100, recordingTime: 0);

        _ = timeline.Snapshot(recordingTime: 500);
        var laterSnapshot = timeline.Snapshot(recordingTime: 750);

        Assert.Collection(
            laterSnapshot,
            action => AssertAction(action, at: 0, pos: 0),
            action => AssertAction(action, at: 750, pos: 75));
    }

    [Fact]
    public void StopCommitsCompletedPointsAndInterpolatedEndInMilliseconds()
    {
        var timeline = new RecordingTimeline();
        timeline.StartSegment(Gallery(loop: false), seek: 0, min: 0, max: 100, recordingTime: 0);

        timeline.StopSegment(recordingTime: 1500);
        var actions = timeline.Snapshot(recordingTime: 9000);

        Assert.Collection(
            actions,
            action => AssertAction(action, at: 0, pos: 0),
            action => AssertAction(action, at: 1000, pos: 100),
            action => AssertAction(action, at: 1500, pos: 50));
    }

    [Fact]
    public void SeekAndRangeAreAppliedToTheRecordedSegment()
    {
        var timeline = new RecordingTimeline();
        timeline.StartSegment(Gallery(loop: false), seek: 500, min: 20, max: 80, recordingTime: 200);

        timeline.StopSegment(recordingTime: 700);
        var actions = timeline.Snapshot(recordingTime: 700);

        Assert.Collection(
            actions,
            action => AssertAction(action, at: 0, pos: 0),
            action => AssertAction(action, at: 199, pos: 0),
            action => AssertAction(action, at: 200, pos: 50),
            action => AssertAction(action, at: 700, pos: 80));
    }

    [Fact]
    public void LoopingGalleryIsExpandedAcrossEveryElapsedCycle()
    {
        var timeline = new RecordingTimeline();
        timeline.StartSegment(Gallery(loop: true), seek: 0, min: 0, max: 100, recordingTime: 0);

        timeline.StopSegment(recordingTime: 2500);
        var actions = timeline.Snapshot(recordingTime: 2500);

        Assert.Collection(
            actions,
            action => AssertAction(action, at: 0, pos: 0),
            action => AssertAction(action, at: 1000, pos: 100),
            action => AssertAction(action, at: 2000, pos: 0),
            action => AssertAction(action, at: 2500, pos: 50));
    }

    private static FunscriptGallery Gallery(bool loop)
    {
        var commands = CmdLinear.ParseFunscript(new FunScriptFile
        {
            actions =
            [
                new FunScriptAction { at = 0, pos = 0 },
                new FunScriptAction { at = 1000, pos = 100 },
                new FunScriptAction { at = 2000, pos = 0 }
            ]
        });

        return new FunscriptGallery
        {
            Name = "test",
            Duration = 2000,
            Loop = loop,
            Commands = commands
        };
    }

    private static void AssertAction(FunScriptAction action, long at, int pos)
    {
        Assert.Equal(at, action.at);
        Assert.Equal(pos, action.pos);
    }
}
