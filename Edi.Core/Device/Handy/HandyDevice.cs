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

namespace Edi.Core.Device.Handy
{
    internal class HandyDevice :  ISendGallery
    {

        public string Key { get; set; }

        public long CurrentTime { get; set; }
        public bool IsPlaying { get; set; }

        public bool isReady { get; set; }

        
        private static long timeSyncAvrageOffset;
        private static long timeSyncInitialOffset;
        private HttpClient Client ;
        private IGalleryRepository repository { get; set; }
        private Timer timerGalleryEnd = new Timer();

        public HandyDevice(HttpClient Client, IGalleryRepository repository)
        {
            timerGalleryEnd.Elapsed += TimerGalleryEnd_Elapsed;
            this.Client = Client;
            this.repository = repository;
        }

        public void Pausa()
        {
            //TODO SendPause
        }

        public async Task Play(long? timeMs)
        {
            await Seek(timeMs ?? 0);

        }

        public async Task Seek(long timeMs)
        {
            var req = new SyncPlayRequest(ServerTime, timeMs);
            var resp = await Client.PutAsync("hssp/play", new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json"));
        }

        public async Task Stop()
        {

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

        private GalleryIndex currentGallery;
        public async Task SendGallery(string name, long seek = 0)
        {
            var gallery = repository.Get(name);
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
            if (currentGallery.Repeats)
                await SendGallery(currentGallery.Name);
            else
                Pausa();

        }

    }
    public record ServerTimeResponse(long serverTime);
    public record SyncPlayRequest(long estimatedServerTime, long startTime);
    public record SyncUpload(string url);
    public record ConnectedResponse(bool connected);
    public record ModeRequest(int mode);
}
