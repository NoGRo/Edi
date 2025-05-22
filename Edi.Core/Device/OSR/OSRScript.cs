using Edi.Core.Funscript;

namespace Edi.Core.Device.OSR
{
    internal class OSRScript
    {
        private struct InterpolationCoefficients
        {
            public double s1;
            public double s2;
        }

        public readonly Axis[] SupportedAxis = new Axis[] { Axis.Default, Axis.Surge, Axis.Sway, Axis.Twist, Axis.Roll, Axis.Pitch, Axis.Vibrate, Axis.Valve, Axis.Suction };
        public long CurrentTime => Math.Max(0, Convert.ToInt32((DateTime.Now - playbackStartTime).TotalMilliseconds + seekTime));


        private Dictionary<Axis, List<CmdLinear>> unprocessedCommands;
        private Dictionary<Axis, List<CmdLinear>> playbackCommands;
        private Dictionary<Axis, int> commandIndex = new();
        private Dictionary<Axis, Dictionary<long, InterpolationCoefficients>> interpolationCoefficients;

        private DateTime playbackStartTime = DateTime.Now;
        private long seekTime = 0;
        private int scriptLength;

        public OSRScript(Dictionary<Axis, List<CmdLinear>> commands)
        {
            unprocessedCommands = commands;
            scriptLength = (int)commands.Values.SelectMany(x => x).ToList().MaxBy(x => x.AbsoluteTime).AbsoluteTime;
        }

        public OSRScript(Dictionary<Axis, List<CmdLinear>> commands, long seek)
        {
            unprocessedCommands = commands;
            seekTime = seek;
            scriptLength = (int)commands.Values.SelectMany(x => x).ToList().MaxBy(x => x.AbsoluteTime).AbsoluteTime;
        }

        public void ProcessCommands(OSRDevice device)
        {
            interpolationCoefficients = new();
            var processedCommands = new Dictionary<Axis, List<CmdLinear>>();

            var sb = new ScriptBuilder();

            foreach (var axis in SupportedAxis)
            {
                var commands = unprocessedCommands.GetValueOrDefault(axis)?.Clone();

                if (commands == null)
                {
                    var value = axis != Axis.Default && axis != Axis.Vibrate ? 50 : 0;
                    var bufferTime = 500 + (int)seekTime;
                    sb.AddCommandMillis(bufferTime, value);
                    sb.AddCommandMillis(bufferTime, value);
                    commands = sb.Generate();
                }

                if (!device.Config.EnableMultiAxis && axis != Axis.Default)
                {
                    var value = axis == Axis.Vibrate ? 0 : 50;
                    var bufferTime = 500 + (int)seekTime;
                    sb.AddCommandMillis(bufferTime, value);
                    sb.AddCommandMillis(bufferTime, value);
                    commands = sb.Generate();
                }

                if (commands.First().buttplugMillis == 0)
                {
                    commands.RemoveAt(0);
                }

                var seekIdx = commands.FindLastIndex(c => c.AbsoluteTime < seekTime);
                var lastPosition = device.LastPosition?.GetAxisValue(axis) ?? 5000;

                var currentPositionCommand = CmdLinear.GetCommandMillis((int)seekTime, Math.Round(lastPosition / 99.99f));
                currentPositionCommand.AbsoluteTime = seekTime;

                if (seekIdx >= 0)
                {
                    var beforeSeekCmd = commands[seekIdx];
                    var afterSeekCmd = commands[seekIdx + 1];

                    if (afterSeekCmd.AbsoluteTime - seekTime < seekTime - beforeSeekCmd.AbsoluteTime)
                    {
                        commands.RemoveAt(seekIdx + 1);
                    }
                }
                commands.Insert(seekIdx + 1, currentPositionCommand);

                processedCommands[axis] = commands;

                CmdLinear prevCmd = null;
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

        public OSRPosition GetNextPosition(int deltaMillis)
        {
            OSRPosition pos = null;
            var nextMillis = CurrentTime + deltaMillis;

            if (playbackCommands == null || nextMillis > scriptLength)
            {
                return null;
            }

            var hasScript = false;

            Dictionary<Axis, ushort?> positionValues = new();
            foreach (var axis in SupportedAxis.ToArray())
            {
                var index = commandIndex.GetValueOrDefault(axis, 0);
                var command = playbackCommands[axis].ElementAt(index);

                while (command.Next == null || command.Next.AbsoluteTime <= nextMillis)
                {
                    if (command.Next == null || index > playbackCommands[axis].Count)
                    {
                        break;
                    }

                    command = command.Next;
                    commandIndex[axis] = index++;
                }

                if (interpolationCoefficients[axis].ContainsKey(command.AbsoluteTime))
                {
                    var coefficients = interpolationCoefficients[axis][command.AbsoluteTime];
                    var followingCommand = command.Next ?? playbackCommands[axis].ElementAt(1).Clone();
                    if (followingCommand.AbsoluteTime < command.AbsoluteTime)
                        followingCommand.AbsoluteTime += scriptLength;

                    var value = Math.Max(0, Math.Min(100, CubicHermite(command.AbsoluteTime, command.Value, followingCommand.AbsoluteTime, followingCommand.Value, coefficients.s1, coefficients.s2, nextMillis)));

                    positionValues[axis] = (ushort)(value * 99.99f);
                    hasScript = true;
                }
                else
                {
                    positionValues[axis] = null;
                }
            }

            if (hasScript)
            {
                pos = OSRPosition.FromAxisDictionary(positionValues);
                pos.DeltaMillis = deltaMillis;
            }

            return pos;
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
            interpolationCoefficients[axis] = new();
            var loopedCommands = commands.GetRange(1, commands.Count - 1);

            var firstCommand = commands.First();
            firstCommand.Value = firstCommand.Value;

            var lastCommand = commands.Last();

            for (var i = 0; i < commands.Count - 1; i++)
            {
                var currentCommand = commands[i];
                var previousCommand = loopedCommands[LoopIndex(i - 1, loopedCommands.Count)].Clone();
                while (previousCommand.AbsoluteTime > currentCommand.AbsoluteTime)
                    previousCommand.AbsoluteTime -= lastCommand.AbsoluteTime;

                var previous2Command = loopedCommands[LoopIndex(i - 2, loopedCommands.Count)].Clone();
                while (previous2Command.AbsoluteTime > previousCommand.AbsoluteTime)
                    previous2Command.AbsoluteTime -= lastCommand.AbsoluteTime;

                var nextCommand = loopedCommands[i % loopedCommands.Count].Clone();
                while (nextCommand.AbsoluteTime < currentCommand.AbsoluteTime)
                    nextCommand.AbsoluteTime += lastCommand.AbsoluteTime;

                var next2Command = loopedCommands[(i + 1) % loopedCommands.Count].Clone();
                while (next2Command.AbsoluteTime < nextCommand.AbsoluteTime)
                    next2Command.AbsoluteTime += lastCommand.AbsoluteTime;

                var next3Command = loopedCommands[(i + 2) % loopedCommands.Count].Clone();
                while (next3Command.AbsoluteTime < next2Command.AbsoluteTime)
                    next3Command.AbsoluteTime += lastCommand.AbsoluteTime;

                //MakimaSlopes(previous2Command.AbsoluteTime, previous2Command.Value, previousCommand.AbsoluteTime, previousCommand.Value, currentCommand.AbsoluteTime, currentCommand.Value, nextCommand.AbsoluteTime, nextCommand.Value, next2Command.AbsoluteTime, next2Command.Value, next3Command.AbsoluteTime, next3Command.Value, out var s1, out var s2);
                PchipSlopes(previousCommand.AbsoluteTime, previousCommand.Value, currentCommand.AbsoluteTime, currentCommand.Value, nextCommand.AbsoluteTime, nextCommand.Value, next2Command.AbsoluteTime, next2Command.Value, out var s1, out var s2);

                interpolationCoefficients[axis][currentCommand.AbsoluteTime] = new InterpolationCoefficients
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

        private static void PchipSlopes(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, out double s1, out double s2)
        {
            var hkm1 = x1 - x0;
            var dkm1 = (y1 - y0) / hkm1;

            var hk1 = x2 - x1;
            var dk1 = (y2 - y1) / hk1;
            var w11 = 2 * hk1 + hkm1;
            var w12 = hk1 + 2 * hkm1;

            s1 = (w11 + w12) / (w11 / dkm1 + w12 / dk1);
            if (!double.IsFinite(s1) || dk1 * dkm1 < 0)
                s1 = 0;

            var hkm2 = x2 - x1;
            var dkm2 = (y2 - y1) / hkm2;

            var hk2 = x3 - x2;
            var dk2 = (y3 - y2) / hk2;
            var w21 = 2 * hk2 + hkm2;
            var w22 = hk2 + 2 * hkm2;

            s2 = (w21 + w22) / (w21 / dkm2 + w22 / dk2);
            if (!double.IsFinite(s2) || dk2 * dkm2 < 0)
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
