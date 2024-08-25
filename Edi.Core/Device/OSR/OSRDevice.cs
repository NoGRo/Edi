using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Funscript;
using PropertyChanged;
using System.IO.Ports;

namespace Edi.Core.Device.OSR
{
    [AddINotifyPropertyChangedInterface]
    public class OSRDevice : IDevice, IRange
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
        public bool IsPause { get; private set; } = true;
        public bool IsReady => DevicePort?.IsOpen == true;

        internal OSRPosition? LastPosition { get; private set; }

        private FunscriptRepository repository { get; set; }
        private string selectedVariant;

        private FunscriptGallery? currentGallery;
        private OSRScript? playbackScript { get; set; }
        private bool speedRampUp { get; set; } = false;
        private DateTime? speedRampUpTime { get; set; }

        private volatile CancellationTokenSource playbackCancellationTokenSource = new();

        private SemaphoreSlim asyncLock = new(1, 1);

        private volatile CancellationTokenSource rangeCancellationTokenSource = new();

        private int min = 0;
        private int max = 100;
        private int targetMin = 0;
        private int targetMax = 100;

        public int Min { get => targetMin; set {
                targetMin = value;
                _ = ApplyRange();
            }
        }
        public int Max { get => targetMax; set { 
                targetMax = value;
                _ = ApplyRange();
            } 
        }

        public OSRDevice(SerialPort devicePort, FunscriptRepository repository, OSRConfig config)
        {
            DevicePort = devicePort;
            Name = GetDeviceName();

            Config = config;
            this.repository = repository;

            selectedVariant = repository.GetVariants().FirstOrDefault("");
        }

        public async Task PlayGallery(string name, long seek = 0)
        {
            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
                return;

            var script = new OSRScript(gallery.AxesCommands, seek);
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
            asyncLock.Wait();
            try
            {
                playbackCancellationTokenSource?.Cancel();
                playbackCancellationTokenSource = newCancellationTokenSource;

                playbackScript = script;
                script.ProcessCommands(this);

                _ = Task.Run(() => PlayCommands(newCancellationTokenSource.Token));
            }
            finally
            {
                asyncLock.Release();
            }
        }

        private void PlayCommands(CancellationToken token)
        {
            if (playbackScript == null)
                return;

            while (!token.IsCancellationRequested)
            {
                var pos = playbackScript.GetNextPosition();

                if (token.IsCancellationRequested || pos == null)
                {
                    if (currentGallery?.Loop == true && !token.IsCancellationRequested)
                    {
                        playbackScript.Loop();
                        PlayScript(playbackScript);
                        return;
                    }

                    return;
                } else {
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
                            pos.DeltaMillis += (int)adjustment;
                        }
                        else
                        {
                            speedRampUp = false;
                            speedRampUpTime = null;
                        }
                    }

                    SendPos(pos);
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

        private string? GetDeviceRanges()
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
                return string.Empty;

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
                if (tryCount++ >= 5)
                    throw new Exception("Timeout waiting for OSR response");
                Thread.Sleep(100);
            }
            return DevicePort.ReadExisting();
         }

        public async Task ReturnToHome()
        {
            var pos = OSRPosition.ZeroedPosition();
            pos.DeltaMillis = 1000;

            await asyncLock.WaitAsync();
            try
            {
                SendPos(pos);
                await Task.Delay(1000);
            }
            finally
            {
                asyncLock.Release();
            }
        }


        private void SendPos(OSRPosition pos)
        {
            if (DevicePort == null)
                return;

            if (LastPosition != null)
                pos.Merge(LastPosition);

            var posClone = pos.Clone();
            posClone.UpdateRanges(GetDeviceRangeConfig());

            var tCode = posClone.OSRCommandString(LastPosition);
            if (tCode.Length > 0)
            {
                try
                {
                    DevicePort.WriteLine(tCode);
                    LastPosition = pos;
                } catch (Exception) { 
                    playbackCancellationTokenSource.Cancel();
                }
            }
        }

        private RangeConfiguration GetDeviceRangeConfig()
        {
            var rangeLimits = Config.RangeLimits.Clone();

            if (min == 0 && max == 100)
                return rangeLimits;

            var delta = max - min;
            rangeLimits.Linear.UpperLimit = (int)(rangeLimits.Linear.LowerLimit + rangeLimits.Linear.RangeDelta() * max / 100f);
            rangeLimits.Linear.LowerLimit = (int)(rangeLimits.Linear.LowerLimit + rangeLimits.Linear.RangeDelta() * min / 100f);

            var rollDelta = rangeLimits.Roll.RangeDelta();
            rangeLimits.Roll.UpperLimit = (int)(rollDelta / 2f + delta / 2f);
            rangeLimits.Roll.LowerLimit = (int)(rollDelta / 2f - delta / 2f);

            var pitchDelta = rangeLimits.Pitch.RangeDelta();
            rangeLimits.Pitch.UpperLimit = (int)(pitchDelta / 2f + delta / 2f);
            rangeLimits.Pitch.LowerLimit = (int)(pitchDelta / 2f - delta / 2f);

            var twistDelta = rangeLimits.Twist.RangeDelta();
            rangeLimits.Twist.UpperLimit = (int)(twistDelta / 2f + delta / 2f);
            rangeLimits.Twist.LowerLimit = (int)(twistDelta / 2f - delta / 2f);

            var swayDelta = rangeLimits.Sway.RangeDelta();
            rangeLimits.Sway.UpperLimit = (int)(swayDelta / 2f + delta / 2f);
            rangeLimits.Sway.LowerLimit = (int)(swayDelta / 2f - delta / 2f);

            var surgeDelta = rangeLimits.Surge.RangeDelta();
            rangeLimits.Surge.UpperLimit = (int)(surgeDelta / 2f + delta / 2f);
            rangeLimits.Surge.LowerLimit = (int)(surgeDelta / 2f - delta / 2f);

            return rangeLimits;
        }

        private async Task ApplyRange()
        {
            var previousCts = rangeCancellationTokenSource;
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            rangeCancellationTokenSource = cts;
            previousCts.Cancel();

            var delta = 3;
            var iterations = Math.Max(Math.Abs(targetMax - max), Math.Abs(targetMin - min)) / delta;

            for (int i = 0; i <= iterations; i++)
            {
                if (token.IsCancellationRequested) break;

                var currentLower = min;
                var targetLower = targetMin;

                if (targetLower > currentLower)
                {
                    min = Math.Min(currentLower + delta, targetLower);
                }
                else
                {
                    min = Math.Max(currentLower - delta, targetLower);
                }

                var currentUpper = max;
                var targetUpper = targetMax;

                if (targetUpper > currentUpper)
                {
                    max = Math.Min(currentUpper + delta, targetUpper);
                }
                else
                {
                    max = Math.Max(currentUpper - delta, targetUpper);
                }

                if (LastPosition != null)
                    SendPos(LastPosition);

                await Task.Delay(15);
            }
        }

        public string ResolveDefaultVariant()
        => Variants.FirstOrDefault("");
    }
}
