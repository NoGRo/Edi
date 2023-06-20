using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Index;

using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.Handy
{
    public class HandyProvider : IDeviceProvider
    {
        public HandyProvider(IndexRepository repository, ConfigurationManager config, DeviceManager deviceManager)
        {
            this.Config = config.Get<HandyConfig>(); 
            this.repository = repository;
            this.deviceManager = deviceManager;
            timerReconnect.Elapsed += TimerReconnect_Elapsed;
        }
        private HandyDevice handyDevice;


        private Timer timerReconnect = new Timer(20000);
        private bool Connected = false;

        private HttpClient Client;
        public HandyConfig Config { get; set; }
        private DeviceManager deviceManager;
        private IndexRepository repository { get; set; }

        public async Task Init()
        {
            if (string.IsNullOrEmpty(Config.Key))
                return;


    
            timerReconnect.Stop();
            timerReconnect.Start();

            Client = new HttpClient() { BaseAddress = new Uri("https://www.handyfeeling.com/api/handy/v2/") };
            Client.DefaultRequestHeaders.Remove("X-Connection-Key");
            Client.DefaultRequestHeaders.Add("X-Connection-Key", Config.Key);
            HttpResponseMessage resp = null;

            try
            {
                resp = await Client.GetAsync("connected");
            }
            catch { }

            if (resp?.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return;
            }

            
            var status = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
            if (!status.connected)
            {
                return;
            }


            Connected = true;



            resp = await Client.PutAsync("mode", new StringContent(JsonConvert.SerializeObject(new ModeRequest(1)), Encoding.UTF8, "application/json"));


            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                //OnStatusChange("Server fail Response");
                return;
            }

            if (handyDevice != null)
            {
                deviceManager.UnloadDevice(handyDevice);
            }
            handyDevice = new HandyDevice(Client, repository);
            handyDevice.Key = Config.Key;
            deviceManager.LoadDevice(handyDevice);


            await handyDevice.updateServerTime();
            
        }
        private async void TimerReconnect_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!Connected)
            {
                await Init();
                return;
            }
            
            var resp = await Client.GetAsync("connected");
            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Connected = false;
                deviceManager.UnloadDevice(handyDevice);
                handyDevice = null;
                await Init();
            }
            
        }
        public async Task UploadHandy(string scriptUrl)
        {
            var resp = await Client.PutAsync("hssp/setup", new StringContent(JsonConvert.SerializeObject(new SyncUpload(scriptUrl)), Encoding.UTF8, "application/json"));
        }
        private async Task<string> uploadBlob(FileInfo file)
        {

            using (var blobClient = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://www.handyfeeling.com/api/sync/upload");

                var content = new MultipartFormDataContent
                {
                    { new StreamContent(file.OpenRead()), "syncFile", "Edi.csv" }
                };

                request.Content = content;

                var resp = await blobClient.SendAsync(request);

                return JsonConvert.DeserializeObject<SyncUpload>(await resp.Content.ReadAsStringAsync()).url;
            }
        }

    }
}
