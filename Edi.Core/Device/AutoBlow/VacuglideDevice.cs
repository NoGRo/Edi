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
using System.Diagnostics.CodeAnalysis;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.Definition;
using System.Runtime.CompilerServices;
using PropertyChanged;
using System.Timers;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;
using Edi.Core.Device;

namespace Edi.Core.Device.AutoBlow
{
    [AddINotifyPropertyChangedInterface]
    internal class VacuglideDevice : DeviceBase<IndexRepository, IndexGallery>
    {
        private readonly ILogger _logger;

        public string Key { get; set; }
        public string Cluster { get; set; }
        public HttpClient Client = null;
        private string CurrentBundle = "default";
        private CancellationTokenSource uploadCancellationTokenSource;
        private bool isStopCalled;

        public VacuglideDevice(HttpClient client, IndexRepository repository, ILogger logger)
            : base(repository, logger)
        {
            _logger = logger;
            Key = client.DefaultRequestHeaders.GetValues("x-device-token").First();
            Name = $"Vacuglide [{Key}]";
            IsReady = false;
            this.Client = client;

            _logger.LogInformation($"VacuglideDevice initialized with Name: {Name} and Key: {Key}");
        }

        internal override void SetVariant()
        {
            _logger.LogInformation($"Setting variant for VacuglideDevice: {Name} with SelectedVariant: {SelectedVariant}");
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
                _logger.LogWarning($"Device not ready for playback. Key: {Key}");
                return;
            }

            try
            {
                isStopCalled = false;
                var req = new VacuSyncScriptStartRequest(timeMs);
                var token = playCancelTokenSource.Token;

                _logger.LogInformation($"Seeking on {Name} to time {timeMs}");
                await Client.PutAsync("/vacuglide/sync-script/start", 
                    new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"), token);
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning($"Seek operation canceled for {Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during Seek on {Name}: {ex.Message}");
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

            _logger.LogInformation($"Stopping gallery on {Name}");
            try
            {
                await Client.PutAsync("/vacuglide/sync-script/stop", null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during StopGallery on {Name}: {ex.Message}");
            }
        }

        private async void upload(string bundle = null, bool delay = true)
        {
            IsReady = false;
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
                    await Client.PutAsync("/vacuglide/sync-script/stop", null, uploadCancellationTokenSource.Token);

                    CurrentBundle = bundle ?? CurrentBundle;

                    _logger.LogInformation($"Uploading bundle {CurrentBundle} for variant {SelectedVariant} on {Name}");
                    var file = repository.GetBundle($"{CurrentBundle}.{selectedVariant}", "csv");
                    
                    var content = new MultipartFormDataContent 
                    { 
                        { 
                            new StreamContent(file.OpenRead()), 
                            "file", 
                            $"EdiBundle{selectedVariant}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.csv".ToLower() 
                        } 
                    };

                    var resp = await Client.PutAsync("/vacuglide/sync-script/upload-csv", content, uploadCancellationTokenSource.Token);

                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"Upload failed for {Name}. Status code: {resp.StatusCode}");
                        return;
                    }

                    var responseContent = await resp.Content.ReadAsStringAsync(uploadCancellationTokenSource.Token);
                    var status = JsonConvert.DeserializeObject<VacuglideUploadResponse>(responseContent);
                    
                    _logger.LogInformation($"Upload successful for {Name}. Token: {status?.token}. Device is now ready.");
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

    // Vacuglide API Response Records
    public record VacuglideDeviceInfoResponse(string deviceType, string firmwareVersion, string hardwareVersion);
    public record VacuglideDeviceStateResponse(string operationalCode, int syncScriptCurrentTime, int syncScriptOffsetTime, string syncScriptToken, bool syncScriptLoop);
    public record VacuSyncScriptStartRequest(long startTimeMs);
    public record VacuSyncScriptOffsetRequest(int offsetMs);
    public record VacuSyncScriptLoopRequest(bool loop);
    public record VacuTargetSpeedRequest(int speed);
    public record VacuLocalScriptRequest(int scriptIndex);
    public record VacuglideUploadResponse(string token);
    public record VacuValveStateRequest(bool state);
}
