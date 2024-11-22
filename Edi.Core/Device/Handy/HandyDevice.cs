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
using System.Reflection;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    internal class HandyDevice : DeviceBase<IndexRepository,IndexGallery> 
    {

        public string Key { get; set; }

        
        private static long timeSyncAvrageOffset;
        public HttpClient Client = null;
        private bool isStopCalled;
        
        internal override void SetVariant()
        {
            upload();
        }
        private string CurrentBundle = "default";
        public HandyDevice(HttpClient Client, IndexRepository repository): base(repository) 
        {
            Key = Client.DefaultRequestHeaders.GetValues("X-Connection-Key").First();
            //make unique nane 
            Name = $"The Handy [{Key}]";

            IsReady = false;
            this.Client = Client;
        }

    
        internal override async Task applyRange()
        {
            Debug.WriteLine($"Handy: {Key} Slide {Min}-{Max}");
            var request = new SlideRequest(Min, Max);
            await Client.PutAsync("slide", new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"));
        }

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
            await Seek();
        }

        private async Task Seek()
        {
            if (!IsReady)
                return;
            
            
            try
            {
                isStopCalled = false;
                var req = new SyncPlayRequest(ServerTime, currentGallery.StartTime + CurrentTime);
                Debug.WriteLine($"Handy: [{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}] {req.estimatedServerTime} {Key} PLay [{req.startTime}] ({currentGallery?.Name ?? ""}))");
                var token = playCancelTokenSource.Token;
                await Client.PutAsync("hssp/play", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"), token);
                // second call after warmp up connection  
                await Task.Delay(1500, token);
                if (currentGallery is null || token.IsCancellationRequested || isStopCalled)
                    return;

                req = new SyncPlayRequest(ServerTime, currentGallery.StartTime + CurrentTime);
                Debug.WriteLine($"Handy: [{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}] {req.estimatedServerTime} {Key} PLay AfterWarmup [{req.startTime}] ({currentGallery?.Name ?? ""}))");
                await Client.PutAsync("hssp/play", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"), token);

            }

            catch (TaskCanceledException ex)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Handy: {Key} Error: {ex.Message}");
            }
        }

        public override async Task StopGallery()
        {
            isStopCalled = true;
            if (!IsReady)
            {
                return;
            }
            Debug.WriteLine($"Handy: {Key} Stop");
            try
            {
                await Client.PutAsync("hssp/stop", null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Handy: {Key} Error: {ex.Message}");
            }
        }



        private Task uploadTask { get; set; }
        private CancellationTokenSource uploadCancellationTokenSource;

        private async void upload(string bundle = null, bool delay = true)
        {

            uploadCancellationTokenSource?.Cancel(true);
            await Task.Delay(50);
            uploadCancellationTokenSource = new CancellationTokenSource();
            
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
                        return;
                    }

                }
                try
                {
                    Task pause =  Client.PutAsync("hssp/stop",null, uploadCancellationTokenSource.Token);
                    IsReady = false;

                    CurrentBundle = bundle ?? CurrentBundle;

                    var blob = await uploadBlob(repository.GetBundle($"{CurrentBundle}.{selectedVariant}", "csv"));
                    
                    await pause;

                    
                    var resp = await Client.PutAsync("hssp/setup", new StringContent(JsonConvert.SerializeObject(new SyncUpload(blob)), Encoding.UTF8, "application/json"), uploadCancellationTokenSource.Token);
                    var result = await resp.Content.ReadAsStringAsync();

                    if (result.Contains("timeout"))
                    {
                        //when the divice ends, re adquiere seek command
                    }
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
        private async Task<string> uploadBlob(FileInfo file)
        {

            using (var blobClient = new HttpClient())
            {
                blobClient.Timeout = TimeSpan.FromMinutes(3);
                var request = new HttpRequestMessage(HttpMethod.Post, "https://www.handyfeeling.com/api/sync/upload");

                var content = new MultipartFormDataContent
                {
                    { new StreamContent(file.OpenRead()), "syncFile", "Edi.csv" }
                };

                request.Content = content;

                var resp = await blobClient.SendAsync(request, uploadCancellationTokenSource.Token);

                return JsonConvert.DeserializeObject<SyncUpload>(await resp.Content.ReadAsStringAsync(uploadCancellationTokenSource.Token)).url;
            }
        }

        internal async Task updateServerTime()
        {
            timeSyncAvrageOffset = await ServerTimeSync.SyncServerTimeAsync();
            Debug.WriteLine($"Handy: [Offset {timeSyncAvrageOffset}]");
        }

      
        private long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +  timeSyncAvrageOffset;


    }
public static class ServerTimeSync
    {
        private static double _estimatedAverageOffset = 0;
        private static DateTime _estimatedDatetime;
        private static int offsetRefreshMinutes = 10;


        
        
        public static async Task<long> SyncServerTimeAsync()
        {
            var client = new HttpClient();

            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            //_ =  await GetServerTimeAsync(); //warmup
            var syncTries = 30;
            var offsetAggregated = new List<double>();
            for (int i = 0; i < syncTries; i++)
            {
                var tStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var response = await client.GetAsync("https://www.handyfeeling.com/api/handy/v2/servertime");
                var tEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var tServer = data.RootElement.GetProperty("serverTime").GetInt64();
                
                var tRtd = tEnd - tStart;
                var tOffset = (tServer + (tRtd / 2.0)) - tEnd;
                offsetAggregated.Add(tOffset);
            }
            offsetAggregated.Sort();
            var trimmedOffsets = offsetAggregated.Skip(4).Take(offsetAggregated.Count - 8).ToList();

            // Calcular el promedio de los offsets sin los extremos
            _estimatedAverageOffset = Math.Round(trimmedOffsets.Average());
            return Convert.ToInt64( _estimatedAverageOffset);
        }

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
