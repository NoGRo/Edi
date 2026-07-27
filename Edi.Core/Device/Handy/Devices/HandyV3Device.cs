using Edi.Core.Device;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Services;
using Microsoft.Extensions.Logging;
using PropertyChanged;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    internal class HandyV3Device
        : DeviceBase<FunscriptRepository, FunscriptGallery>
    {
        // Set to false only to diagnose devices that reject HSP play(add).
        private const bool UseEmbeddedAddInPlay = true;
        private static readonly TimeSpan StreamingPollInterval =
            TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan WarmupSynchronizationDelay =
            TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan ClockSynchronizationLifetime =
            TimeSpan.FromMinutes(20);

        private readonly ILogger _logger;
        private readonly object _sessionInitializationSync = new();
        private readonly object _clockSynchronizationSync = new();
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Func<DateTimeOffset> _getUtcNow;

        private HspState _hspState;
        private Task _sessionInitializationTask;
        private Task _clockSynchronizationTask = Task.CompletedTask;
        private DateTimeOffset _clockSynchronizationValidUntil =
            DateTimeOffset.MinValue;
        private int _streamId = -1;
        private int _tailPointStreamIndex;
        private bool _isStopCalled;

        public HandyV3Device(
            IHandyClient client,
            FunscriptRepository repository,
            ILogger logger,
            Func<TimeSpan, CancellationToken, Task> delay = null,
            Func<DateTimeOffset> getUtcNow = null)
            : base(repository, logger)
        {
            Client = client;
            Key = client.Key;
            Name = client.DisplayName;
            _logger = logger;
            _delay = delay ?? Task.Delay;
            _getUtcNow = getUtcNow ?? (() => DateTimeOffset.UtcNow);
            IsReady = true;

            _logger.LogInformation("Handy V3 device initialized.");
        }

        public string Key { get; }
        public IHandyClient Client { get; }
        internal override bool SelfManagedLoop { get; set; } = true;

        internal override async Task applyRange()
        {
            _logger.LogInformation(
                $"Applying Handy range. Min: {Min}, Max: {Max}.");
            var request = new SlideRequest(Min, Max);
            await Client.SetStroke(
                request,
                playCancelTokenSource.Token);
        }

        public override Task PlayGallery(FunscriptGallery gallery, long seek = 0)
            => PlayGallery(gallery, seek, playCancelTokenSource.Token);

        protected override async Task PlayGallery(
            FunscriptGallery gallery,
            long seek,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                $"PlayGallery called for gallery: {gallery?.Name}, seek: {seek}");

            SeekTime = seek;
            IsPause = false;

            try
            {
                await EnsureHspSession();
                StartClockSynchronizationIfExpired();

                var plan = CreatePlaybackPlan(gallery, seek);
                CurrentDuration = plan.Duration;

                var bufferCapacity = GetBufferCapacity();
                var canUseDeviceLoop =
                    gallery.Loop && plan.Points.Count <= bufferCapacity;
                SelfManagedLoop = canUseDeviceLoop;

                if (gallery.Loop && !canUseDeviceLoop)
                {
                    _logger.LogWarning(
                        $"Gallery {gallery.Name} has {plan.Points.Count} points, " +
                        $"which exceeds the Handy buffer capacity of {bufferCapacity}. " +
                        "Falling back to EDI's virtual loop.");
                }

                var initialPointCount =
                    Math.Min(
                        Math.Min(
                            bufferCapacity,
                            Client.MaxPointsPerRequest),
                        plan.Points.Count);
                var initialPoints =
                    plan.Points.Take(initialPointCount).ToList();
                var remainingPoints =
                    plan.Points.Skip(initialPointCount).ToList();

                await StartPlaybackWithoutAddRoundTrip(
                    initialPoints,
                    plan.StartTime,
                    loop: canUseDeviceLoop,
                    cancellationToken);

                if (remainingPoints.Count > 0)
                {
                    _ = ObserveStreamingTask(
                        StreamRemainingPoints(
                            plan.Points,
                            initialPointCount,
                            remainingPoints,
                            bufferCapacity,
                            cancellationToken),
                        gallery.Name);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    $"Error playing gallery {gallery?.Name}.");
                throw;
            }
        }

        private PlaybackPlan CreatePlaybackPlan(
            FunscriptGallery gallery,
            long seek)
        {
            var orderedPoints = gallery.Commands?
                .OrderBy(command => command.AbsoluteTime)
                .Select(command => new Point(
                    Convert.ToInt32(command.AbsoluteTime),
                    Math.Clamp(
                        Convert.ToInt32(Math.Round(command.Value)),
                        0,
                        100)))
                .ToList();
            if (orderedPoints?.Count > 0 != true)
            {
                throw new InvalidOperationException(
                    $"Gallery '{gallery.Name}' has no points to play.");
            }

            var duration = gallery.Duration;
            if (duration <= 0)
                duration = orderedPoints.Last().t;

            var startTime = Math.Clamp(seek, 0, duration);
            var points = gallery.Loop
                ? orderedPoints
                    .Where(point => point.t <= duration)
                    .ToList()
                : SelectPointsFromSeek(orderedPoints, startTime);
            if (points.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Gallery '{gallery.Name}' has no points at seek {seek}.");
            }

            return new PlaybackPlan(points, duration, startTime);
        }

        private static List<Point> SelectPointsFromSeek(
            List<Point> points,
            long seek)
        {
            var firstAtOrAfterSeek =
                points.FindIndex(point => point.t >= seek);
            if (firstAtOrAfterSeek < 0)
                return [points.Last()];

            var startIndex = Math.Max(0, firstAtOrAfterSeek - 1);
            return points.Skip(startIndex).ToList();
        }

        private int GetBufferCapacity()
            => Math.Max(
                1,
                _hspState?.max_points
                ?? Client.MaxPointsPerRequest);

        private async Task StartPlaybackWithoutAddRoundTrip(
            List<Point> points,
            long startTime,
            bool loop,
            CancellationToken cancellationToken)
        {
            if (UseEmbeddedAddInPlay
                && points.Count <= Client.MaxPointsPerRequest)
            {
                var add = new HspAddRequest(
                    points,
                    flush: true,
                    ReserveTailPointStreamIndex(points.Count));
                await SendPlayCommand(
                    startTime,
                    loop,
                    add,
                    cancellationToken);
                return;
            }

            var bufferLoadTask = LoadInitialBuffer(
                points,
                cancellationToken);

            await SendPlayCommand(
                startTime,
                loop,
                add: null,
                cancellationToken);
            await bufferLoadTask;
        }

        private async Task LoadInitialBuffer(
            List<Point> points,
            CancellationToken cancellationToken)
        {
            var flush = true;

            foreach (var chunk in points.Chunk(
                Client.MaxPointsPerRequest))
            {
                var pointChunk = chunk.ToList();
                await SendPointChunk(
                    pointChunk,
                    flush,
                    cancellationToken);
                flush = false;
            }
        }

        private async Task StreamRemainingPoints(
            List<Point> allPoints,
            int initialPointCount,
            List<Point> remainingPoints,
            int bufferCapacity,
            CancellationToken cancellationToken)
        {
            var uploadChunkSize =
                Math.Min(
                    Client.MaxPointsPerRequest,
                    bufferCapacity);
            var uploadedPointCount = initialPointCount;

            foreach (var chunk in remainingPoints.Chunk(uploadChunkSize))
            {
                var pointChunk = chunk.ToList();
                var uploadedAfterChunk =
                    uploadedPointCount + pointChunk.Count;
                var pointsThatMustBeConsumed =
                    uploadedAfterChunk - bufferCapacity;
                if (pointsThatMustBeConsumed > 0)
                {
                    var consumedIndex = Math.Min(
                        pointsThatMustBeConsumed - 1,
                        allPoints.Count - 1);
                    await WaitUntilPlaybackReaches(
                        allPoints[consumedIndex].t,
                        cancellationToken);
                }

                await SendPointChunk(
                    pointChunk,
                    flush: false,
                    cancellationToken);
                uploadedPointCount = uploadedAfterChunk;
            }
        }

        private async Task WaitUntilPlaybackReaches(
            int playbackTime,
            CancellationToken cancellationToken)
        {
            while (CurrentTime < playbackTime)
            {
                var remaining =
                    TimeSpan.FromMilliseconds(playbackTime - CurrentTime);
                var delay = remaining < StreamingPollInterval
                    ? remaining
                    : StreamingPollInterval;
                await Task.Delay(delay, cancellationToken);
            }
        }

        private async Task ObserveStreamingTask(
            Task streamingTask,
            string galleryName)
        {
            try
            {
                await streamingTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    $"Error streaming remaining points for gallery {galleryName}.");
            }
        }

        private async Task EnsureHspSession()
        {
            Task initializationTask;
            lock (_sessionInitializationSync)
            {
                if (_streamId != -1)
                    return;

                initializationTask = _sessionInitializationTask
                    ??= InitializeHspSession();
            }

            try
            {
                await initializationTask;
            }
            catch
            {
                lock (_sessionInitializationSync)
                {
                    if (ReferenceEquals(
                        _sessionInitializationTask,
                        initializationTask))
                    {
                        _sessionInitializationTask = null;
                    }
                }

                throw;
            }
        }

        private async Task InitializeHspSession()
        {
            _logger.LogInformation("Initializing the Handy HSP session.");

            StartClockSynchronizationIfExpired();
            var setupRequest = new HspSetupRequest(
                Random.Shared.Next(1, int.MaxValue));
            _hspState = await Client.Setup(
                setupRequest,
                CancellationToken.None);
            _streamId = _hspState.stream_id;
            _tailPointStreamIndex =
                _hspState.tail_point_stream_index;

            _logger.LogInformation(
                $"HSP session initialized. StreamId: {_streamId}, " +
                $"MaxPoints: {_hspState.max_points}");
        }

        private void StartClockSynchronizationIfExpired()
        {
            TaskCompletionSource completion;
            lock (_clockSynchronizationSync)
            {
                if (_getUtcNow() < _clockSynchronizationValidUntil
                    || !_clockSynchronizationTask.IsCompleted)
                {
                    return;
                }

                completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _clockSynchronizationTask = completion.Task;
            }

            _ = ObserveWarmupSynchronization(
                SynchronizeDeviceClock(completion));
        }

        private async Task SynchronizeDeviceClock(
            TaskCompletionSource completion)
        {
            try
            {
                await Client.SynchronizeClock(CancellationToken.None);
                lock (_clockSynchronizationSync)
                {
                    _clockSynchronizationValidUntil =
                        _getUtcNow() + ClockSynchronizationLifetime;
                }
            }
            finally
            {
                completion.TrySetResult();
            }
        }

        private async Task SendPointChunk(
            List<Point> points,
            bool flush,
            CancellationToken cancellationToken)
        {
            if (points.Count == 0)
                return;

            var request = new HspAddRequest(
                points,
                flush,
                ReserveTailPointStreamIndex(points.Count));
            _hspState = await Client.AddPoints(
                request,
                cancellationToken);
        }

        private async Task SendPlayCommand(
            long startTime,
            bool loop,
            HspAddRequest add,
            CancellationToken cancellationToken)
        {
            _isStopCalled = false;
            var request = new HspPlayRequest(
                Convert.ToInt32(startTime),
                ServerTime,
                1.0,
                loop,
                add);
            var state = await Client.Play(
                request,
                cancellationToken);

            if (currentGallery is null
                || cancellationToken.IsCancellationRequested
                || _isStopCalled)
            {
                return;
            }

            _hspState = state;
            _logger.LogInformation(
                $"Play command sent. PlayState: {_hspState.play_state}");

            _ = ObserveWarmupSynchronization(
                SynchronizePlaybackAfterWarmup(cancellationToken));
        }

        private async Task SynchronizePlaybackAfterWarmup(
            CancellationToken cancellationToken)
        {
            await _delay(
                WarmupSynchronizationDelay,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (currentGallery is null || _isStopCalled)
                return;

            _hspState = await Client.SyncTime(
                new HspSyncTimeRequest(
                    CurrentTime,
                    ServerTime,
                    filter: 1.0),
                cancellationToken);
            _logger.LogInformation(
                "Handy HSP playback time synchronized after connection warm-up.");
        }

        private async Task ObserveWarmupSynchronization(Task task)
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
                _logger.LogWarning(
                    ex,
                    "Could not synchronize Handy HSP playback time after warm-up.");
            }
        }

        private int ReserveTailPointStreamIndex(int pointCount)
            => Interlocked.Add(
                ref _tailPointStreamIndex,
                pointCount);

        public override async Task StopGallery()
        {
            _isStopCalled = true;
            _logger.LogInformation("Stopping Handy gallery playback.");

            try
            {
                await Client.Stop(playCancelTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "The Handy stop operation was canceled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error stopping the Handy gallery.");
            }
        }

        private long ServerTime =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            + ServerTimeSync.timeSyncAvrageOffset;
    }

    internal record PlaybackPlan(
        List<Point> Points,
        int Duration,
        long StartTime);

}
