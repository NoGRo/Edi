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

        
        public HandyConfig Config { get; set; }
        private List<String> Keys = new List<string>();
        private Dictionary<string, HandyDevice> devices = new Dictionary<string, HandyDevice>();
        private DeviceManager deviceManager;
        private IndexRepository repository { get; set; }

        public async Task Init()
        {
            if (string.IsNullOrEmpty(Config.Key))
                return;

            
            Keys = Config.Key.Split(',')
                                .Where(x=> !string.IsNullOrWhiteSpace(x))
                                .Select(x => x.Trim())  
                                .ToList();
            timerReconnect.Stop();
            var tasks = new List<Task>();

            Keys.AsParallel().ForAll(async key =>
            {
                
                await Connect(key);
            });
            
            timerReconnect.Start();
        }
        private async Task Connect(string Key)
        {
            if (devices.ContainsKey(Key))
                return;

            HttpClient Client = NewClient(Key);
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

            resp = await Client.PutAsync("mode", new StringContent(JsonConvert.SerializeObject(new ModeRequest(1)), Encoding.UTF8, "application/json"));


            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                //OnStatusChange("Server fail Response");
                return;
            }
            var handyDevice = new HandyDevice(Client, repository);
            handyDevice.Key = Config.Key;



            devices.Add(Key, handyDevice);
            deviceManager.LoadDevice(handyDevice);
            await handyDevice.updateServerTime();

        }

        public static HttpClient NewClient(string Key)
        {
            var Client = new HttpClient() { BaseAddress = new Uri("https://www.handyfeeling.com/api/handy/v2/") };
            Client.DefaultRequestHeaders.Remove("X-Connection-Key");
            Client.DefaultRequestHeaders.Add("X-Connection-Key", Key);
            return Client;
        }

        private async void TimerReconnect_Elapsed(object? sender, ElapsedEventArgs e)
        {
            

            foreach (var Key in Keys)
            {

                HttpResponseMessage resp = null;
                try
                {
                    resp = await devices[Key].Client.GetAsync("connected");
                } catch (Exception ex)
                {

                }
                if (resp != null && resp.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    if (devices.ContainsKey(Key))
                    {
                        deviceManager.UnloadDevice(devices[Key]);
                        devices.Remove(Key);
                    }
                    await Connect(Key);
                }
                
            }

        }
       
    }
}
