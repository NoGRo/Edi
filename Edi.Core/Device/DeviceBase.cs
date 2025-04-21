using Edi.Core.Gallery;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Timers;
using PropertyChanged;
using Newtonsoft.Json.Linq;
using Edi.Core.Device.Interfaces;

namespace Edi.Core.Device
{
    [AddINotifyPropertyChangedInterface]
    public abstract class DeviceBase<TRepository, TGallery> : IDevice, IRange
        where TRepository : class, IGalleryRepository<TGallery>
        where TGallery : class, IGallery
    {
        private readonly ILogger _logger;
        protected TRepository repository { get; }
        protected TGallery currentGallery;

        public bool IsPause { get; set; } = true;

        protected DeviceBase(TRepository repository, ILogger logger)
        {
            this.repository = repository;
            _logger = logger;
            timerRange.Elapsed += TimerRange_Elapsed;
            _logger.LogInformation($"Device '{Name}' initialized with repository.");
        }

        public virtual bool IsReady { get; set; } = true;
        internal virtual bool SelfManagedLoop { get; set; } = false;

        public string Channel { get; set; }

        internal string selectedVariant;
        public virtual string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                if (selectedVariant != value)
                {
                    _logger.LogInformation($"Device '{Name}': SelectedVariant changed from '{selectedVariant}' to '{value}'.");
                    selectedVariant = value;
                }
            }
        }

        public void Resume()
        {
            if (currentGallery != null && !IsPause)
            {
                _logger.LogInformation($"Device '{Name}': Resuming gallery playback for '{currentGallery.Name}' at time {CurrentTime}.");
                PlayGallery(currentGallery.Name, CurrentTime).GetAwaiter();
            }
        }

        internal virtual void SetVariant()
        {
            _logger.LogInformation($"Device '{Name}': Setting variant for SelectedVariant: '{SelectedVariant}'");
        }

        public virtual IEnumerable<string> Variants => repository.GetVariants();

        public string Name { get; set; }
        public DateTime SyncSend { get; private set; }
        public long SeekTime { get; internal set; }
        public int CurrentTime => currentGallery == null ? 0 : Convert.ToInt32(((DateTime.Now - SyncSend).TotalMilliseconds + SeekTime) % currentGallery.Duration);

        private System.Timers.Timer timerRange = new System.Timers.Timer(100);
        private Task TimerRangeTask;
        private Task PlayStopRangeTask;
        private int lastMin;
        private int lastMax = 100;

        internal virtual async Task applyRange() { }
        internal bool isStopRange(int min, int max) => min == max;
        private async void TimerRange_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (TimerRangeTask != null && !TimerRangeTask.IsCompleted)
                return;

            if (min == lastMin && max == lastMax)
            {
                _logger.LogInformation($"Device '{Name}': Range values are unchanged. Stopping timer.");
                timerRange.Stop();
                return;
            }
            var resume = isStopRange(lastMin, lastMax)
                        && !isStopRange(min, max);

            lastMax = max;
            lastMin = min;

            if (TimerRangeTask != null)
                await TimerRangeTask;

            if (!isStopRange(min, max))
                TimerRangeTask = applyRange();

            if (currentGallery == null)
                return;

            if (resume)
                PlayStopRangeTask = PlayGallery(currentGallery, CurrentTime);
            else if (isStopRange(min, max))
            {
                _ = StopGallery();
            }



        }

        public record SlideRequest(int min, int max);
        private int max = 100;
        private int min;
        public int Min
        {
            get => min;
            set
            {
                min = value;
                if (!timerRange.Enabled)
                {
                    _logger.LogInformation($"Device '{Name}': Min changed to {min}. Starting timer.");
                    timerRange.Start();
                }
            }
        }
        public int Max
        {
            get => max;
            set
            {
                max = value;
                if (!timerRange.Enabled)
                {
                    _logger.LogInformation($"Device '{Name}': Max changed to {max}. Starting timer.");
                    timerRange.Start();
                }
            }
        }



        internal CancellationTokenSource playCancelTokenSource = new CancellationTokenSource();
        public virtual async Task PlayGallery(string name, long seek = 0)
        {
            var previousCts = Interlocked.Exchange(ref playCancelTokenSource, new CancellationTokenSource());
            previousCts?.Cancel(true);
            _logger.LogInformation($"Device '{Name}': Playing gallery '{name}' with seek: {seek}.");

            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null || Max == 0)
            {
                _logger.LogInformation($"Device '{Name}': Gallery is null or unplayable (name: '{name}', variant: '{SelectedVariant}', max: {Max}). Stopping playback.");
                await Stop();
                return;
            }

            SeekTime = seek;
            SyncSend = DateTime.Now;
            currentGallery = gallery;
            IsPause = false;

            if (!isStopRange(Min, Max))
                _ = PlayGallery(gallery, seek);

            if (SelfManagedLoop)
                return;

            var interval = gallery.Duration - seek;
            var token = playCancelTokenSource.Token;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(interval), token);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation($"Device '{Name}': Playback task was canceled for gallery '{name}'.");
                return;
            }

            if (token.IsCancellationRequested)
                return;

            if (currentGallery?.Loop == true && !IsPause)
            {
                _logger.LogInformation($"Device '{Name}': Looping gallery playback for '{currentGallery.Name}'.");
                _ = PlayGallery(currentGallery.Name);
            }
            else
            {
                await Stop();
            }
        }

        public abstract Task PlayGallery(TGallery gallery, long seek = 0);

        public virtual async Task Stop()
        {
            var previousCts = Interlocked.Exchange(ref playCancelTokenSource, new CancellationTokenSource());
            previousCts?.Cancel(true);

            currentGallery = null;
            IsPause = true;
            _logger.LogInformation($"Device '{Name}': Stopping gallery playback.");

            await StopGallery();
        }

        public abstract Task StopGallery();

        public virtual string DefaultVariant()
            => Variants.FirstOrDefault("");
    }
}
