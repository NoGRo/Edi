using CsvHelper;
using CsvHelper.Configuration;
using Edi.Core.Device;
using Edi.Core.Device.Handy.Transport;
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
using System.Timers;
using System.Xml.Linq;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    internal class HandyV3Device : DeviceBase<FunscriptRepository, FunscriptGallery>
    {
        private const int CHUNK_SIZE = 100;
        private const long SAFETY_MARGIN_MS = 7000;

        public string Key { get; set; }
        internal override bool SelfManagedLoop { get; set; } = false;
        private readonly ILogger _logger;
        private readonly IHandyTransport _transport;
        private readonly HandyCommandExecutor _commandExecutor;

        // HSP State tracking
        private HandyHspState _hspState;
        private Dictionary<string, DynamicIndexGallery> _galleryIndex = new();
        private long _nextStartTime = 0;
        private int _streamId = -1;
        private Task _pointUploadTask;
        private GalleryBundlerConfig _configBundler;
        private HandyConfig _configHandy;
        private ScriptBuilder _sb = new ScriptBuilder();
        private bool isStopCalled;

        public HandyV3Device(IHandyTransport transport, FunscriptRepository repository, ConfigurationManager configurationManager, ILogger logger, string key) : base(repository, logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _commandExecutor = new HandyCommandExecutor(transport, logger);
            Key = key;
            Name = $"The Handy [{Key}]";
            _logger = logger;
            _logger.LogInformation($"HandyV3Device initialized with Key: {Key}.");
            _configBundler = configurationManager.Get<GalleryBundlerConfig>();
            _configHandy = configurationManager.Get<HandyConfig>();
            IsReady = true;
        }

        internal override async Task applyRange()
        {
            _logger.LogInformation($"Applying range for Key: {Key}, Min: {Min}, Max: {Max}.");
            await _commandExecutor.SetSlideAsync(Min, Max, playCancelTokenSource.Token);
        }

        public override async Task PlayGallery(FunscriptGallery gallery, long seek = 0)
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

                if (CurrentIndexGallery?.GalleryName == gallery.Name
                    && CurrentIndexGallery.IsComplete)
                {
                    await SendPlayCommandAsync(CurrentTime);
                }

                var points = LoadGallery(gallery, seek);

                await SendPlayCommandAsync(CurrentTime, points);

                if (!CurrentIndexGallery.IsComplete)
                {
                    await UploadRemainingPointsAsync();
                }
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
                int streamId = new Random(DateTime.Now.Millisecond).Next(3000);
                var setupResult = await _commandExecutor.SetupHspSessionAsync(streamId, playCancelTokenSource.Token);

                if (setupResult?.result == null)
                {
                    _logger.LogError("Failed to initialize HSP session - null result");
                    return;
                }

                _hspState = setupResult.result;
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

        private List<HandyPoint> LoadGallery(FunscriptGallery gallery, long seek = 0)
        {
            var commands = gallery.Commands;
            if (commands.Count == 0)
            {
                _logger.LogWarning($"Gallery {gallery.Name} has no commands.");
                return [];
            }

            var firstChunk = commands.Take(CHUNK_SIZE);
            var (chunk, index) = seek <= firstChunk.Last().AbsoluteTime 
                ? (firstChunk as IEnumerable<CmdLinear>, 0)
                : GetSeekChunk(commands, seek);

            var chunkList = chunk.Take(CHUNK_SIZE);
            CurrentIndexGallery = new DynamicIndexGallery
            {
                GalleryName = gallery.Name,
                FirtsIndex = index,
                LastIndex = index + chunkList.Count(),
                IsComplete = commands.Count <= index + CHUNK_SIZE
            };

            _logger.LogInformation($"Starting gallery {gallery.Name} from {(index == 0 ? "beginning" : $"seek position: {seek}")}");
            return chunkList.ToPoints();
        }

        private (IEnumerable<CmdLinear> chunk, int index) GetSeekChunk(List<CmdLinear> commands, long seek)
        {
            int seekIndex = commands.FindIndex(c => c.AbsoluteTime >= seek);
            return (commands.Skip(Math.Max(0, seekIndex)), seekIndex);
        }



        private async Task UploadRemainingPointsAsync()
        {
            try 
            { 
                while (!playCancelTokenSource.Token.IsCancellationRequested && !CurrentIndexGallery.IsComplete)
                {
                    List<HandyPoint> points = null; 

                    if (CurrentIndexGallery.LastIndex < currentGallery.Commands.Count())
                    {
                        points = currentGallery.Commands
                            .Skip(CurrentIndexGallery.LastIndex)
                            .Take(CHUNK_SIZE)
                            .ToPoints();
                        CurrentIndexGallery.LastIndex += points.Count();

                    }
                    else if (CurrentIndexGallery.FirtsIndex > 0  && currentGallery.Loop)
                    {
                        points = currentGallery.Commands
                            .Take(CurrentIndexGallery.FirtsIndex)
                            .TakeLast(CHUNK_SIZE)
                            .ToPoints();
                        CurrentIndexGallery.FirtsIndex -= points.Count();
                    }
                    if (points?.Any() == true)
                    {
                        await SendPointChunkAsync(points);
                    }

                    CurrentIndexGallery.IsComplete = CurrentIndexGallery.FirtsIndex == 0
                                                  && 
                                                    (!currentGallery.Loop 
                                                    || CurrentIndexGallery.LastIndex == currentGallery.Commands.Count);
                }                
                
            }
            catch (TaskCanceledException)
            {
                
            }
        }
       
        private async Task SendPointChunkAsync(List<HandyPoint> points, bool flush = false)
        {
            if (points.Count == 0)
                return;

            _logger.LogInformation($"Sending {points.Count} points, flush: {flush}");


            var addRequest = new HandyHspAddRequest(points, flush, _hspState?.tail_point_stream_index + points.Count ?? 0);
            var result = await _commandExecutor.AddPointsAsync(addRequest, playCancelTokenSource.Token);

            if (result?.result != null)
            {
                _hspState = result.result;
                _logger.LogInformation($"Points sent successfully. Buffer state: points={_hspState.points}, current_point={_hspState.current_point}");
            }
            else
            {
                _logger.LogError("Failed to send point chunk");
            }
        }

        private async Task SendPlayCommandAsync(long startTime, List<HandyPoint> points = null)
        {
            _logger.LogInformation($"Sending play command with startTime: {startTime}");

            try
            {
                isStopCalled = false;

                var playRequest = new HandyHspPlayRequest(
                    start_time: (int)startTime,
                    server_time: ServerTime,
                    playback_rate: 1.0,
                    flush: points != null,
                    loop: currentGallery.Loop,
                    add: new HandyHspPlayAddRequest(points));
                
                var token = playCancelTokenSource.Token;
                var result = await _commandExecutor.PlayAsync(playRequest, token);

                if (currentGallery is null || token.IsCancellationRequested || isStopCalled)
                    return;

                if (result?.result != null)
                {
                    _hspState = result.result;
                    _logger.LogInformation($"Play command sent. PlayState: {_hspState.play_state}");
                }
                else
                {
                    _logger.LogError("Failed to send play command");
                }
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
                await _commandExecutor.StopAsync(playCancelTokenSource.Token);
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

        private DynamicIndexGallery CurrentIndexGallery { get; set; }


    }

    /// <summary>
    /// Represents a gallery's state within the device buffer
    /// </summary>
    internal class DynamicIndexGallery
    {
        public string GalleryName { get; internal set; }
        public int FirtsIndex { get; internal set; }
        public bool IsComplete { get; internal set; }
        public int LastIndex { get; internal set; }
    }

    internal enum GalleryState
    {
        Valid,
        PartiallyValid,
        Expired
    }
}

