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

namespace Edi.Core.Device.Handy
{
    internal class HandyDevice : IDevice, IEqualityComparer<HandyDevice>
    {

        public string Key { get; set; }

        public string Name => $"The Handy [{Key}]";
        private long CurrentTime { get; set; }
        
        private static long timeSyncAvrageOffset;
        private static long timeSyncInitialOffset;
        private HttpClient Client ;
        private IndexRepository repository { get; set; }
        private Timer timerGalleryEnd = new Timer();

        private IndexGallery currentGallery;
        private string selectedVariant;
        public string SelectedVariant { get => selectedVariant ??  repository.Config.DefaulVariant; set => selectedVariant = value; }

        public IEnumerable<string> Variants => repository.GetVariants();

        public HandyDevice(HttpClient Client, IndexRepository repository)
        {
            timerGalleryEnd.Elapsed += TimerGalleryEnd_Elapsed;
            this.Client = Client;
            this.repository = repository;
            
        }



        private async Task Seek(long timeMs)
        {
            var req = new SyncPlayRequest(ServerTime, timeMs);
            var resp = await Client.PutAsync("hssp/play", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"));
        }


        private long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeSyncInitialOffset + timeSyncAvrageOffset;

        private long ResumeAt { get; set; }


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
            var gallery = repository.Get(name, selectedVariant);
            if (gallery == null)
            {
                return ;
            }
            currentGallery = gallery;

            timerGalleryEnd.Interval = gallery.Duration - seek;
            timerGalleryEnd.Start();
            await Seek(gallery.StartTime + seek);
        }
        private async void TimerGalleryEnd_Elapsed(object? sender, ElapsedEventArgs e)
        {
            timerGalleryEnd.Stop();
            if (currentGallery.Loop)
                await PlayGallery(currentGallery.Name);
            else
                await Pause();

        }

        public async Task Pause()
        {
            timerGalleryEnd.Stop();
            ResumeAt = CurrentTime;
            await Client.PutAsync("hssp/stop",null);
        }

        public async Task Resume()
        {
            await Seek(ResumeAt);
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
}
