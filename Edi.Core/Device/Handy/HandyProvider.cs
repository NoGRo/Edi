using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Index;

using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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


        private Timer timerReconnect = new Timer(40000);

        
        public HandyConfig Config { get; set; }
        private List<String> Keys = new List<string>();
        private Dictionary<string, HandyDevice> devices = new Dictionary<string, HandyDevice>();
        private DeviceManager deviceManager;
        private IndexRepository repository { get; set; }

        public async Task Init()
        {
            if (string.IsNullOrEmpty(Config.Key))
                return;

            await Task.Delay(500);
            RemoveAll();

            Keys = Config.Key.Split(',')
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(x => x.Trim())
                                .ToList();
            timerReconnect.Stop();
            

            

            ConnectAll();

            timerReconnect.Start();
        }

        private void ConnectAll()
        {
            Keys.AsParallel().ForAll(async key =>
            {
                await Connect(key);
            });
        }

        private async Task Connect( string  Key)
        {

            HttpClient Client = devices.ContainsKey(Key) ? devices[Key].Client : NewClient(Key);
                
            HttpResponseMessage resp = null;

            try
            {
                resp = await Client.GetAsync("connected");
            }
            catch { }

            if (resp?.StatusCode != System.Net.HttpStatusCode.OK)
            {
                
                Remove(Key);
                return;
            }
            var status = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
            if (!status.connected)
            {
                Remove(Key);
                return;
            }

            
            if (devices.ContainsKey(Key))
                return;

            _ = await Client.PutAsync("mode", new StringContent(JsonConvert.SerializeObject(new ModeRequest(1)), Encoding.UTF8, "application/json"));

            var handyDevice = new HandyDevice(Client, repository);
            lock (devices) {
                devices.Add(Key, handyDevice);
                deviceManager.LoadDevice(handyDevice);
            }

            await handyDevice.updateServerTime();

        }

        private void RemoveAll()
        {
            foreach (var key in Keys)
                Remove(key);
        }
        private void Remove(string Key)
        {
            if (devices.ContainsKey(Key))
            {
                deviceManager.UnloadDevice(devices[Key]);
                devices.Remove(Key);
            }
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
            ConnectAll();
        }
    }
}
