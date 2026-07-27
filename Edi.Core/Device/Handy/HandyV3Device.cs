using Edi.Core.Device;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PropertyChanged;
using System.Text;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    internal class HandyV3Device
        : DeviceBase<FunscriptRepository, FunscriptGallery>
    {
        // Set to false only to diagnose devices that reject HSP play(add).
        private const bool UseEmbeddedAddInPlay = true;
        private const int MaxPointsPerRequest = 100;
        private static readonly TimeSpan StreamingPollInterval =
            TimeSpan.FromMilliseconds(250);

        private readonly ILogger _logger;
        private readonly object _sessionInitializationSync = new();

        private HspState _hspState;
        private Task _sessionInitializationTask;
        private int _streamId = -1;
        private int _tailPointStreamIndex;
        private bool _isStopCalled;

        public HandyV3Device(
            HttpClient client,
            FunscriptRepository repository,
            ILogger logger)
            : base(repository, logger)
        {
            Client = client;
            Key = client.DefaultRequestHeaders
                .GetValues("X-Connection-Key")
                .First();
            Name = $"The Handy [{Key}]";
            _logger = logger;
            IsReady = true;

            _logger.LogInformation(
                $"HandyV3Device initialized with Key: {Key}.");
        }

        public string Key { get; }
        public HttpClient Client { get; }
        internal override bool SelfManagedLoop { get; set; } = true;

        internal override async Task applyRange()
        {
            _logger.LogInformation(
                $"Applying range for Key: {Key}, Min: {Min}, Max: {Max}.");
            var request = new SlideRequest(Min, Max);
            await Client.PutAsync(
                "v2/slide",
                JsonContent(request),
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
                        Math.Min(bufferCapacity, MaxPointsPerRequest),
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
                _hspState?.max_points ?? MaxPointsPerRequest);

        private async Task StartPlaybackWithoutAddRoundTrip(
            List<Point> points,
            long startTime,
            bool loop,
            CancellationToken cancellationToken)
        {
            if (UseEmbeddedAddInPlay
                && points.Count <= MaxPointsPerRequest)
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

            foreach (var chunk in points.Chunk(MaxPointsPerRequest))
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
                Math.Min(MaxPointsPerRequest, bufferCapacity);
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
            _logger.LogInformation(
                $"Initializing HSP session for Key: {Key}");

            var setupRequest = new
            {
                stream_id = Random.Shared.Next(1, int.MaxValue)
            };
            var response = await Client.PutAsync(
                "v3/hsp/setup",
                JsonContent(setupRequest),
                CancellationToken.None);
            _hspState = await ReadHspState(response, "setup");
            _streamId = _hspState.stream_id;
            _tailPointStreamIndex =
                _hspState.tail_point_stream_index;

            _logger.LogInformation(
                $"HSP session initialized. StreamId: {_streamId}, " +
                $"MaxPoints: {_hspState.max_points}");
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
            var response = await Client.PutAsync(
                "v3/hsp/add",
                JsonContent(request),
                cancellationToken);
            _hspState = await ReadHspState(response, "add");
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
            var response = await Client.PutAsync(
                "v3/hsp/play",
                JsonContent(request),
                cancellationToken);

            if (currentGallery is null
                || cancellationToken.IsCancellationRequested
                || _isStopCalled)
            {
                return;
            }

            _hspState = await ReadHspState(response, "play");
            _logger.LogInformation(
                $"Play command sent. PlayState: {_hspState.play_state}");
        }

        private int ReserveTailPointStreamIndex(int pointCount)
            => Interlocked.Add(
                ref _tailPointStreamIndex,
                pointCount);

        private async Task<HspState> ReadHspState(
            HttpResponseMessage response,
            string operation)
        {
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert
                .DeserializeObject<HspStateResult>(content)
                ?.result
                ?? throw new InvalidOperationException(
                    $"The Handy returned an invalid HSP {operation} response.");
        }

        public override async Task StopGallery()
        {
            _isStopCalled = true;
            _logger.LogInformation(
                $"Stopping gallery playback for Key: {Key}");

            try
            {
                var response = await Client.PutAsync(
                    "v3/hsp/stop",
                    null,
                    playCancelTokenSource.Token);
                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    $"Stopping operation canceled for Key: {Key}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    $"Error stopping gallery for Key: {Key}.");
            }
        }

        private StringContent JsonContent(object value)
            => new(
                JsonConvert.SerializeObject(value),
                Encoding.UTF8,
                "application/json");

        private long ServerTime =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            + ServerTimeSync.timeSyncAvrageOffset;
    }

    internal record PlaybackPlan(
        List<Point> Points,
        int Duration,
        long StartTime);

    internal record HspStateResult(HspState result);

    internal record HspState(
        int stream_id,
        int max_points,
        int points,
        int current_point,
        long current_time,
        bool loop,
        double playback_rate,
        long first_point_time,
        long last_point_time,
        string play_state,
        int tail_point_stream_index,
        int tail_point_stream_index_threshold);

    internal record OffsetRequest(int offset);

    internal record HspAddRequest(
        List<Point> points,
        bool flush,
        int tail_point_stream_index);

    internal record HspPlayRequest(
        int start_time,
        long server_time,
        double playback_rate,
        bool loop,
        [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        HspAddRequest add);

    internal record Point(int t, int x);
}
