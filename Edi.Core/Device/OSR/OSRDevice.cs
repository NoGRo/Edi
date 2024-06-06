using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using Edi.Core.Gallery.CmdLineal;
using PropertyChanged;
using System.IO.Ports;
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
                if (currentGallery != null && PlaybackScript != null && !IsPause)
                    PlayGallery(currentGallery.Name, PlaybackScript.CurrentTime).GetAwaiter();
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
        private OSRScript? PlaybackScript { get; set; }
        private bool IsNewScript { get; set; } = false;
        private bool SpeedRampUp { get; set; } = false;
        private DateTime? SpeedRampUpTime { get; set; }
        private ConcurrentDictionary<Axis, CmdLinear> lastCommandSent { get; set; } = new();
        private volatile CancellationTokenSource playbackCancellationTokenSource = new();

        private SemaphoreSlim AsyncLock = new(1, 1);
        private string ChannelCode(Axis type) => channelCodes[(int)type];

        public OSRDevice(SerialPort devicePort, FunscriptRepository repository, OSRConfig config)
        {
            DevicePort = devicePort;
            Name = GetDeviceName();

            Config = config;
            this.repository = repository;

            selectedVariant = repository.GetVariants().FirstOrDefault();
        }

        public async Task PlayGallery(string name, long seek = 0)
        {
            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
                return;

            var script = new OSRScript(gallery.AxesCommands, seek);
            IsNewScript = currentGallery?.Name != gallery.Name;
            currentGallery = gallery;
            if (IsPause)
                SpeedRampUp = true;

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
            AsyncLock.Wait();
            try
            {
                playbackCancellationTokenSource?.Cancel();
                playbackCancellationTokenSource = newCancellationTokenSource;
            }
            finally
            {
                AsyncLock.Release();
            }

            PlaybackScript = script;
            script.ProcessCommands(this);

            _ = Task.Run(() => PlayCommands(newCancellationTokenSource.Token));
        }

        private void PlayCommands(CancellationToken token)
        {
            if (PlaybackScript == null)
                return;

            Axis[] scriptCommandTypes = PlaybackScript.SupportedAxis.ToArray();
            HashSet<Axis> finishedCommandTypes = new();

            while (!token.IsCancellationRequested)
            {
                foreach (var axis in scriptCommandTypes)
                {
                    if (finishedCommandTypes.Contains(axis))
                        continue;

                    var send = PlaybackScript.GetNextCommand(axis, out CmdLinear? nextCmd);

                    if (token.IsCancellationRequested || (nextCmd == null && send == true))
                    {
                        finishedCommandTypes.Add(axis);

                        if (finishedCommandTypes.Count != scriptCommandTypes.Length)
                        {
                            continue;
                        }

                        if (currentGallery?.Loop == true && !token.IsCancellationRequested)
                        {
                            PlaybackScript.Loop();
                            PlayScript(PlaybackScript);
                            return;
                        }

                        return;
                    }

                    if (nextCmd != null)
                    {

                        var prevCmd = LastCommandSent(axis);
                        if (!SpeedRampUp && nextCmd.Millis <= 200 && IsNewScript && prevCmd != null)
                        {
                            var deltaValue = Math.Abs(prevCmd.Value - nextCmd.Value);
                            var speed = deltaValue / nextCmd.Millis;

                            SpeedRampUp = speed > 0.4;
                        }

                        IsNewScript = false;
                        if (SpeedRampUp)
                        {
                            if (SpeedRampUpTime == null)
                                SpeedRampUpTime = DateTime.Now;

                            var rampUpDuration = 1000;
                            var adjustment = rampUpDuration - DateTime.Now.Subtract((DateTime)SpeedRampUpTime).TotalMilliseconds;

                            if (adjustment > 0)
                            {
                                var easeAmount = Math.Sin(((rampUpDuration - adjustment) / rampUpDuration * Math.PI) / 2);
                                adjustment = (1 - easeAmount) * 1000;
                                nextCmd = CmdLinear.GetCommandMillis(nextCmd.Millis + (int)adjustment, nextCmd.Value);
                            }
                            else
                            {
                                SpeedRampUp = false;
                                IsNewScript = false;
                                SpeedRampUpTime = null;
                            }
                        }

                        SendCmd(nextCmd, axis);
                    }
                }
            }
        }

        public bool AlivePing()
        {
            try
            {
                var ranges = GetDeviceRanges();

                if (ranges == null)
                    return false;
                if (ranges.StartsWith("L0"))
                    return true;

            } catch { }

            return false;
        }

        private string GetDeviceRanges()
        {
            if (!DevicePort.IsOpen)
                return null;

            DevicePort.ReadExisting();
            DevicePort.Write("d2\n");
            var ranges = ReadDeviceOutput();
            return ranges.Replace("\r\n", "");
        }

        private string GetDeviceName()
        {
            if (!DevicePort.IsOpen)
                return null;

            DevicePort.ReadExisting();
            DevicePort.Write("d1\n");
            var name = ReadDeviceOutput();
            return name.Replace("\r\n", "");
        }

        private string ReadDeviceOutput()
        {
            var tryCount = 0;
            while (DevicePort.BytesToRead == 0)
            {
                if (tryCount++ > 5)
                    throw new Exception("Timeout waiting for OSR response");
                Thread.Sleep(100);
            }
            return DevicePort.ReadExisting();
         }

        public async Task ReturnToHome()
        {
            var cmds = ReturnToHomeCommands();

            await AsyncLock.WaitAsync();
            try
            {
                cmds.Keys
                    .AsParallel()
                    .ForAll(axis => { SendCmd(cmds[axis].First(), axis); });
                await Task.Delay(1000);
            }
            finally
            {
                AsyncLock.Release();
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

            var value = Math.Min(100, (limits.LowerLimit / 100f * 100) + (limits.RangeDelta() / 100f * command.Value));
            return CmdLinear.GetCommandMillis(command.Millis, value);
        }

        public string ResolveDefaultVariant()
        => Variants.FirstOrDefault("");
    }
}
