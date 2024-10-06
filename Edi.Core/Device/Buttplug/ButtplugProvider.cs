
using Buttplug.Client;
using Buttplug.Client.Connectors.WebsocketConnector;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
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
        public ButtplugProvider(FunscriptRepository repository, ConfigurationManager config, DeviceManager deviceManager)
        {

            this.Config = config.Get<ButtplugConfig>();

            this.repository = repository;
            DeviceManager = deviceManager;
            Controller = new ButtplugController(Config, deviceManager);
        }

        public readonly ButtplugConfig Config;
        private Timer timerReconnect = new Timer(20000);
        private List<ButtplugDevice> devices = new List<ButtplugDevice>();
        private DeviceManager DeviceManager;
        public ButtplugClient client { get; set; }
        public ButtplugController Controller { get; set; }
        private FunscriptRepository repository { get; }
        public async Task Init()
        {
            timerReconnect.Elapsed += timerReconnectevent;
            timerReconnect.Start();
            //RemoveAllDevices();
            await Connect();
        }

        public event EventHandler<string> StatusChange;
        private void OnStatusChange(string e)
        {
            StatusChange?.Invoke(null, e);
        }

        public async Task Connect()
        {

            timerReconnect.Enabled = false;
            if (client != null)
            {
                if (client.Connected)
                    await client.DisconnectAsync();

                client.Dispose();
                client = null;
                await RemoveAllDevices();

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
                timerReconnect.Enabled = true;
                return;
            }

        }

        private async Task RemoveAllDevices()
        {
            foreach (ButtplugDevice devicerm in devices)
            {
               await DeviceManager.UnloadDevice(devicerm);
            }
            devices.Clear();
        }

        private void AddDeviceOn(ButtplugClientDevice Device)
        {
            var newdevices = new List<ButtplugDevice>();
            

            //OSR6 detect if is Osr and create another Device class ?


            for (uint i = 0; i < Device.LinearAttributes.Count; i++)
            {
                newdevices.Add(new ButtplugDevice(Device,ActuatorType.Position, i, repository,Config));
            }

            for (uint i = 0; i < Device.VibrateAttributes.Count; i++)
            {
                newdevices.Add(new ButtplugDevice(Device, ActuatorType.Vibrate, i, repository,Config));
            }
            for (uint i = 0; i < Device.RotateAttributes.Count; i++)
            {
                newdevices.Add(new ButtplugDevice(Device, ActuatorType.Rotate, i, repository,Config));
            }
            for (uint i = 0; i < Device.OscillateAttributes.Count; i++)
            {
                newdevices.Add(new ButtplugDevice(Device, ActuatorType.Oscillate, i, repository,Config));
            }

            devices.AddRange(newdevices);

            foreach (var device in newdevices) { 
                DeviceManager.LoadDevice(device);
            }
        }
        private void RemoveDeviceOn(ButtplugClientDevice Device)
        {
            var rmdevices =  new List<ButtplugDevice>();

            for (uint i = 0; i < Device.LinearAttributes.Count; i++)
            {
                rmdevices.Add(devices.FirstOrDefault(x => x.Device == Device && x.Actuator == ActuatorType.Position && x.Channel == i));
            }

            for (uint i = 0; i < Device.VibrateAttributes.Count; i++)
            {
                rmdevices.Add(devices.FirstOrDefault(x => x.Device == Device && x.Actuator == ActuatorType.Vibrate && x.Channel == i));
               
            }
            for (uint i = 0; i < Device.RotateAttributes.Count; i++)
            {
                rmdevices.Add(devices.FirstOrDefault(x => x.Device == Device && x.Actuator == ActuatorType.Rotate && x.Channel == i));
            }
            for (uint i = 0; i < Device.OscillateAttributes.Count; i++)
            {
                rmdevices.Add(devices.FirstOrDefault(x => x.Device == Device && x.Actuator == ActuatorType.Oscillate && x.Channel == i));
            }

            foreach (ButtplugDevice devicerm in rmdevices.Where(x=> x is not null))
            {
                devices.Remove(devicerm);
                DeviceManager.UnloadDevice(devicerm);
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
            foreach (var device in (sender as ButtplugClient).Devices)
            {
                RemoveDeviceOn(device);
            }
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
