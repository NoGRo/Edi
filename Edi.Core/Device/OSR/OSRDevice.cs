using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.CmdLineal;
using PropertyChanged;
using System.Diagnostics;
using System.IO.Ports;

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
            }
            finally
            {
                asyncLock.Release();
            }

            playbackScript = script;
            script.ProcessCommands(this);

            _ = Task.Run(() => PlayCommands(newCancellationTokenSource.Token));
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

            var posClone = pos.Clone();
            posClone.UpdateRanges(Config.RangeLimits);

            var tCode = posClone.OSRCommandString(LastPosition);
            if (tCode.Trim().Length > 0)
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

        public string ResolveDefaultVariant()
        => Variants.FirstOrDefault("");
    }
}
