
using Buttplug.Client;
using Buttplug.Client.Connectors.WebsocketConnector;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
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
        public ButtplugProvider(FunscriptRepository repository, IConfiguration config, IDeviceManager deviceManager)
        {
            
            this.Config = new ButtplugConfig();

            config.GetSection("Buttplug").Bind(this.Config);

            this.repository = repository;
            DeviceManager = deviceManager;
        }

        public readonly ButtplugConfig Config;
        private Timer timerReconnect = new Timer(20000);

        private IDeviceManager DeviceManager;
        public ButtplugClient client { get; set; }
        private FunscriptRepository repository { get; }
        public async Task Init()
        {
            timerReconnect.Elapsed += timerReconnectevent;
            timerReconnect.Start(); 
            await Connect();
        }

        public event EventHandler<string> StatusChange;
        private void OnStatusChange(string e)
        {
            StatusChange?.Invoke(null, e);
        }

        public async Task Connect()
        {


            if (client != null)
            {
                if (client.Connected)
                    await client.DisconnectAsync();

                client.Dispose();
                client = null;
            }
            client = new ButtplugClient("Edi");

            client.DeviceAdded += Client_DeviceAdded;
            client.DeviceRemoved += Client_DeviceRemoved;
            client.ErrorReceived += Client_ErrorReceived;
            client.ServerDisconnect += Client_ServerDisconnect;
            

            try
            {
                if (!string.IsNullOrEmpty(Config.Url))
                {
                    var conector = new ButtplugWebsocketConnector(new Uri(Config.Url));
                    await client.ConnectAsync(conector);
                    if (client.Connected)
                        await client.StartScanningAsync();
                }
//                else
  //                  await client.ConnectAsync(new buttplugE);
            }
            catch (ButtplugClientConnectorException ex)
            {
               
                return;
            }

        }
        private void AddDeviceOn(ButtplugClientDevice Device)
        {
            for (uint i = 0; i < Device.LinearAttributes.Count; i++)
            {
                DeviceManager.LoadDevice(new ButtplugDevice(Device,ActuatorType.Position, i, repository));
            }

            for (uint i = 0; i < Device.VibrateAttributes.Count; i++)
            {
                DeviceManager.LoadDevice(new ButtplugDevice(Device, ActuatorType.Vibrate, i, repository));
            }
            for (uint i = 0; i < Device.RotateAttributes.Count; i++)
            {
                DeviceManager.LoadDevice(new ButtplugDevice(Device, ActuatorType.Rotate, i, repository));
            }
            for (uint i = 0; i < Device.OscillateAttributes.Count; i++)
            {
                DeviceManager.LoadDevice(new ButtplugDevice(Device, ActuatorType.Oscillate, i, repository));
            }


        }
        private void RemoveDeviceOn(ButtplugClientDevice Device)
        {
            for (uint i = 0; i < Device.LinearAttributes.Count; i++)
            {
                DeviceManager.UnloadDevice(new ButtplugDevice(Device, ActuatorType.Position, i, repository));
            }

            for (uint i = 0; i < Device.VibrateAttributes.Count; i++)
            {
                DeviceManager.UnloadDevice(new ButtplugDevice(Device, ActuatorType.Vibrate, i, repository));
            }
            for (uint i = 0; i < Device.RotateAttributes.Count; i++)
            {
                DeviceManager.UnloadDevice(new ButtplugDevice(Device, ActuatorType.Rotate, i, repository));
            }
            for (uint i = 0; i < Device.OscillateAttributes.Count; i++)
            {
                DeviceManager.UnloadDevice(new ButtplugDevice(Device, ActuatorType.Oscillate, i, repository));
            }
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
