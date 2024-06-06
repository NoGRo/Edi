using Edi.Core.Device.AutoBlow;
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
using Edi.Core.Device.Handy;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.AutoBlow
{
    public class AutoBlowProvider : IDeviceProvider
    {
        public AutoBlowProvider(IndexRepository repository, ConfigurationManager config, DeviceManager deviceManager)
        {
            this.Config = config.Get<HandyConfig>(); 
            this.repository = repository;
            this.deviceManager = deviceManager;

            timerReconnect.Elapsed += TimerReconnect_Elapsed;
        }
        private AutoBlowDevice device;


        private Timer timerReconnect = new Timer(40000);

        
        public HandyConfig Config { get; set; }

        private List<String> Keys = new List<string>();
        private Dictionary<string, AutoBlowDevice> devices = new Dictionary<string, AutoBlowDevice>();
        private DeviceManager deviceManager;
        private IndexRepository repository { get; set; }

        public async Task Init()
        {
            if (string.IsNullOrEmpty(Config.Key))
                return;

            await Task.Delay(500);
            await RemoveAll();

            Keys = Config.Key.Split(',')
                                .Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length == 12)
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
                
                await Remove(Key);
                return;
            }
            var connected = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());
            if (!connected.connected)
            {
                await Remove(Key);
                return;
            }

            
            if (devices.ContainsKey(Key))
                return;
            Client.Dispose();
            
            Client = NewClient(Key, connected.cluster);

            resp = await Client.GetAsync("state");

            var status = JsonConvert.DeserializeObject<Status>(await resp.Content.ReadAsStringAsync());

            var device = new AutoBlowDevice(Client, repository);
            lock (devices) {
                if (devices.ContainsKey(Key))
                    return;
                devices.Add(Key, device);
                deviceManager.LoadDevice(device);
            }

            await device.updateServerTime();

        }

        private async Task RemoveAll()
        {
            foreach (var key in Keys)
                await Remove(key);
        }
        private async Task Remove(string Key)
        {
            if (devices.ContainsKey(Key))
            {
                await deviceManager.UnloadDevice(devices[Key]);
                devices.Remove(Key);
            }
        }

        public static HttpClient NewClient(string Key, string Cluster = null)
        {
            Cluster = Cluster ?? "us-east-1.autoblowapi.com";
            var Client = new HttpClient() { BaseAddress = new Uri($"https://{Cluster}/autoblow/") };
            Client.DefaultRequestHeaders.Remove("x-device-token");
            Client.DefaultRequestHeaders.Add("x-device-token", Key);
            return Client;
        }

        private async void TimerReconnect_Elapsed(object? sender, ElapsedEventArgs e)
        {
            ConnectAll();
        }
    }
}
