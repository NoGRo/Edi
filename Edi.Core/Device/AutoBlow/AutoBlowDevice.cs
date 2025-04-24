using System.Text.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
using System.Threading;
using PropertyChanged;
using System.Timers;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;
using Edi.Core.Device;

namespace Edi.Core.Device.AutoBlow
{
    [AddINotifyPropertyChangedInterface]
    internal class AutoBlowDevice : DeviceBase<IndexRepository, IndexGallery>
    {
        private readonly ILogger _logger;

        public string Key { get; set; }
        private static long timeSyncAvrageOffset;
        private static long timeSyncInitialOffset;
        public HttpClient Client = null;
        private string CurrentBundle = "default";
        private Task uploadTask { get; set; }
        private CancellationTokenSource uploadCancellationTokenSource;

        public AutoBlowDevice(HttpClient Client, IndexRepository repository, ILogger logger)
            : base(repository, logger)
        {
            _logger = logger;
            Key = Client.DefaultRequestHeaders.GetValues("x-device-token").First();
            Name = $"AutoBlow [{Key}]";
            IsReady = false;
            this.Client = Client;

            _logger.LogInformation($"AutoBlowDevice initialized with Name: {Name} and Key: {Key}");
        }

        internal override void SetVariant()
        {
            _logger.LogInformation($"Setting variant for AutoBlowDevice: {Name} with SelectedVariant: {SelectedVariant}");
            upload();
        }

        public override async Task PlayGallery(IndexGallery gallery, long seek = 0)
        {
            _logger.LogInformation($"PlayGallery called on {Name} for gallery {gallery.Name} with seek: {seek}");
            if (gallery.Bundle != CurrentBundle)
            {
                gallery = repository.Get(gallery.Name, SelectedVariant, CurrentBundle);

                if (gallery.Bundle != CurrentBundle)
                {
                    _logger.LogInformation($"Uploading new bundle {gallery.Bundle} for {Name}");
                    upload(gallery.Bundle, false);
                }
            }
            await Seek(gallery.StartTime + seek);
        }

        private async Task Seek(long timeMs)
        {
            if (!IsReady)
            {
                return;
            }
            _logger.LogInformation($"Seeking on {Name} to time {timeMs} for gallery {currentGallery?.Name ?? ""}");
            try
            {
                var req = new SyncPlayRequest(timeMs);
                await Client.PutAsync("sync-script/start", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during Seek on {Name}: {ex.Message}");
            }
        }

        public override async Task StopGallery()
        {
            if (!IsReady)
            {
                return;
            }
            _logger.LogInformation($"Stopping gallery on {Name}");
            try
            {
                await Client.PutAsync("sync-script/stop", null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during StopGallery on {Name}: {ex.Message}");
            }
        }

        private async void upload(string bundle = null, bool delay = true)
        {
            var previousCts = Interlocked.Exchange(ref uploadCancellationTokenSource, new CancellationTokenSource());
            previousCts?.Cancel(true);
            await Task.Delay(50);

            _ = Task.Run(async () =>
            {
                if (delay)
                {
                    try
                    {
                        _logger.LogInformation($"Delaying upload for {Name}");
                        await Task.Delay(3000, uploadCancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogInformation($"Upload task was canceled for {Name}");
                        return;
                    }
                }

                try
                {
                    _logger.LogInformation($"Stopping sync-script before upload for {Name}");
                    await Client.PutAsync("sync-script/stop", null, uploadCancellationTokenSource.Token);
                    IsReady = false;
                    CurrentBundle = bundle ?? CurrentBundle;

                    _logger.LogInformation($"Uploading bundle {CurrentBundle} for variant {SelectedVariant} on {Name}");
                    var file = repository.GetBundle($"{CurrentBundle}.{selectedVariant}", "csv");
                    var resp = await Client.PutAsync("sync-script/upload-csv",
                        new MultipartFormDataContent { { new StreamContent(file.OpenRead()), "file", $"EdiCurrentBundle{selectedVariant}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.csv".ToLower() } });

                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"Upload failed for {Name}. Status code: {resp.StatusCode}");
                        return;
                    }

                    var status = JsonConvert.DeserializeObject<Status>(await resp.Content.ReadAsStringAsync());
                    _logger.LogInformation($"Upload successful for {Name}. Device is now ready.");
                    IsReady = true;
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation($"Upload task canceled for {Name}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error during upload on {Name}: {ex.Message}");
                }
            });
        }

        private long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public record ServerTimeResponse(long serverTime);
    public record SyncPlayRequest(long startTimeMs);
    public record SyncUpload(string url);
    public record ConnectedResponse(bool connected, string cluster);
    public record Status(string OperationalMode, int LocalScript, int LocalScriptSpeed, int MotorTemperature, int OscillatorTargetSpeed, int OscillatorLowPoint, int OscillatorHighPoint, int SyncScriptCurrentTime, int SyncScriptOffsetTime, string SyncScriptToken, bool SyncScriptLoop);
    public record ModeRequest(int mode);
    public record ErrorDetails(int Code, string Name, string Message, bool Connected);
    public record SlideRequest(int min, int max);
    public record ErrorResponse(ErrorDetails Error);
}
