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
using FunscriptIntegrationService.Connector.Shared;
using System.Collections.Concurrent;

namespace Edi.Core.Device.OSR
{
    [AddINotifyPropertyChangedInterface]
    public class OSRDevice : IDevice
    {
        public SerialPort DevicePort { get; private set; }
        public string Name { get; set; }
        public OSRConfig Config { get; private set; }
        public string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                selectedVariant = value;
                if (currentGallery != null && playbackScript != null && !IsPause)
                    PlayGallery(currentGallery.Name, playbackScript.CurrentTime).GetAwaiter();
            }
        }
        public IEnumerable<string> Variants => repository.GetVariants();
        public DateTime GalleryStart { get; private set; } = DateTime.Now;
        public bool IsPause { get; private set; } = true;
        public bool IsReady => DevicePort?.IsOpen == true;
        public CmdLinear? LastCommandSent(Axis axis) => lastCommandSent.ContainsKey(axis) ? lastCommandSent[axis] : null;


        private readonly string[] channelCodes = new string[] { "L0", "L1", "L2", "R0", "R1", "R2", "V0", "A0", "A1" };
        private FunscriptRepository repository { get; set; }
        private string selectedVariant;


        private FunscriptGallery? currentGallery;
        private OSRScript? playbackScript { get; set; }
        private bool isNewScript { get; set; } = false;
        private bool speedRampUp { get; set; } = false;
        private DateTime? speedRampUpTime { get; set; }
        private ConcurrentDictionary<Axis, CmdLinear> lastCommandSent { get; set; } = new();
        private volatile CancellationTokenSource playbackCancellationTokenSource = new();

        private SemaphoreSlim semaphore = new(1, 1);

        private string ChannelCode(Axis type) => channelCodes[(int)type];

        public OSRDevice(SerialPort devicePort, FunscriptRepository repository, OSRConfig config)
        {
            DevicePort = devicePort;
            Name = GetDeviceName();

            Config = config;
            this.repository = repository;

            selectedVariant = repository.Config.DefaulVariant;
        }

        public async Task PlayGallery(string name, long seek = 0)
        {
            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
                return;

            var script = new OSRScript(gallery.AxisCommands, seek);
            isNewScript = currentGallery?.Name != gallery.Name;
            currentGallery = gallery;
            if (IsPause)
                speedRampUp = true;

            IsPause = false;

            PlayScript(script);
        }

        public async Task Stop()
        {
            IsPause = true;
            playbackCancellationTokenSource.Cancel();
        }

        private void PlayScript(OSRScript script)
        {
            var newCancellationTokenSource = new CancellationTokenSource();
            semaphore.Wait();
            try
            {
                playbackCancellationTokenSource.Cancel();
                playbackCancellationTokenSource = newCancellationTokenSource;
            } finally
            {
                semaphore.Release();
            }

            playbackScript = script;
            script.ProcessCommands(this);

            _ = Task.Run(() => PlayCommands(newCancellationTokenSource.Token));
        }

        private void PlayCommands(CancellationToken token)
        {
            if (playbackScript == null)
                return;

            Axis[] scriptCommandTypes = playbackScript.SupportedAxis.ToArray();
            HashSet<Axis> finishedCommandTypes = new();

            while (!token.IsCancellationRequested)
            {
                foreach (var axis in scriptCommandTypes)
                {
                    if (finishedCommandTypes.Contains(axis))
                        continue;

                    var send = playbackScript.GetNextCommand(axis, out CmdLinear? nextCmd);

                    if (token.IsCancellationRequested || (nextCmd == null && send == true))
                    {
                        finishedCommandTypes.Add(axis);

                        if (finishedCommandTypes.Count != scriptCommandTypes.Length)
                        {
                            continue;
                        }

                        if (currentGallery?.Loop == true && !token.IsCancellationRequested)
                        {
                            playbackScript.Loop();
                            PlayScript(playbackScript);
                            return;
                        }

                        return;
                    }

                    if (nextCmd != null)
                    {

                        var prevCmd = LastCommandSent(axis);
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

                        SendCmd(nextCmd, axis);
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
                Thread.Sleep(100);
            }
            var name = DevicePort.ReadExisting();
            return name.Replace("\r\n", "");
        }

        public async Task ReturnToHome()
        {
            var cmds = ReturnToHomeCommands();

            await semaphore.WaitAsync();
            try
            {
                cmds.Keys
                    .AsParallel()
                    .ForAll(axis => { SendCmd(cmds[axis].First(), axis); });
                await Task.Delay(1000);
            }
            finally
            {
                semaphore.Release();
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

        private void SendCmd(CmdLinear cmd, Axis axis)
        {
            if (DevicePort == null)
                return;

            var updatedCmd = GetUpdatedCommandRange(cmd, axis);
            var value = (int)(updatedCmd.Value * 99.99);
            var tCode = $"{ChannelCode(axis)}{value.ToString().PadLeft(4, '0')}I{updatedCmd.Millis}";

            DevicePort.WriteLine(tCode);
            lastCommandSent[axis] = cmd;
        }

        private CmdLinear GetUpdatedCommandRange(CmdLinear command, Axis axis)
        {
            CmdRange limits;
            switch (axis)
            {
                case Axis.Default:
                    limits = Config.RangeLimits.Linear;
                    break;
                case Axis.Surge:
                    limits = Config.RangeLimits.Surge;
                    break;
                case Axis.Sway:
                    limits = Config.RangeLimits.Sway;
                    break;
                case Axis.Twist:
                    limits = Config.RangeLimits.Twist;
                    break;
                case Axis.Roll:
                    limits = Config.RangeLimits.Roll;
                    break;
                case Axis.Pitch:
                    limits = Config.RangeLimits.Pitch;
                    break;
                default:
                    return command;
            }

            var value = Math.Min(100, (limits.LowerLimit / 100f * 100) + (limits.RangeDelta / 100f * command.Value));
            return CmdLinear.GetCommandMillis(command.Millis, value);
        }
    }
}
