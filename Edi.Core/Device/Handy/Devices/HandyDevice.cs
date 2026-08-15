using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Edi.Core.Funscript;
using CsvHelper.Configuration;
using Edi.Core.Gallery;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.Definition;
using System.Runtime.CompilerServices;
using PropertyChanged;
using System.Timers;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    internal class HandyDevice
        : DeviceBase<IndexRepository, IndexGallery>,
          IDeviceWithOffsetConfiguration
    {
        private readonly ILogger _logger;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public string Key { get; set; }
        public HttpClient Client = null;

        private string CurrentBundle = "default";
        private bool isStopCalled;
        public HandyDevice(
            HttpClient client,
            IndexRepository repository,
            ILogger logger,
            Func<TimeSpan, CancellationToken, Task> delay = null,
            int defaultOffset = -80)
            : base(repository, logger)
        {


            _logger = logger;
            Key = client.DefaultRequestHeaders.GetValues("X-Connection-Key").First();
            Name = $"The Handy [{Key}]";

            IsReady = repository?.BundlerConfig.DisableBundler == true;
            Client = client;
            _delay = delay ?? Task.Delay;
            EnableOffset(defaultOffset);
            _logger.LogInformation($"HandyDevice initialized with Key: {Key}.");
        }

        public void ApplyConfiguration(DeviceConfig configuration)
            => ApplyOffsetConfiguration(configuration);

        protected override Task ApplyOffset(
            int offsetMilliseconds,
            CancellationToken cancellationToken)
            => HandyProvider.ApplyOffset(
                Client,
                usesV3Api: false,
                offsetMilliseconds,
                cancellationToken);

        internal override void SetVariant()
        {
            _logger.LogInformation($"Setting variant for Key: {Key} with SelectedVariant: {SelectedVariant}.");
            if (repository?.BundlerConfig.DisableBundler != true)
                upload();
        }

        internal override async Task applyRange()
        {
            _logger.LogInformation($"Applying range for Key: {Key}, Min: {Min}, Max: {Max}.");
            var request = new SlideRequest(Min, Max);
            await Client.PutAsync("v2/slide", new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"));
        }

        public override Task PlayGallery(IndexGallery gallery, long seek = 0)
            => PlayGallery(gallery, seek, playCancelTokenSource.Token);

        protected override async Task PlayGallery(
            IndexGallery gallery,
            long seek,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Starting gallery '{gallery?.Name}' on Key: {Key} with seek: {seek}.");

            if (gallery.Bundle != CurrentBundle)
            {
                gallery = repository.Get(gallery.Name, SelectedVariant, CurrentBundle);//find in current bundle 
                currentGallery = gallery;
                if (gallery.Bundle != CurrentBundle)//not in the current uploaded bundle 
                {
                    upload(gallery.Bundle, false);
                }
            }
            await Seek(cancellationToken);
        }

        private async Task Seek(CancellationToken cancellationToken)
        {
            if (!IsReady)
            {
                _logger.LogWarning($"Device not ready for playback. Key: {Key}");
                return;
            }

            try
            {
                isStopCalled = false;
                var req = new SyncPlayRequest(ServerTime, currentGallery.StartTime + CurrentTime);
                Debug.WriteLine($"Handy: [{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}] {req.estimatedServerTime} {Key} PLay [{req.startTime}] ({currentGallery?.Name ?? ""}))");
                await Client.PutAsync("v2/hssp/play", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"), cancellationToken);
                await _delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
                if (currentGallery is null || cancellationToken.IsCancellationRequested || isStopCalled)
                    return;

                req = new SyncPlayRequest(ServerTime, currentGallery.StartTime + CurrentTime);
                Debug.WriteLine($"Handy: [{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}] {req.estimatedServerTime} {Key} PLay AfterWarmup [{req.startTime}] ({currentGallery?.Name ?? ""}))");
                await Client.PutAsync("v2/hssp/play", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning($"Seek operation canceled for Key: {Key}.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during seek for Key: {Key} - {ex.Message}");
            }
        }

        public override async Task StopGallery()
        {
            isStopCalled = true;
            if (!IsReady)
            {
                _logger.LogWarning($"Device not ready to stop playback. Key: {Key}");
                return;
            }

            _logger.LogInformation($"Stopping gallery playback for Key: {Key}.");

            try
            {
                await Client.PutAsync("v2/hssp/stop", null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping gallery for Key: {Key} - {ex.Message}");
            }
        }
        private Task uploadTask { get; set; }
        private CancellationTokenSource uploadCancellationTokenSource;

        private async void upload(string bundle = null, bool delay = true)
        {
            IsReady = false;
            Interlocked.Exchange(ref uploadCancellationTokenSource, new CancellationTokenSource())?.Cancel(true);
            uploadTask = Task.Run(async () =>
            {
                if (delay)
                {
                    try
                    {
                        await Task.Delay(3000, uploadCancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogWarning($"Upload task canceled for Key: {Key}.");
                        return;
                    }
                }

                try
                {
                    _logger.LogInformation($"Starting upload for Key: {Key}, Bundle: {bundle ?? CurrentBundle}.");

                    Task pause = Client.PutAsync("v2/hssp/stop", null, uploadCancellationTokenSource.Token);
                    IsReady = false;

                    CurrentBundle = bundle ?? CurrentBundle;
                    var blob = await uploadBlob(repository.GetBundle($"{CurrentBundle}.{selectedVariant}", "csv"));

                    await pause;

                    var resp = await Client.PutAsync("v2/hssp/setup", new StringContent(JsonConvert.SerializeObject(new SyncUpload(blob)), Encoding.UTF8, "application/json"), uploadCancellationTokenSource.Token);
                    var result = await resp.Content.ReadAsStringAsync();

                    if (result.Contains("timeout"))
                    {
                        _logger.LogWarning($"Upload timed out for Key: {Key}.");
                    }

                    IsReady = true;
                    _logger.LogInformation($"Upload completed and device is ready for Key: {Key}.");
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning($"Upload task canceled for Key: {Key}.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error during upload for Key: {Key} - {ex.Message}");
                }
            });
        }

        private async Task<string> uploadBlob(FileInfo file)
        {
            _logger.LogInformation($"Uploading blob for file: {file.Name}.");

            using (var blobClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://www.handyfeeling.com/api/sync/upload");
                var content = new MultipartFormDataContent
                {
                    { new StreamContent(file.OpenRead()), "syncFile", "Edi.csv" }
                };
                request.Content = content;

                var resp = await blobClient.SendAsync(request, uploadCancellationTokenSource.Token);
                var uploadResult = JsonConvert.DeserializeObject<SyncUpload>(await resp.Content.ReadAsStringAsync(uploadCancellationTokenSource.Token));

                _logger.LogInformation($"Blob upload completed for file: {file.Name} with URL: {uploadResult.url}.");
                return uploadResult.url;
            }
        }



        private long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ServerTimeSync.timeSyncAvrageOffset;
    }

    // Example usage:
    // await ServerTimeSync.SyncServerTimeAsync(10);
    // var serverTime = ServerTimeSync.GetEstimatedServerTime();
    // Console.WriteLine($"Estimated Server Time: {serverTime}");

    public record ServerTimeResponse(long serverTime);
    public record SyncPlayRequest(long estimatedServerTime, long startTime);
    public record SyncUpload(string url);
    public record ConnectedResponse(bool connected);
    public record ModeRequest(int mode);
    public record ErrorDetails(int Code, string Name, string Message, bool Connected);
    public record SlideRequest(int min, int max);
    public record ErrorResponse(ErrorDetails Error);

}

