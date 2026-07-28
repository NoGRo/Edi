using Edi.Core.Funscript.Command;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Device.Simulator;

internal sealed class RecordingTimeline
{
    private readonly List<FunScriptAction> committed =
        [new FunScriptAction { at = 0, pos = 0 }];
    private Segment activeSegment;

    public void StartSegment(
        FunscriptGallery gallery,
        long seek,
        int min,
        int max,
        long recordingTime)
    {
        StopSegment(recordingTime);
        activeSegment = new Segment(
            gallery,
            Math.Max(0, seek),
            Math.Clamp(min, 0, 100),
            Math.Clamp(max, 0, 100),
            Math.Max(0, recordingTime));
    }

    public void StopSegment(long recordingTime)
    {
        if (activeSegment == null)
            return;

        Append(committed, Render(activeSegment, recordingTime));
        activeSegment = null;
    }

    public List<FunScriptAction> Snapshot(long recordingTime)
    {
        var result = committed
            .Select(action => new FunScriptAction
            {
                at = action.at,
                pos = action.pos
            })
            .ToList();

        if (activeSegment != null)
            Append(result, Render(activeSegment, recordingTime));

        return result;
    }

    private static List<FunScriptAction> Render(
        Segment segment,
        long recordingTime)
    {
        var gallery = segment.Gallery;
        var commands = gallery.Commands;
        var elapsed = Math.Max(0, recordingTime - segment.RecordingStart);
        var duration = Math.Max(0, gallery.Duration);
        if (commands == null || commands.Count == 0 || duration == 0)
            return [];

        var sourceStart = gallery.Loop
            ? PositiveModulo(segment.Seek, duration)
            : Math.Clamp(segment.Seek, 0, duration);
        var sourceEnd = gallery.Loop
            ? sourceStart + elapsed
            : Math.Min(duration, sourceStart + elapsed);
        var rendered = new List<FunScriptAction>
        {
            Action(
                segment.RecordingStart,
                Scale(ValueAt(commands, sourceStart), segment.Min, segment.Max))
        };

        if (gallery.Loop)
        {
            var firstCycle = sourceStart / duration;
            var finalCycle = sourceEnd / duration;
            for (var cycle = firstCycle; cycle <= finalCycle; cycle++)
            {
                foreach (var command in commands)
                {
                    var boundary = cycle * duration + command.AbsoluteTime;
                    if (boundary <= sourceStart || boundary > sourceEnd)
                        continue;

                    rendered.Add(Action(
                        segment.RecordingStart + boundary - sourceStart,
                        Scale(command.Value, segment.Min, segment.Max)));
                }
            }
        }
        else
        {
            foreach (var command in commands.Where(
                         command => command.AbsoluteTime > sourceStart
                                    && command.AbsoluteTime <= sourceEnd))
            {
                rendered.Add(Action(
                    segment.RecordingStart + command.AbsoluteTime - sourceStart,
                    Scale(command.Value, segment.Min, segment.Max)));
            }
        }

        var recordingEnd = segment.RecordingStart + sourceEnd - sourceStart;
        var endPosition = gallery.Loop && sourceEnd > 0 && sourceEnd % duration == 0
            ? ValueAt(commands, duration)
            : ValueAt(commands, gallery.Loop
                ? PositiveModulo(sourceEnd, duration)
                : sourceEnd);
        AppendAction(
            rendered,
            Action(recordingEnd, Scale(endPosition, segment.Min, segment.Max)));
        return rendered;
    }

    private static double ValueAt(IReadOnlyList<CmdLinear> commands, long time)
    {
        var next = commands.FirstOrDefault(command => command.AbsoluteTime >= time);
        if (next == null)
            return commands[^1].Value;

        if (time == next.AbsoluteTime || next.Millis <= 0)
            return next.Value;

        var commandStart = next.AbsoluteTime - next.Millis;
        return next.GetValueInTime(Math.Max(0, time - commandStart));
    }

    private static int Scale(double value, int min, int max)
        => (int)Math.Round(
            min + (max - min) * Math.Clamp(value, 0, 100) / 100);

    private static long PositiveModulo(long value, long divisor)
        => (value % divisor + divisor) % divisor;

    private static FunScriptAction Action(long at, int position)
        => new()
        {
            at = Math.Max(0, at),
            pos = Math.Clamp(position, 0, 100)
        };

    private static void Append(
        List<FunScriptAction> destination,
        IEnumerable<FunScriptAction> source)
    {
        var isFirst = true;
        foreach (var action in source)
        {
            if (isFirst
                && destination.Count > 0
                && destination[^1].at < action.at
                && destination[^1].pos != action.pos
                && action.at - destination[^1].at > 1)
            {
                AppendAction(
                    destination,
                    Action(action.at - 1, destination[^1].pos));
            }

            AppendAction(destination, action);
            isFirst = false;
        }
    }

    private static void AppendAction(
        List<FunScriptAction> actions,
        FunScriptAction action)
    {
        if (actions.Count > 0 && actions[^1].at == action.at)
        {
            actions[^1].pos = action.pos;
            return;
        }

        if (actions.Count > 0 && actions[^1].at > action.at)
        {
            throw new InvalidOperationException(
                "Recording actions must be appended chronologically.");
        }

        actions.Add(action);
    }

    private sealed record Segment(
        FunscriptGallery Gallery,
        long Seek,
        int Min,
        int Max,
        long RecordingStart);
}
