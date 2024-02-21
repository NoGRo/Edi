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
using System.Timers;
using Timer = System.Timers.Timer;
using System.Diagnostics.CodeAnalysis;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.Definition;
using Edi.Core.Device.Interfaces;
using System.Runtime.CompilerServices;
using System.Threading;
using PropertyChanged;
using System.ComponentModel.DataAnnotations;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    internal class HandyDevice : IDevice, IEqualityComparer<HandyDevice>
    {

        public string Key { get; set; }

        public string Name { get; set; }
        
        private static long timeSyncAvrageOffset;
        private static long timeSyncInitialOffset;
        public HttpClient Client = null;
        private IndexRepository repository { get; set; }
        private Timer timerGalleryEnd = new Timer();
        private int CurrentTime => Convert.ToInt32((DateTime.Now - SyncSend).TotalMilliseconds + SeekTime);
        public DateTime SyncSend { get; private set; } = DateTime.Now;
        private long SeekTime { get; set; }
        private IndexGallery currentGallery;
        public bool IsPause { get; private set; } = true;

        private string selectedVariant;
        public string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                selectedVariant = value;
                upload();
                
            }
        }
        public bool IsReady { get; private set; } = false;
        
        public IEnumerable<string> Variants => repository.GetVariants();

        public HandyDevice(HttpClient Client, IndexRepository repository)
        {
            Key = Client.DefaultRequestHeaders.GetValues("X-Connection-Key").First();
            //timer elapse for loop galleries 
            timerGalleryEnd.Elapsed += TimerGalleryEnd_Elapsed;

            //make unique nane 
            Name = $"The Handy [{Key}]";
            
            this.Client = Client;
            this.repository = repository;

            SelectedVariant = repository.GetVariants().FirstOrDefault(); ;
        }
        
        private Task uploadTask { get; set; }
        private CancellationTokenSource uploadCancellationTokenSource;

        private async Task Seek(long timeMs)
        {

            var req = new SyncPlayRequest(ServerTime, timeMs);
            if (IsReady)
            {
                Debug.WriteLine($"Handy: {Key} PLay [{timeMs}] ({currentGallery?.Name ?? ""}))");
                try
                {
                    await Client.PutAsync("hssp/play", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Handy: {Key} Error: {ex.Message}");   
                }
            }
                

        }
        public async Task Stop()
        {

            currentGallery = null;
            IsPause = true;
            timerGalleryEnd.Stop();
            
            if (IsReady)
            {
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

        }

        private async void upload(string bundle = null, bool delay = true)
        {
            uploadCancellationTokenSource?.Cancel();
            await Task.Delay(100);
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

                    bundle = bundle ?? currentGallery?.Bundle ?? "default";

                    var blob = await uploadBlob(repository.GetBundle($"{bundle}.{selectedVariant}", "csv"), uploadCancellationTokenSource.Token);
                    
                    await pause;

                    
                    var resp = await Client.PutAsync("hssp/setup", new StringContent(JsonConvert.SerializeObject(new SyncUpload(blob)), Encoding.UTF8, "application/json"), uploadCancellationTokenSource.Token);
                    var result = await resp.Content.ReadAsStringAsync();

                    if(result.Contains("timeout") )
                    {
                        //when ends the divice re adquiere seek command
                    }
                    IsReady = true;

                    if (currentGallery != null && !IsPause)
                    {
                        await PlayGallery(currentGallery.Name, CurrentTime);
                    }
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
        private async Task<string> uploadBlob(FileInfo file, CancellationToken  cancellationToken)
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

                var resp = await blobClient.SendAsync(request, cancellationToken);

                return JsonConvert.DeserializeObject<SyncUpload>(await resp.Content.ReadAsStringAsync(cancellationToken)).url;
            }
        }

        private long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeSyncInitialOffset + timeSyncAvrageOffset;
        public async Task updateServerTime()
        {
            var totalCalls = 30;
            var discardTopBotom = 2;
            //warm up
            _ = await getServerOfsset();


            timeSyncInitialOffset = await getServerOfsset();

            var offsets = new List<long>();
            for (int i = 0; i < 30; i++)
            {
                offsets.Add(await getServerOfsset() - timeSyncInitialOffset);
            }
            timeSyncAvrageOffset = Convert.ToInt64(
                                        offsets.OrderBy(x => x)
                                            .Take(totalCalls - discardTopBotom).TakeLast(totalCalls - discardTopBotom * 2) //discard TopBotom Extreme cases
                                            .Average()
                                    );

        }
        private async Task<long> getServerOfsset()
        {
            var sendTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var result = await Client.GetAsync("servertime");
            var receiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var resp = JsonConvert.DeserializeObject<ServerTimeResponse>(await result.Content.ReadAsStringAsync());
            var estimatedServerTimeNow = resp.serverTime + (receiveTime - sendTime) / 2;
            return estimatedServerTimeNow - receiveTime;
        }
        public async Task PlayGallery(string name, long seek = 0)
        {

            timerGalleryEnd.Stop();
            var gallery = repository.Get(name, selectedVariant, currentGallery?.Bundle);
            if (gallery == null)
            {
                return ;
            }
            if(currentGallery?.Bundle != null  && gallery.Bundle != currentGallery.Bundle )
            {
                
                IsReady = false;
                upload(currentGallery.Bundle, false);
            }

            SyncSend = DateTime.Now;
            SeekTime = seek;
            currentGallery = gallery;
            IsPause = false;

            timerGalleryEnd.Interval = gallery.Duration - seek ;
            timerGalleryEnd.Start();

            await Seek(gallery.StartTime + seek);
        }
        private async void TimerGalleryEnd_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Debug.WriteLine($"Handy: {Key} PLay Timer Elapse ({currentGallery?.Name ?? ""}))");
            timerGalleryEnd.Stop();
            if (currentGallery?.Loop == true && !IsPause)
                await PlayGallery(currentGallery.Name);
            else
                await Stop();

        }


        public bool Equals(HandyDevice? x, HandyDevice? y)
            => x.Key == y.Key;

        public int GetHashCode([DisallowNull] HandyDevice obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Key);
            return hash.ToHashCode();
        }
    }
    public record ServerTimeResponse(long serverTime);
    public record SyncPlayRequest(long estimatedServerTime, long startTime);
    public record SyncUpload(string url);
    public record ConnectedResponse(bool connected);
    public record ModeRequest(int mode);
    public record ErrorDetails(int Code, string Name, string Message, bool Connected);
    public record ErrorResponse(ErrorDetails Error);

}
