using CsvHelper;
using CsvHelper.Configuration;
using Edi.Core.Device;
using Edi.Core.Funscript.Command;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Gallery.Index;
using Edi.Core.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    internal class HandyV3Device : DeviceBase<IndexRepository, IndexGallery>
    {
        private const int CHUNK_SIZE = 100;
        private const long SAFETY_MARGIN_MS = 7000;

        public string Key { get; set; }
        public HttpClient Client = null;
        internal override bool SelfManagedLoop { get; set; } = false;
        private readonly ILogger _logger;

        // HSP State tracking
        private HspState _hspState;
        private Dictionary<string, DynamicIndexGallery> _galleryIndex = new();
        private long _nextStartTime = 0;
        private int _streamId = -1;
        private Task _pointUploadTask;
        private GalleryBundlerConfig _configBundler;
        private HandyConfig _configHandy;
        private ScriptBuilder _sb = new ScriptBuilder();
        private bool isStopCalled;

        public HandyV3Device(HttpClient Client, IndexRepository repository ,ConfigurationManager configurationManager, ILogger logger) : base(repository, logger)
        {
            Key = Client.DefaultRequestHeaders.GetValues("X-Connection-Key").First();
            Name = $"The Handy [{Key}]";
            this.Client = Client;
            _logger = logger;
            _logger.LogInformation($"HandyV3Device initialized with Key: {Key}.");
            _configBundler =  configurationManager.Get<GalleryBundlerConfig>();
            _configHandy = configurationManager.Get<HandyConfig>();
            IsReady = true;
        }

        internal override async Task applyRange()
        {
            _logger.LogInformation($"Applying range for Key: {Key}, Min: {Min}, Max: {Max}.");
            var request = new SlideRequest(Min, Max);
            await Client.PutAsync("v2/slide", new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"), playCancelTokenSource.Token);
        }

        public override async Task PlayGallery(IndexGallery gallery, long seek = 0)
        {
            _logger.LogInformation($"PlayGallery called for gallery: {gallery?.Name}, seek: {seek}");

            
            SeekTime = seek;
            IsPause = false;

            try
            {
                // Initialize HSP if not already done
                if (_streamId == -1)
                {
                    await InitializeHspSession();
                }

                var points = new List<Point>(); 
                // Check if gallery is already loaded and valid
                string galleryKey = gallery.Name;
                if (!_galleryIndex.TryGetValue(currentGallery.Name, out var existingGallery) ||
                        !existingGallery.IsValid ||
                        !existingGallery.IsComplete)
                {
                
                    _logger.LogInformation($"Gallery {currentGallery.Name} already loaded and valid. Sending play command with seek: {seek}");

                    points = LoadGallery(gallery, seek);
                }

                // Load gallery


                // Send play command
                existingGallery = _galleryIndex[currentGallery.Name];
                CurrentDuration = existingGallery.TotalDuration + (gallery.Loop ? -_configBundler.RepeatDuration : -_configBundler.SpacerDuration);
                await SendPlayCommand(existingGallery.StartTime + CurrentTime, points);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error playing gallery: {ex.Message}");
                throw;
            }
        }

        private async Task InitializeHspSession()
        {
            _logger.LogInformation($"Initializing HSP session for Key: {Key}");

            try
            {
                var setupResponse = await Client.PutAsync("v3/hsp/setup", 
                    new StringContent(JsonConvert.SerializeObject(new  { stream_id = new Random(DateTime.Now.Millisecond).Next(3000) }), Encoding.UTF8, "application/json"),
                    playCancelTokenSource.Token);

                var responseContent = await setupResponse.Content.ReadAsStringAsync();
                _hspState = JsonConvert.DeserializeObject<HspStateResult>(responseContent)?.result;
                _streamId = _hspState.stream_id;
                _nextStartTime = 0;
                _galleryIndex.Clear();
                _logger.LogInformation($"HSP session initialized. StreamId: {_streamId}, MaxPoints: {_hspState.max_points}");

                
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing HSP session: {ex.Message}");
                throw;
            }
        }

        private List<Point> LoadGallery(IndexGallery gallery, long seek = 0)
        {
            string galleryKey = gallery.Name;
            _logger.LogInformation($"Loading gallery: {galleryKey}, seek: {seek}");

            var commands = gallery.Actions.OrderBy(c => c.at).ToList();

            if (commands.Count == 0)
            {
                _logger.LogWarning($"Gallery {galleryKey} has no commands.");
                return new List<Point>();
            }
            // Evaluate first 100 points
            var firstChunk = commands.Take(CHUNK_SIZE).ToList();
            long firstChunkEndTime = firstChunk.Last().at;
            long remainingTimeAfterSeek = firstChunkEndTime - seek;
            bool canStartFromBeginning = seek <= firstChunkEndTime && (firstChunk.Last().at - seek  < SAFETY_MARGIN_MS || remainingTimeAfterSeek >= SAFETY_MARGIN_MS);

            DynamicIndexGallery indexGallery;
            List<FunScriptAction> initialChunk;
            int initialIndex;

            if (canStartFromBeginning)
            {
                _logger.LogInformation($"Starting gallery {galleryKey} from beginning (seek within first 100 points)");
                initialChunk = firstChunk;
                initialIndex = initialChunk.Count;
            }
            else
            {
                _logger.LogInformation($"Starting gallery {galleryKey} from seek position: {seek}");
                var seekIndex = commands.FindIndex(c => c.at >= seek);
                initialChunk = commands.Skip(Math.Max(0, seekIndex)).Take(CHUNK_SIZE).ToList();
                initialIndex = seekIndex  + initialChunk.Count;
            }

            long galleryBufferStart = _nextStartTime;
            long galleryBufferEnd = _nextStartTime + firstChunk.Last().at;
            indexGallery = new DynamicIndexGallery
            {
                GalleryName = galleryKey,
                StartTime = _nextStartTime,
                SeekTime = seek,
                StartedFromBeginning = canStartFromBeginning,
                IsComplete = canStartFromBeginning ? commands.Count <= CHUNK_SIZE : commands.Count <= initialIndex,
                UploadedIndex = initialIndex,
                TotalDuration = Convert.ToInt32(commands.Last().at),
                BufferTimeRange = (galleryBufferStart, galleryBufferEnd)
            };

            // Add gallery to index
            _galleryIndex[galleryKey] = indexGallery;

            // Send initial chunk

            // Update next start time
          

            // Start background upload task for remaining points
            var points =  initialChunk.Select( cmd => new Point(
                (int)(cmd.at + _nextStartTime),
                cmd.pos
            )).ToList();
            

            _nextStartTime += indexGallery.TotalDuration;

            return points; 

            if (!indexGallery.IsComplete)
            {
                _pointUploadTask = UploadRemainingPointsAsync(gallery, indexGallery, initialIndex);
            }
        }

   

        private async Task UploadRemainingPointsAsync(IndexGallery gallery, DynamicIndexGallery indexGallery, int startIndex)
        {
            _logger.LogInformation($"Starting background upload for gallery: {indexGallery.GalleryName}, startIndex: {startIndex}");

            try
            {
                var commands = gallery.Actions.OrderBy(c => c.at).ToList();
                int currentIndex = startIndex;

                while (currentIndex < commands.Count && !playCancelTokenSource.Token.IsCancellationRequested)
                {
                    // Calculate current playback time
                    var lastCommand = commands[Math.Min(indexGallery.UploadedIndex - 1, commands.Count - 1)];
                    long estimatedPlaybackTime = lastCommand.at;

                    // Get next point time
                    if (currentIndex < commands.Count)
                    {
                        long nextPointTime = commands[currentIndex].at;
                        long timeUntilPlayback = nextPointTime - estimatedPlaybackTime;

                        // Wait until safety margin is within SAFETY_MARGIN_MS
                        if (timeUntilPlayback > SAFETY_MARGIN_MS)
                        {
                            long delayMs = timeUntilPlayback - SAFETY_MARGIN_MS;
                            await Task.Delay(Convert.ToInt32(delayMs), playCancelTokenSource.Token);
                        }
                    }

                    // Send next chunk
                    var chunk = commands.Skip(currentIndex).Take(CHUNK_SIZE).ToList();
                    if (chunk.Count > 0)
                    {
                        // Adjust times to absolute buffer time
                        var adjustedChunk = chunk.Select(cmd =>
                        {
                            var adjusted =new FunScriptAction { at = cmd.at, pos = cmd.pos };
                            adjusted.at += indexGallery.StartTime;
                            return adjusted;
                        }).ToList();

                        await SendPointChunk(adjustedChunk, indexGallery.StartTime, flush: false);
                        currentIndex += CHUNK_SIZE;
                        indexGallery.UploadedIndex = currentIndex;
                    }
                    else
                    {
                        break;
                    }
                }

                indexGallery.IsComplete = true;
                _logger.LogInformation($"Background upload completed for gallery: {indexGallery.GalleryName}");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"Background upload canceled for gallery: {indexGallery.GalleryName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during background upload for gallery {indexGallery.GalleryName}: {ex.Message}");
            }
        }
            
        private async Task SendPointChunk(List<FunScriptAction> points, long startTime, bool flush = false)
        {
            if (points.Count == 0)
                return;

            _logger.LogInformation($"Sending {points.Count} points, flush: {flush}");

            var pointList = points.Select(cmd => new Point(
                (int)(cmd.at + startTime),
                cmd.pos
            )).ToList();

            var addRequest = new HspAddRequest(pointList, flush, _hspState?.tail_point_stream_index + pointList.Count() ?? 0);

            try
            {
                var response = await Client.PutAsync("v3/hsp/add",
                    new StringContent(JsonConvert.SerializeObject(addRequest), Encoding.UTF8, "application/json"),
                    playCancelTokenSource.Token);

                var responseContent = await response.Content.ReadAsStringAsync();
                _hspState = JsonConvert.DeserializeObject<HspStateResult>(responseContent)?.result;

                _logger.LogInformation($"Points sent successfully. Buffer state: points={_hspState.points}, current_point={_hspState.current_point}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending point chunk: {ex.Message}");
                throw;
            }
        }

        private async Task SendPlayCommand(long startTime, List<Point> points)
        {
            _logger.LogInformation($"Sending play command with startTime: {startTime}");

            try
            {
                isStopCalled = false;
                var playRequest = new HspPlayRequest((int)startTime, ServerTime, 1.0, false, new(points));
                var token = playCancelTokenSource.Token;
                var response = await Client.PutAsync("v3/hsp/play",
                    new StringContent(JsonConvert.SerializeObject(playRequest), Encoding.UTF8, "application/json"),
                    token);

                if (currentGallery is null || token.IsCancellationRequested || isStopCalled)
                    return;

                var responseContent = await response.Content.ReadAsStringAsync();
                _hspState = JsonConvert.DeserializeObject<HspStateResult>(responseContent)?.result;

                _logger.LogInformation($"Play command sent. PlayState: {_hspState.play_state}");
            }
         
            catch (TaskCanceledException)
            {
                _logger.LogWarning($"Seek operation canceled for Key: {Key}.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending play command: {ex.Message}");
                throw;
            }
        }

        public override async Task StopGallery()
        {
            isStopCalled = true;
            _logger.LogInformation($"Stopping gallery playback for Key: {Key}");

            try
            {
                await Client.PutAsync("v3/hsp/stop", null, playCancelTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning($"stopping operation canceled for Key: {Key}.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping gallery: {ex.Message}");
            }
        }


        private long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ServerTimeSync.timeSyncAvrageOffset + _configHandy?.OffsetMS ?? 0;

        /// <summary>
        /// Validates gallery expiration based on HSP buffer state
        /// </summary>
        private void ValidateGalleriesAgainstBufferState()
        {
            
            _logger.LogInformation($"Validating galleries against buffer state. FirstTime: {_hspState.first_point_time}, LastTime: {_hspState.last_point_time}");

            foreach (var gallery in _galleryIndex.Values.ToList())
            {
                var (start, end) = gallery.BufferTimeRange;

                if (end < _hspState.first_point_time)
                {
                    gallery.State = GalleryState.Expired;
                    gallery.IsValid = false;
                    _logger.LogInformation($"Gallery {gallery.GalleryName} marked as expired.");
                }
                else if (start < _hspState.first_point_time && end > _hspState.first_point_time)
                {
                    gallery.State = GalleryState.PartiallyValid;
                    gallery.IsValid = true;
                    _logger.LogInformation($"Gallery {gallery.GalleryName} marked as partially valid.");
                }
                else if (start >= _hspState.first_point_time && end <= _hspState.last_point_time)
                {
                    gallery.State = GalleryState.Valid;
                    gallery.IsValid = true;
                    _logger.LogInformation($"Gallery {gallery.GalleryName} marked as valid.");
                }
            }
        }
    }

    /// <summary>
    /// Represents a gallery's state within the device buffer
    /// </summary>
    internal class DynamicIndexGallery
    {
        public string GalleryName { get; set; }
        public string GalleryId { get; set; }
        public long StartTime { get; set; }
        public long SeekTime { get; set; }
        public bool StartedFromBeginning { get; set; }
        public bool IsComplete { get; set; }
        public int UploadedIndex { get; set; }
        public int TotalDuration { get; set; }
        public (long start, long end) BufferTimeRange { get; set; }
        public GalleryState State { get; set; } = GalleryState.Valid;
        public bool IsValid { get; set; } = true;
    }

    internal enum GalleryState
    {
        Valid,
        PartiallyValid,
        Expired
    }
    #region HSP Models
    internal record HspStateResult(HspState result);

    internal record HspState(int stream_id, int max_points, int points, int current_point, long current_time, bool loop, double playback_rate, long first_point_time, long last_point_time, string play_state, int tail_point_stream_index, int tail_point_stream_index_threshold);

    internal record OffsetRequest(int offset);

    internal record HspAddRequest(List<Point> points, bool flush, int tail_point_stream_index);

    internal record HspPlayRequest(int start_time, long server_time, double playback_rate, bool loop, HspPlayAddRequest add);
    internal record HspPlayAddRequest(IEnumerable<Point> points);

    internal record Point(int t, int x)
    {
        public Point() : this(default, default) { }
    }

    #endregion
}

