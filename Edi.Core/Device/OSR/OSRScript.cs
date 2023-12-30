using Edi.Core.Device.OSR;
using Edi.Core.Funscript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunscriptIntegrationService.Connector.Shared
{
    public class OSRScript
    {
        private struct MakimaCoefficient
        {
            public double s1;
            public double s2;
        }

        public readonly Axis[] SupportedAxis = new Axis[] { Axis.Default, Axis.Surge, Axis.Sway, Axis.Twist, Axis.Roll, Axis.Pitch, Axis.Vibrate, Axis.Valve, Axis.Suction };
        public long CurrentTime => Convert.ToInt32((DateTime.Now - playbackStartTime).TotalMilliseconds + seekTime);


        private Dictionary<Axis, List<CmdLinear>> unprocessedCommands;
        private Dictionary<Axis, List<CmdLinear>> playbackCommands;
        private Dictionary<Axis, int> commandIndex = new();
        private Dictionary<Axis, Dictionary<long, MakimaCoefficient>> makimaCoefficients;

        private DateTime playbackStartTime = DateTime.Now;
        private long seekTime = 0;
        private long scriptLength = -1;

        public OSRScript(Dictionary<Axis, List<CmdLinear>> commands)
        {
            unprocessedCommands = commands;
        }

        public OSRScript(Dictionary<Axis, List<CmdLinear>> commands, long seek)
        {
            unprocessedCommands = commands;
            seekTime = seek;
        }

        public void ProcessCommands(OSRDevice device)
        {
            makimaCoefficients = new();
            var processedCommands = new Dictionary<Axis, List<CmdLinear>>();

            var sb = new ScriptBuilder();

            foreach (var axis in SupportedAxis)
            {
                if (!unprocessedCommands.ContainsKey(axis))
                {
                    var value = axis != Axis.Default && axis != Axis.Vibrate ? 50 : 0;
                    sb.AddCommandMillis(500 + (int)seekTime, value);
                    unprocessedCommands[axis] = sb.Generate();
                }

                if (!device.Config.EnableMultiAxis && axis != Axis.Default)
                {
                    var value = axis == Axis.Vibrate ? 0 : 50;
                    sb.AddCommandMillis(500 + (int)seekTime, value);
                    unprocessedCommands[axis] = sb.Generate();
                    continue;
                }

                var commands = unprocessedCommands[axis].Clone();

                var seekIdx = commands.FindLastIndex(c => c.AbsoluteTime < seekTime);
                var currentPositionCommand = CmdLinear.GetCommandMillis((int)seekTime, device.LastCommandSent(axis)?.Value ?? 50);
                currentPositionCommand.AbsoluteTime = seekTime;

                if (seekIdx >= 0)
                {
                    commands.Insert(seekIdx + 1, currentPositionCommand);
                }

                commands.Insert(0, CmdLinear.GetCommandMillis(0, device.LastCommandSent(axis)?.Value ?? 50));

                processedCommands[axis] = commands;

                CmdLinear? prevCmd = null;
                foreach (var cmd in processedCommands[axis])
                {
                    if (prevCmd != null)
                        prevCmd.Next = cmd;
                    prevCmd = cmd;
                }

                CreateCoefficients(processedCommands[axis], axis);
            }

            playbackCommands = processedCommands;
            ResetIndices();
        }

        public bool GetNextCommand(Axis axis, out CmdLinear? cmd)
        {
            cmd = null;
            var millisDelta = 5;
            if (playbackCommands == null)
            {
                return true;
            }

            var index = commandIndex.GetValueOrDefault(axis, 0);
            var command = playbackCommands[axis].ElementAt(index);

            var nextMillis = CurrentTime + millisDelta;

            while (command.Next == null || command.Next.AbsoluteTime <= nextMillis)
            {
                index++;
                if (command.Next == null || index > playbackCommands[axis].Count)
                {
                    return true;
                }

                command = command.Next;
                commandIndex[axis] = index;
            }

            var coefficients = makimaCoefficients[axis][command.AbsoluteTime];
            var value = Math.Max(0, Math.Min(100, CubicHermite(command.AbsoluteTime, command.Value, command.Next.AbsoluteTime, command.Next.Value, coefficients.s1, coefficients.s2, nextMillis)));
            cmd = CmdLinear.GetCommandMillis(millisDelta, value);
            return true;
        }

        public void Loop()
        {
            ResetIndices();
            playbackStartTime = playbackStartTime.AddMilliseconds(scriptLength).AddMilliseconds(-seekTime);
            seekTime = 0;
        }

        private void ResetIndices()
        {
            foreach (var axis in SupportedAxis)
            {
                commandIndex[axis] = 0;
            }
        }

        private void CreateCoefficients(List<CmdLinear> commands, Axis axis)
        {
            makimaCoefficients[axis] = new();
            var loopedCommands = commands.GetRange(1, commands.Count - 1);

            var firstCommand = commands.First();
            firstCommand.Value = firstCommand.Value;

            var lastCommand = commands.Last();
            if (lastCommand.AbsoluteTime > scriptLength)
                scriptLength = lastCommand.AbsoluteTime;

            for (var i = 0; i < commands.Count - 1; i++)
            {
                var currentCommand = commands[i];
                var previousCommand = loopedCommands[LoopIndex(i - 1, loopedCommands.Count)].Clone();
                while (previousCommand.AbsoluteTime > currentCommand.AbsoluteTime)
                    previousCommand.AbsoluteTime -= lastCommand.AbsoluteTime;

                var previous2Command = loopedCommands[LoopIndex(i - 2, loopedCommands.Count)].Clone();
                while (previous2Command.AbsoluteTime > previousCommand.AbsoluteTime)
                    previous2Command.AbsoluteTime -= lastCommand.AbsoluteTime;

                var nextCommand = loopedCommands[(i) % loopedCommands.Count].Clone();
                while (nextCommand.AbsoluteTime < currentCommand.AbsoluteTime)
                    nextCommand.AbsoluteTime += lastCommand.AbsoluteTime;

                var next2Command = loopedCommands[(i + 1) % loopedCommands.Count].Clone();
                while (next2Command.AbsoluteTime < nextCommand.AbsoluteTime)
                    next2Command.AbsoluteTime += lastCommand.AbsoluteTime;

                var next3Command = loopedCommands[(i + 2) % loopedCommands.Count].Clone();
                while (next3Command.AbsoluteTime < next2Command.AbsoluteTime)
                    next3Command.AbsoluteTime += lastCommand.AbsoluteTime;

                MakimaSlopes(previous2Command.AbsoluteTime, previous2Command.Value, previousCommand.AbsoluteTime, previousCommand.Value, currentCommand.AbsoluteTime, currentCommand.Value, nextCommand.AbsoluteTime, nextCommand.Value, next2Command.AbsoluteTime, next2Command.Value, next3Command.AbsoluteTime, next3Command.Value, out var s1, out var s2);

                makimaCoefficients[axis][currentCommand.AbsoluteTime] = new MakimaCoefficient
                {
                    s1 = s1,
                    s2 = s2
                };
            }
        }

        private int LoopIndex(int index, int count)
        {
            var i = index;
            if (i <= 0)
            {
                i -= 1;
                do
                {
                    i += count;
                } while (i < 0);

                return i;
            }

            return i - 1;
        }

        private static void MakimaSlopes(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4, double x5, double y5, out double s1, out double s2)
        {
            var m4 = (y5 - y4) / (x5 - x4);
            var m3 = (y4 - y3) / (x4 - x3);
            var m2 = (y3 - y2) / (x3 - x2);
            var m1 = (y2 - y1) / (x2 - x1);
            var m0 = (y1 - y0) / (x1 - x0);

            var w11 = Math.Abs(m3 - m2) + Math.Abs(m3 + m2) / 2;
            var w12 = Math.Abs(m1 - m0) + Math.Abs(m1 + m0) / 2;
            s1 = (w11 * m1 + w12 * m2) / (w11 + w12);
            if (!double.IsFinite(s1))
                s1 = 0;

            var w21 = Math.Abs(m4 - m3) + Math.Abs(m4 + m3) / 2;
            var w22 = Math.Abs(m2 - m1) + Math.Abs(m2 + m1) / 2;
            s2 = (w21 * m2 + w22 * m3) / (w21 + w22);
            if (!double.IsFinite(s2))
                s2 = 0;
        }

        private static double CubicHermite(double x0, double y0, double x1, double y1, double s0, double s1, double x)
        {
            var d = x1 - x0;
            var dx = x - x0;
            var t = dx / d;
            var r = 1 - t;

            return r * r * (y0 * (1 + 2 * t) + s0 * dx)
                 + t * t * (y1 * (3 - 2 * t) - d * s1 * r);
        }
    }
}
