using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Index;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy
{
    public class HandyProvider : IDeviceProvider
    {
        public HandyProvider(IndexRepository repository, IConfiguration config, IDeviceManager deviceLoad)
        {
            this.Config = new HandyConfig();
            config.GetSection(HandyConfig.Section).Bind(this.Config);

            this.repository = repository;
            this.deviceLoad = deviceLoad;
        }

        private HttpClient Client = new HttpClient() { BaseAddress = new Uri("https://www.handyfeeling.com/api/handy/v2/") };
        public HandyConfig Config { get; set; }
        private IDeviceManager deviceLoad;
        public IndexRepository repository { get; set; }

        public async Task Init()
        {
            repository.Init();
            if (string.IsNullOrEmpty(Config.Key))
                return;

            
            Client.DefaultRequestHeaders.Remove("X-Connection-Key");
            Client.DefaultRequestHeaders.Add("X-Connection-Key", Config.Key);

            var resp = await Client.GetAsync("connected");
            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return;
            }

            var status = JsonConvert.DeserializeObject<ConnectedResponse>(await resp.Content.ReadAsStringAsync());

            if (!status.connected)
            {
                //OnStatusChange("Handy is not Conected");
                return;
            }


            //OnStatusChange("Uploading & Sync");
            var blob = uploadBlob(repository.Assets["csv"]);

            resp = await Client.PutAsync("mode", new StringContent(JsonConvert.SerializeObject(new ModeRequest(1)), Encoding.UTF8, "application/json"));


            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                //OnStatusChange("Server fail Response");
                return;
            }


            var upload = UploadHandy(await blob);
            var handyDevice = new HandyDevice(Client, repository);

            await handyDevice.updateServerTime();
            //OnStatusChange("Uploading");
            await upload;
            
            deviceLoad.LoadDevice(handyDevice);
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
