
using Buttplug;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.Buttplug
{
    public class ButtplugProvider : IDeviceProvider
    {
        public ButtplugProvider(ILoadDevice deviceLoad, IGalleryRepository repository, IConfiguration config)
        {
            this.DeviceLoad = deviceLoad;
            this.Config = new ButtplugConfig();

            config.GetSection("Buttplug").Bind(this.Config);

            this.repository = repository;
        }

        public readonly ButtplugConfig Config;
        private Timer timerReconnect = new Timer();

        private readonly ILoadDevice DeviceLoad;
        public ButtplugClient client { get; set; }
        private IGalleryRepository repository { get; }
        public async Task Init()
        {
            timerReconnect.Elapsed += timerReconnectevent;
            await Connect();
        }

        public event EventHandler<string> StatusChange;
        private void OnStatusChange(string e)
        {
            StatusChange?.Invoke(null, e);
        }

        public async Task Connect()
        {

            OnStatusChange("Connecting...");

            if (client != null)
            {
                if (client.Connected)
                    await client.DisconnectAsync();

                client.Dispose();
                client = null;
                timerReconnect.Enabled = false;
            }
            client = new ButtplugClient("DJ Sex");

            client.DeviceAdded += Client_DeviceAdded;
            client.DeviceRemoved += Client_DeviceRemoved;
            client.ErrorReceived += Client_ErrorReceived;
            client.ServerDisconnect += Client_ServerDisconnect;
            client.ScanningFinished += Client_ScanningFinished;

            try
            {
                if (!string.IsNullOrEmpty(Config.Url))
                    await client.ConnectAsync(new ButtplugWebsocketConnectorOptions(new Uri(Config.Url)));
                else
                    await client.ConnectAsync(new ButtplugEmbeddedConnectorOptions());
            }
            catch (ButtplugConnectorException ex)
            {
                OnStatusChange("Can't Connect");
                return;
            }

            if (client.Connected)
                OnStatusChange("Connected");

            await client.StartScanningAsync();
            foreach (var buttplugClientDevice in client.Devices)
            {
                AddDeviceOn(buttplugClientDevice);
            }

        }
        private void AddDeviceOn(ButtplugClientDevice Device)
        {
            if (Device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.LinearCmd)
                || Device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.VibrateCmd))
            {

                DeviceLoad.LoadDevice(new ButtplugDevice(Device, repository));
                OnStatusChange($"Device Found [{Device.Name}]");
            }
        }
        private void RemoveDeviceOn(ButtplugClientDevice Device)
        {
            if (Device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.LinearCmd)
                || Device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.VibrateCmd))
            {
                DeviceLoad.UnloadDevice(new ButtplugDevice(Device, repository));
                OnStatusChange($"Device Remove [{Device.Name}]");
            }
            OnStatusChange("Disconnect");
        }

        private void Client_ServerDisconnect(object sender, EventArgs e)
        {
            timerReconnect.Enabled = true;
            OnStatusChange("Disconnect");
        }
        private void Client_DeviceRemoved(object sender, DeviceRemovedEventArgs e)
        {
            RemoveDeviceOn(e.Device);
        }
        private void Client_ErrorReceived(object sender, ButtplugExceptionEventArgs e)
        {
            OnStatusChange("Error");
        }
        private void Client_ScanningFinished(object sender, EventArgs e)
        {
            OnStatusChange($"Scanning Finished");
        }
        private void Client_DeviceAdded(object sender, DeviceAddedEventArgs e)
        {
            AddDeviceOn(e.Device);
        }
        private void timerReconnectevent(object sender, ElapsedEventArgs e)
        {
            if (!client.Connected)
                Connect();
            else
                timerReconnect.Enabled = false;
        }
    }
}
