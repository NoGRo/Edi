using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
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
                logger.LogInformation($"Setting variant on device '{Name}' with SelectedVariant: {SelectedVariant}.");
                if (currentGallery != null && playbackScript != null && !IsPause)
                    PlayGallery(currentGallery.Name, playbackScript.CurrentTime).GetAwaiter();
            }
        }
        public IEnumerable<string> Variants => repository.GetVariants();
        public bool IsPause { get; private set; } = true;
        public bool IsReady => DevicePort?.IsOpen == true;

        internal OSRPosition? LastPosition { get; private set; }

        private readonly ILogger logger;
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
                logger.LogInformation($"Applying range for device: {Name}, Min: {Min}");
                _ = ApplyRange();
            }
        }
        public int Max { get => targetMax; set { 
                targetMax = value;
                logger.LogInformation($"Applying range for device: {Name}, Max: {Max}");
                _ = ApplyRange();
            } 
        }

        private Timer positionUpdateTimer;
        private int updateMs;

        public OSRDevice(SerialPort devicePort, FunscriptRepository repository, OSRConfig config, ILogger logger)
        {
            this.logger = logger;
            DevicePort = devicePort;
            Name = GetDeviceName();

            Config = config;
            this.repository = repository;

            selectedVariant = repository.GetVariants().FirstOrDefault("");

            updateMs = 1000 / config.UpdateRate;
            positionUpdateTimer = new Timer(_ => PlayCommands(), null, 0, updateMs);
        }

        public async Task PlayGallery(string name, long seek = 0)
        {
            logger.LogInformation($"Starting gallery '{name}' on device: {this.Name} with seek: {seek}");
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
            logger.LogInformation($"Stopping gallery playback for device: {Name}");
        }

        private void PlayScript(OSRScript script)
        {
            var newCancellationTokenSource = new CancellationTokenSource();
            asyncLock.Wait();
            try
            {
                playbackCancellationTokenSource?.Cancel();
                playbackCancellationTokenSource = newCancellationTokenSource;

                script.ProcessCommands(this);
                playbackScript = script;
            }
            finally
            {
                asyncLock.Release();
            }
        }

        private void PlayCommands()
        {
            if (!Monitor.TryEnter(positionUpdateTimer))
                return;

            try
            {
                if (playbackScript == null || playbackCancellationTokenSource.IsCancellationRequested)
                    return;

                var pos = playbackScript.GetNextPosition(updateMs);

                if (playbackCancellationTokenSource.IsCancellationRequested || pos == null)
                {
                    if (currentGallery?.Loop == true && !playbackCancellationTokenSource.IsCancellationRequested)
                    {
                        playbackScript.Loop();
                        PlayScript(playbackScript);
                    }

                    return;
                }
                else
                {
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

                
            } finally
            {
                Monitor.Exit(positionUpdateTimer);
            }
        }

        public bool AlivePing()
        {
            try
            {
                return ValidateTCode();

            } catch (Exception e) {
                logger.LogError(e, $"Error during liveness check for device '{Name}'");
            }

            return false;
        }

        private bool ValidateTCode()
        {
            if (!DevicePort.IsOpen)
                return false;

            DevicePort.DiscardInBuffer();

            DevicePort.Write("d1\n");
            var tryCount = 0;

            while (DevicePort.BytesToRead == 0)
            {
                if (tryCount++ >= 5)
                    throw new Exception("Timeout waiting for TCode response");
                Thread.Sleep(100);
            }
            var protocol = DevicePort.ReadExisting();

            return (protocol.Contains("tcode", StringComparison.OrdinalIgnoreCase));
        }

        private string GetDeviceName()
        {
            if (!DevicePort.IsOpen)
                return string.Empty;

            DevicePort.DiscardInBuffer();
            DevicePort.Write("d0\n");
            var tryCount = 0;

            while (DevicePort.BytesToRead == 0)
            {
                if (tryCount++ >= 5)
                    throw new Exception("Timeout waiting for TCode Name response");
                Thread.Sleep(100);
            }
            var name = DevicePort.ReadExisting();
            if (name.Count(c => c == '\n') > 1)
                throw new Exception("Fail get valid Name response");
            
            return name.Replace("\r\n", "");
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
