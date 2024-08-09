using System.Text.Json;
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
using System.Threading;
using PropertyChanged;
using System.Timers;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;

namespace Edi.Core.Device.AutoBlow
{
    [AddINotifyPropertyChangedInterface]
    internal class AutoBlowDevice : DeviceBase<IndexRepository,IndexGallery>
    {

        public string Key { get; set; }

        
        private static long timeSyncAvrageOffset;
        private static long timeSyncInitialOffset;
        public HttpClient Client = null;


        internal override void SetVariant()
        {
            upload();
        }
        private string CurrentBundle = "default";
        public AutoBlowDevice(HttpClient Client, IndexRepository repository): base(repository) 
        {
            Key = Client.DefaultRequestHeaders.GetValues("x-device-token").First();
            //make unique nane 
            Name = $"AutoBlow [{Key}]";

            IsReady = false;
            this.Client = Client;
        }

    
        //internal override async Task applyRange()
        //{
        //    Debug.WriteLine($"AutoBlow: {Key} Slide {Min}-{Max}");
        //    var request = new SlideRequest(Min, Max);
        //    await Client.PutAsync("Slide", new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"));
        //}

        public override async Task PlayGallery(IndexGallery gallery, long seek = 0)
        {
            if (gallery.Bundle != CurrentBundle)
            {
                gallery = repository.Get(gallery.Name, SelectedVariant, CurrentBundle);//find in current bundle 

                if (gallery.Bundle != CurrentBundle)//not in the current uploaded bundle 
                {
                    upload(gallery.Bundle, false);
                }
            }
            await Seek(gallery.StartTime + seek);
        }

        private async Task Seek(long timeMs)
        {
            if (IsReady)
            {
                Debug.WriteLine($"AutoBlow: [{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}] {ServerTime} {Key} PLay [{timeMs}] ({currentGallery?.Name ?? ""}))");
                try
                {
                    var req = new SyncPlayRequest(timeMs);
                    await Client.PutAsync("sync-script/start", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AutoBlow: {Key} Error: {ex.Message}");
                }
            }


        }
        public override async Task StopGallery()
        {
            if (IsReady)
            {
                Debug.WriteLine($"AutoBlow: {Key} Stop");
                try
                {
                    await Client.PutAsync("sync-script/stop", null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AutoBlow: {Key} Error: {ex.Message}");
                }
            }
        }



        private Task uploadTask { get; set; }
        private CancellationTokenSource uploadCancellationTokenSource;

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
                        await Task.Delay(3000, uploadCancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }

                }
                try
                {
                    await Client.PutAsync("sync-script/stop", null, uploadCancellationTokenSource.Token);
                    IsReady = false;

                    CurrentBundle = bundle ?? CurrentBundle;


                    var file = repository.GetBundle($"{CurrentBundle}.{selectedVariant}", "csv");
                    var resp  = await Client.PutAsync("sync-script/upload-csv",
                        new MultipartFormDataContent { { new StreamContent(file.OpenRead()), "file", $"EdiCurrentBundle{selectedVariant}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds}.csv".ToLower() } });
                    

                    if(!resp.IsSuccessStatusCode)
                        return;

                    var status = JsonConvert.DeserializeObject<Status>(await resp.Content.ReadAsStringAsync());


                    IsReady = true;
                    Resume();
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex) {
                    return;
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
