using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Microsoft.Extensions.Logging;
using PropertyChanged;
using System.ComponentModel;
using System.Timers;

namespace Edi.Core.Device
{
    [AddINotifyPropertyChangedInterface]
    public abstract class DeviceBase<TRepository, TGallery> : IDevice, IRange
        where TRepository : class, IGalleryRepository<TGallery>
        where TGallery : class, IGallery
    {
        private readonly ILogger logger;
        private readonly SemaphoreSlim stateLock = new(1, 1);
        private readonly System.Timers.Timer rangeTimer = new(100) { AutoReset = false };
        private Task activeDeviceTask = Task.CompletedTask;
        private DeviceConfig offsetConfiguration;
        private long commandVersion;
        private long rangeVersion;

        protected TRepository repository { get; }
        protected TGallery currentGallery;
        internal CancellationTokenSource playCancelTokenSource = new();

        protected DeviceBase(TRepository repository, ILogger logger)
        {
            this.repository = repository;
            this.logger = logger;
            rangeTimer.Elapsed += RangeTimerElapsed;
        }

        public virtual bool IsReady { get; set; } = true;
        public bool IsPause { get; set; } = true;
        internal virtual bool SelfManagedLoop { get; set; }
        public string Channel { get; set; }
        public string Name { get; set; }

        internal string selectedVariant;
        public virtual string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                if (selectedVariant == value)
                    return;

                selectedVariant = value;
                if (value != null && value != "None")
                    SetVariant();
            }
        }

        public int RepositoryVersion { get; private set; }

        [DependsOn(nameof(RepositoryVersion))]
        public virtual IEnumerable<string> Variants => repository.GetVariants();
        public DateTime SyncSend { get; private set; }
        public long SeekTime { get; internal set; }
        internal int CurrentDuration;
        public int CurrentTime
        {
            get
            {
                var gallery = currentGallery;
                var duration = CurrentDuration;
                if (gallery == null || duration <= 0)
                    return 0;

                var elapsed = ElapsedPlaybackTime;
                return gallery.Loop
                    ? (int)((elapsed % duration + duration) % duration)
                    : (int)Math.Clamp(elapsed, 0, duration);
            }
        }

        private int min;
        private int max = 100;
        internal int lastMin;
        internal int lastMax = 100;

        public int Min
        {
            get => min;
            set
            {
                min = value;
                RestartRangeTimer();
            }
        }

        public int Max
        {
            get => max;
            set
            {
                max = value;
                RestartRangeTimer();
            }
        }

        public int OffsetMilliseconds { get; private set; }
        public Task OffsetUpdate { get; private set; } = Task.CompletedTask;

        public void Resume()
        {
            var gallery = currentGallery;
            if (gallery != null && IsPause)
                Observe(PlayGallery(gallery.Name, CurrentTime), "resuming playback");
        }

        internal virtual void SetVariant() { }
        public virtual void RefreshRepository()
        {
            RepositoryVersion++;
            if (!string.IsNullOrEmpty(selectedVariant)
                && selectedVariant != "None")
            {
                SetVariant();
            }
        }

        internal virtual Task applyRange() => Task.CompletedTask;
        internal bool isStopRange(int min, int max) => min == max;

        protected void EnableOffset(int initialOffset)
            => OffsetMilliseconds = DeviceOffset.Normalize(initialOffset);

        protected void ApplyOffsetConfiguration(
            DeviceConfig configuration)
        {
            RemoveConfiguration();
            configuration.OffsetMS ??= OffsetMilliseconds;
            offsetConfiguration = configuration;
            ((INotifyPropertyChanged)configuration).PropertyChanged +=
                OffsetConfigurationChanged;
            ApplyConfiguredOffset();
        }

        public virtual void RemoveConfiguration()
        {
            if (offsetConfiguration is INotifyPropertyChanged changed)
                changed.PropertyChanged -= OffsetConfigurationChanged;
            offsetConfiguration = null;
        }

        protected virtual Task ApplyOffset(
            int offset,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        private void OffsetConfigurationChanged(
            object sender,
            PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(DeviceConfig.OffsetMS))
                ApplyConfiguredOffset();
        }

        private void ApplyConfiguredOffset()
        {
            OffsetMilliseconds = offsetConfiguration.OffsetMS
                ?? OffsetMilliseconds;
            OffsetUpdate = ApplyOffset(
                OffsetMilliseconds,
                CancellationToken.None);
            Observe(OffsetUpdate, "applying playback offset");
        }

        public virtual async Task PlayGallery(string name, long seek = 0)
        {
            var version = Interlocked.Increment(ref commandVersion);
            TGallery gallery = null;
            CancellationTokenSource source = null;
            CancellationToken token = default;
            Task deviceTask = Task.CompletedTask;

            logger.LogDebug(
                "{DeviceType} playback command {CommandVersion} is waiting for the state lock. Gallery: {Gallery}",
                GetType().Name,
                version,
                name);
            await stateLock.WaitAsync();
            try
            {
                logger.LogDebug(
                    "{DeviceType} playback command {CommandVersion} acquired the state lock. Gallery: {Gallery}",
                    GetType().Name,
                    version,
                    name);
                if (version != Volatile.Read(ref commandVersion))
                    return;

                CancelActiveTask();
                gallery = repository.Get(name, SelectedVariant);
                if (version != Volatile.Read(ref commandVersion))
                    return;

                if (gallery == null || Max == 0)
                {
                    currentGallery = null;
                    IsPause = true;
                    await StopDevice();
                    return;
                }

                if (Min != lastMin || Max != lastMax)
                {
                    if (!isStopRange(Min, Max))
                        await applyRange();
                    lastMin = Min;
                    lastMax = Max;
                }

                (source, token, deviceTask) = StartPlayback(gallery, seek);
            }
            finally
            {
                stateLock.Release();
            }

            Observe(
                MonitorPlayback(version, gallery, source, token, deviceTask),
                $"monitoring gallery '{gallery.Name}'");

            try
            {
                await deviceTask;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        public abstract Task PlayGallery(TGallery gallery, long seek = 0);

        protected virtual Task PlayGallery(
            TGallery gallery,
            long seek,
            CancellationToken cancellationToken)
            => PlayGallery(gallery, seek);

        public virtual async Task Stop()
        {
            var version = Interlocked.Increment(ref commandVersion);
            rangeTimer.Stop();
            await stateLock.WaitAsync();
            try
            {
                if (version != Volatile.Read(ref commandVersion))
                    return;

                CancelActiveTask();
                currentGallery = null;
                IsPause = true;
                await StopDevice();
            }
            finally
            {
                stateLock.Release();
            }
        }

        public abstract Task StopGallery();
        public virtual string DefaultVariant() => Variants.FirstOrDefault("");

        private (
            CancellationTokenSource Source,
            CancellationToken Token,
            Task DeviceTask) StartPlayback(TGallery gallery, long seek)
        {
            SeekTime = seek;
            SyncSend = GetUtcNow();
            currentGallery = gallery;
            CurrentDuration = gallery.Duration;
            IsPause = false;

            var source = playCancelTokenSource;
            var task = isStopRange(Min, Max)
                ? Task.CompletedTask
                : PlayGallery(gallery, seek, source.Token) ?? Task.CompletedTask;
            activeDeviceTask = task;
            return (source, source.Token, task);
        }

        private async Task MonitorPlayback(
            long version,
            TGallery gallery,
            CancellationTokenSource source,
            CancellationToken token,
            Task deviceTask)
        {
            try
            {
                var durationTask = PlaybackDelay(
                    GetPlaybackCompletionDelay(gallery),
                    token);
                if (await Task.WhenAny(deviceTask, durationTask) == deviceTask)
                    await deviceTask;

                await durationTask;
                await CompletePlayback(version, gallery, source);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private async Task CompletePlayback(
            long version,
            TGallery gallery,
            CancellationTokenSource source)
        {
            Task nextLifecycle = null;
            await stateLock.WaitAsync();
            try
            {
                if (version != Volatile.Read(ref commandVersion)
                    || !ReferenceEquals(source, playCancelTokenSource)
                    || gallery.Loop && !IsPause && SelfManagedLoop)
                {
                    return;
                }

                var shouldLoop = gallery.Loop && !IsPause;
                CancelActiveTask();
                if (shouldLoop)
                {
                    var (nextSource, nextToken, nextTask) =
                        StartPlayback(
                            gallery,
                            NormalizeLoopSeek(
                                ElapsedPlaybackTime,
                                gallery.Duration));
                    nextLifecycle = MonitorPlayback(
                        version,
                        gallery,
                        nextSource,
                        nextToken,
                        nextTask);
                }
                else
                {
                    currentGallery = null;
                    IsPause = true;
                    await StopDevice();
                }
            }
            finally
            {
                stateLock.Release();
            }

            if (nextLifecycle != null)
                Observe(nextLifecycle, $"monitoring loop '{gallery.Name}'");
        }

        private static int NormalizeLoopSeek(
            long elapsedTime,
            int duration)
            => duration <= 0 || elapsedTime < duration
                ? 0
                : (int)(elapsedTime % duration);

        protected long ElapsedPlaybackTime =>
            (long)(GetUtcNow() - SyncSend).TotalMilliseconds + SeekTime;

        protected virtual TimeSpan GetPlaybackCompletionDelay(TGallery gallery)
            => TimeSpan.FromMilliseconds(
                Math.Max(0, gallery.Duration - CurrentTime));

        private void CancelActiveTask()
        {
            var previousSource = playCancelTokenSource;
            var previousTask = activeDeviceTask;
            logger.LogDebug(
                "{DeviceType} is cancelling the previous playback task",
                GetType().Name);
            previousSource.Cancel();
            logger.LogDebug(
                "{DeviceType} cancelled the previous playback task",
                GetType().Name);
            playCancelTokenSource = new CancellationTokenSource();
            activeDeviceTask = Task.CompletedTask;
            Observe(
                DisposeAfterCompletion(previousTask, previousSource),
                "finishing cancelled playback");
        }

        private static async Task DisposeAfterCompletion(
            Task task,
            CancellationTokenSource source)
        {
            try
            {
                await task;
            }
            finally
            {
                source.Dispose();
            }
        }

        private async Task StopDevice()
        {
            activeDeviceTask = StopGallery() ?? Task.CompletedTask;
            await activeDeviceTask;
            activeDeviceTask = Task.CompletedTask;
        }

        private void RestartRangeTimer()
        {
            rangeVersion = Volatile.Read(ref commandVersion);
            rangeTimer.Stop();
            rangeTimer.Start();
        }

        private async void RangeTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                await ApplyRangeChange(Volatile.Read(ref rangeVersion));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Device '{Name}' failed to apply its range.");
            }
        }

        private async Task ApplyRangeChange(long expectedVersion)
        {
            Task lifecycle = null;
            await stateLock.WaitAsync();
            try
            {
                if (expectedVersion != Volatile.Read(ref commandVersion))
                    return;

                var nextMin = Min;
                var nextMax = Max;
                if (nextMin == lastMin && nextMax == lastMax)
                    return;

                var wasStopped = isStopRange(lastMin, lastMax);
                var isStopped = isStopRange(nextMin, nextMax);
                if (!isStopped)
                    await applyRange();

                lastMin = nextMin;
                lastMax = nextMax;
                if (currentGallery == null)
                    return;

                if (isStopped)
                {
                    CancelActiveTask();
                    await StopDevice();
                }
                else if (wasStopped
                         && expectedVersion == Volatile.Read(ref commandVersion))
                {
                    var gallery = currentGallery;
                    var (source, token, task) = StartPlayback(gallery, CurrentTime);
                    lifecycle = MonitorPlayback(
                        expectedVersion,
                        gallery,
                        source,
                        token,
                        task);
                }
            }
            finally
            {
                stateLock.Release();
            }

            if (lifecycle != null)
                Observe(lifecycle, "monitoring playback after a range change");
        }

        private void Observe(Task task, string operation)
            => _ = ObserveTask(task, operation);

        internal virtual DateTime GetUtcNow() => DateTime.UtcNow;

        internal virtual Task PlaybackDelay(
            TimeSpan delay,
            CancellationToken cancellationToken)
            => Task.Delay(delay, cancellationToken);

        private async Task ObserveTask(Task task, string operation)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Device '{Name}' failed while {operation}.");
            }
        }
    }
}
