using Buttplug;
using Buttplug.Client;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using PropertyChanged;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;
using System.Xml.Linq;
using System.IO.Ports;
using Timer = System.Timers.Timer;
using System.Data;
using NAudio.CoreAudioApi;

namespace Edi.Core.Device.OSR
{
    [AddINotifyPropertyChangedInterface]
    public class OSRDevice : IDevice
    {
        public SerialPort DevicePort { get; private set; }
        public string Name { get; set; }
        public string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                selectedVariant = value;
                if (currentGallery != null && !IsPause)
                    PlayGallery(currentGallery.Name, CurrentTime).GetAwaiter();
            }
        }

        public IEnumerable<string> Variants => repository.GetVariants();
        public DateTime GalleryStart { get; private set; } = DateTime.Now;
        public bool IsPause { get; private set; } = false;
        public bool IsReady => DevicePort?.IsOpen == true;


        private struct MakimaCoefficient
        {
            public double s1;
            public double s2;
        }

        private readonly string[] channelCodes = new string[] { "L0", "L1", "L2", "R0", "R1", "R2", "V0", "A0", "A1" };
        private readonly Axis[] supportedAxis = new Axis[] { Axis.Default, Axis.Surge, Axis.Sway, Axis.Twist, Axis.Roll, Axis.Pitch, Axis.Vibrate, Axis.Valve, Axis.Suction };
        private FunscriptRepository repository { get; set; }
        private OSRConfig config { get; set; }

        private string selectedVariant;


        private FunscriptGallery? currentGallery;
        private Dictionary<Axis, List<CmdLinear>>? playbackCommands { get; set; }
        private Dictionary<Axis, int> commandIndex { get; set; } = new Dictionary<Axis, int>();
        private bool isNewScript { get; set; } = false;
        private bool speedRampUp { get; set; } = false;
        private DateTime? speedRampUpTime { get; set; }
        private Dictionary<Axis, Dictionary<long, MakimaCoefficient>> makimaCoefficients { get; set; }
        private long seekTime { get; set; } = 0;
        private Dictionary<Axis, CmdLinear> lastCommandSent { get; set; } = new Dictionary<Axis, CmdLinear>();
        private volatile CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();


        private string ChannelCode(Axis type) => channelCodes[(int)type];
        private int CurrentTime => Convert.ToInt32((DateTime.Now - GalleryStart).TotalMilliseconds + seekTime);
        private CmdLinear? LastCommandSent(Axis axis) => lastCommandSent.ContainsKey(axis) ? lastCommandSent[axis] : null;


        public OSRDevice(SerialPort devicePort, FunscriptRepository repository, OSRConfig config)
        {
            DevicePort = devicePort;
            Name = GetDeviceName();

            this.config = config;
            this.repository = repository;

            SelectedVariant = repository.Config.DefaulVariant;
        }

        public async Task PlayGallery(string name, long seek = 0)
        {
            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
                return;

            isNewScript = currentGallery?.Name != gallery.Name;
            currentGallery = gallery;
            seekTime = seek;
            GalleryStart = DateTime.Now;

            _ = SendCommands(currentGallery.AxisCommands);
        }

        private async Task SendCommands(Dictionary<Axis, List<CmdLinear>> cmds)
        {
            var newCancellationTokenSource = new CancellationTokenSource();
            lock (DevicePort)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = newCancellationTokenSource;
            }

            playbackCommands = ProcessCommands(cmds);
            commandIndex = new();

            _ = Task.Run(() => PlayCommands(newCancellationTokenSource.Token));
        }

        private async Task PlayCommands(CancellationToken token)
        {
            if (playbackCommands == null)
                return;

            Axis[] scriptCommandTypes = playbackCommands.Keys.ToArray();
            HashSet<Axis> finishedCommandTypes = new HashSet<Axis>();

            CmdLinear? nextCmd;
            Dictionary<Axis, DateTime> commandEndTime = new Dictionary<Axis, DateTime>();

            while (!token.IsCancellationRequested)
            {
                foreach (var axis in scriptCommandTypes)
                {
                    if (finishedCommandTypes.Contains(axis))
                        continue;

                    var send = GetNextCommand(axis, out nextCmd);

                    if (token.IsCancellationRequested || (nextCmd == null && send == true))
                    {
                        finishedCommandTypes.Add(axis);

                        if (finishedCommandTypes.Count != scriptCommandTypes.Length)
                        {
                            continue;
                        }

                        if (currentGallery?.Loop == true && !token.IsCancellationRequested)
                        {
                            GalleryStart = GalleryStart.AddMilliseconds(currentGallery.AxisCommands[axis].Last().AbsoluteTime);
                            _ = SendCommands(currentGallery.AxisCommands);
                            return;
                        }

                        return;
                    }

                    if (nextCmd != null)
                    {
                        var prevCmd = LastCommandSent(axis);

                        if (!IsPause)
                        {
                            if (!speedRampUp && nextCmd.Millis <= 200 && isNewScript && prevCmd != null)
                            {
                                var deltaValue = Math.Abs(prevCmd.Value - nextCmd.Value);
                                var speed = deltaValue / nextCmd.Millis;

                                speedRampUp = speed > 0.4;
                            }

                            isNewScript = false;
                            if (speedRampUp)
                            {
                                if (speedRampUpTime == null)
                                    speedRampUpTime = DateTime.Now;

                                var rampUpDuration = 1000;
                                var adjustment = rampUpDuration - DateTime.Now.Subtract((DateTime)speedRampUpTime).TotalMilliseconds;

                                if (adjustment > 0)
                                {
                                    var easeAmount = Math.Sin(((rampUpDuration - adjustment) / rampUpDuration * Math.PI) / 2);
                                    adjustment = (1 - easeAmount) * 1000;
                                    nextCmd = CmdLinear.GetCommandMillis(nextCmd.Millis + (int)adjustment, nextCmd.Value);
                                }
                                else
                                {
                                    speedRampUp = false;
                                    isNewScript = false;
                                    speedRampUpTime = null;
                                }
                            }

                            _ = SendCmd(nextCmd, axis);
                        }
                    }
                }
            }
        }

        private string GetDeviceName()
        {
            DevicePort.ReadExisting();
            DevicePort.Write("d1\n");
            while (DevicePort.BytesToRead == 0)
            {
                Thread.Sleep(25);
            }
            var name = DevicePort.ReadExisting();
            return name.Replace("\r\n", "");
        }

        public async Task ReturnToHome()
        {
            var cmds = ReturnToHomeCommands();

            isNewScript = false;
            currentGallery = null;
            seekTime = 0;
            foreach (var axis in cmds.Keys)
            {
                _ = SendCmd(cmds[axis].First(), axis);
            }
        }

        private Dictionary<Axis, List<CmdLinear>> ReturnToHomeCommands()
        {
            var cmds = new Dictionary<Axis, List<CmdLinear>>();

            var sb = new ScriptBuilder();
            sb.AddCommandMillis(1000, 0);

            var zeroedScript = sb.Generate();

            cmds[Axis.Default] = zeroedScript;
            cmds[Axis.Vibrate] = zeroedScript;

            sb.AddCommandMillis(1000, 50);

            var halvedScript = sb.Generate();

            cmds[Axis.Pitch] = halvedScript;
            cmds[Axis.Roll] = halvedScript;
            cmds[Axis.Suction] = halvedScript;
            cmds[Axis.Surge] = halvedScript;
            cmds[Axis.Sway] = halvedScript;
            cmds[Axis.Twist] = halvedScript;

            return cmds;
        }

        private async Task SendCmd(CmdLinear cmd, Axis axis)
        {
            if (DevicePort == null)
                return;

            var updatedCmd = GetUpdatedCommandRange(cmd, axis);
            var value = (int)(updatedCmd.Value * 99.99);
            var tCode = $"{ChannelCode(axis)}{value.ToString().PadLeft(4, '0')}I{updatedCmd.Millis}";

            DevicePort.WriteLine(tCode);
            lastCommandSent[axis] = cmd;
        }

        public async Task Pause()
        {
            //IsPause = true;
        }

        private CmdLinear GetUpdatedCommandRange(CmdLinear command, Axis axis)
        {
            CmdRange limits;
            switch (axis)
            {
                case Axis.Default:
                    limits = config.RangeLimits.Linear;
                    break;
                case Axis.Surge:
                    limits = config.RangeLimits.Surge;
                    break;
                case Axis.Sway:
                    limits = config.RangeLimits.Sway;
                    break;
                case Axis.Twist:
                    limits = config.RangeLimits.Twist;
                    break;
                case Axis.Roll:
                    limits = config.RangeLimits.Roll;
                    break;
                case Axis.Pitch:
                    limits = config.RangeLimits.Pitch;
                    break;
                default:
                    return command;
            }


            var value = Math.Min(100, (limits.LowerLimit / 100f * 100) + (limits.RangeDelta / 100f * command.Value));
            return CmdLinear.GetCommandMillis(command.Millis, value);
        }

        private bool GetNextCommand(Axis axis, out CmdLinear? cmd)
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
            }

            var coefficients = makimaCoefficients[axis][command.AbsoluteTime];
            var value = Math.Max(0, Math.Min(100, CubicHermite(command.AbsoluteTime, command.Value, command.Next.AbsoluteTime, command.Next.Value, coefficients.s1, coefficients.s2, nextMillis)));
            cmd = CmdLinear.GetCommandMillis(millisDelta, value);
            commandIndex[axis] = index;
            return true;
        }

        private Dictionary<Axis, List<CmdLinear>> ProcessCommands(Dictionary<Axis, List<CmdLinear>> commands)
        {
            makimaCoefficients = new();
            var processedCommands = new Dictionary<Axis, List<CmdLinear>>();

            var sb = new ScriptBuilder();

            foreach (var axis in supportedAxis)
            {
                if (!commands.ContainsKey(axis))
                {
                    var value = axis != Axis.Default && axis != Axis.Vibrate ? 50 : 0;
                    sb.AddCommandMillis(500, value);
                    commands[axis] = sb.Generate();
                }

                if (!config.EnableMultiAxis && axis != Axis.Default)
                {
                    var value = axis == Axis.Vibrate ? 0 : 50;
                    sb.AddCommandMillis(500, value);
                    commands[axis] = sb.Generate();
                    continue;
                }

                processedCommands[axis] = commands[axis].Prepend(CmdLinear.GetCommandMillis(0, LastCommandSent(axis)?.Value ?? 50)).ToList();

                CmdLinear? prevCmd = null;
                foreach (var cmd in processedCommands[axis])
                {
                    if (prevCmd != null)
                        prevCmd.Next = cmd;
                    prevCmd = cmd;
                }

                CreateCoefficients(processedCommands[axis], axis);
            }

            return processedCommands;
        }


        private void CreateCoefficients(List<CmdLinear> commands, Axis axis)
        {
            makimaCoefficients[axis] = new();
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
