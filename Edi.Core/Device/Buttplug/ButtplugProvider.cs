using Buttplug.Client;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;

namespace Edi.Core.Device.Buttplug
{
    public class ButtplugProvider : IDeviceProvider
    {
        private readonly ILogger _logger;

        public ButtplugProvider(FunscriptRepository repository, ConfigurationManager config, DeviceCollector deviceCollector, ILogger<ButtplugProvider> logger)
        {
            _logger = logger;
            Config = config.Get<ButtplugConfig>();
            this.repository = repository;
            DeviceCollector = deviceCollector;
            Controller = new ButtplugController(Config, deviceCollector, _logger);

            _logger.LogInformation("ButtplugProvider initialized with repository and device manager.");
        }

        public readonly ButtplugConfig Config;
        private Timer timerReconnect = new Timer(20000);
        private List<ButtplugDevice> devices = new List<ButtplugDevice>();
        private DeviceCollector DeviceCollector;
        public ButtplugClient client { get; set; }
        public ButtplugController Controller { get; set; }
        private FunscriptRepository repository { get; }

        public async Task Init()
        {
            _logger.LogInformation("Initializing ButtplugProvider...");
            timerReconnect.Elapsed += timerReconnectevent;
            timerReconnect.Start();

            await Connect();
            _logger.LogInformation("ButtplugProvider initialization complete.");
        }

        public event EventHandler<string> StatusChange;
        private void OnStatusChange(string e)
        {
            StatusChange?.Invoke(null, e);
        }

        public async Task Connect()
        {
            _logger.LogInformation("Attempting to connect to Buttplug client.");
            timerReconnect.Enabled = false;

            if (client != null)
            {
                if (client.Connected)
                {
                    await client.DisconnectAsync();
                    _logger.LogInformation("Disconnected existing client connection.");
                }

                client.Dispose();
                client = null;
                RemoveAllDevices();
                _logger.LogInformation("Existing client disposed and devices removed.");
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
                    var connector = new ButtplugWebsocketConnector(new Uri(Config.Url));
                    await client.ConnectAsync(connector);
                    if (client.Connected)
                    {
                        await client.StartScanningAsync();
                        _logger.LogInformation("Client connected and scanning started.");
                    }
                }
            }
            catch (ButtplugClientConnectorException ex)
            {
                _logger.LogError($"Failed to connect to client: {ex.Message}");
                timerReconnect.Enabled = true;
            }
        }

        private void RemoveAllDevices()
        {
            _logger.LogInformation("Removing all devices.");
            foreach (var devicerm in devices)
            {
                DeviceCollector.UnloadDevice(devicerm);
            }
            devices.Clear();
            _logger.LogInformation("All devices removed.");
        }

        private void AddDeviceOn(ButtplugClientDevice Device)
        {
            _logger.LogInformation($"Adding device: {Device.Name}");
            var newdevices = new List<ButtplugDevice>();

            // OSR6: Detect if it's an OSR device and create a different Device class if necessary.

            for (uint i = 0; i < Device.LinearAttributes.Count; i++)
            {
                newdevices.Add(new ButtplugDevice(Device, ActuatorType.Position, i, repository, Config, _logger));
            }

            for (uint i = 0; i < Device.VibrateAttributes.Count; i++)
            {
                newdevices.Add(new ButtplugDevice(Device, ActuatorType.Vibrate, i, repository, Config, _logger));
            }
            for (uint i = 0; i < Device.RotateAttributes.Count; i++)
            {
                newdevices.Add(new ButtplugDevice(Device, ActuatorType.Rotate, i, repository, Config, _logger));
            }
            for (uint i = 0; i < Device.OscillateAttributes.Count; i++)
            {
                newdevices.Add(new ButtplugDevice(Device, ActuatorType.Oscillate, i, repository, Config, _logger));
            }

            devices.AddRange(newdevices);

            foreach (var device in newdevices)
            {
                DeviceCollector.LoadDevice(device);
                _logger.LogInformation($"Device loaded: {device.Name}");
            }
        }

        private void RemoveDeviceOn(ButtplugClientDevice Device)
        {
            _logger.LogInformation($"Removing device: {Device.Name}");
            var rmdevices = new List<ButtplugDevice>();

            for (uint i = 0; i < Device.LinearAttributes.Count; i++)
            {
                rmdevices.Add(devices.FirstOrDefault(x => x.Device == Device && x.Actuator == ActuatorType.Position && x.DeviceChannel == i));
            }

            for (uint i = 0; i < Device.VibrateAttributes.Count; i++)
            {
                rmdevices.Add(devices.FirstOrDefault(x => x.Device == Device && x.Actuator == ActuatorType.Vibrate && x.DeviceChannel == i));
            }
            for (uint i = 0; i < Device.RotateAttributes.Count; i++)
            {
                rmdevices.Add(devices.FirstOrDefault(x => x.Device == Device && x.Actuator == ActuatorType.Rotate && x.DeviceChannel == i));
            }
            for (uint i = 0; i < Device.OscillateAttributes.Count; i++)
            {
                rmdevices.Add(devices.FirstOrDefault(x => x.Device == Device && x.Actuator == ActuatorType.Oscillate && x.DeviceChannel == i));
            }

            foreach (var devicerm in rmdevices.Where(x => x != null))
            {
                devices.Remove(devicerm);
                DeviceCollector.UnloadDevice(devicerm);
                _logger.LogInformation($"Device removed: {devicerm.Name}");
            }
        }

        private void Client_ServerDisconnect(object sender, EventArgs e)
        {
            timerReconnect.Enabled = true;
            OnStatusChange("Disconnect");
            _logger.LogWarning("Server disconnected. Reconnection timer started.");
        }

        private void Client_DeviceRemoved(object sender, DeviceRemovedEventArgs e)
        {
            _logger.LogInformation($"Device removed: {e.Device.Name}");
            RemoveDeviceOn(e.Device);
        }

        private void Client_ErrorReceived(object sender, ButtplugExceptionEventArgs e)
        {
            _logger.LogError("Error received from client.");
            foreach (var device in (sender as ButtplugClient).Devices)
            {
                RemoveDeviceOn(device);
            }
            OnStatusChange("Error");
        }

        private void Client_ScanningFinished(object sender, EventArgs e)
        {
            OnStatusChange("Scanning Finished");
            _logger.LogInformation("Device scanning finished.");
        }

        private void Client_DeviceAdded(object sender, DeviceAddedEventArgs e)
        {
            _logger.LogInformation($"Device added: {e.Device.Name}");
            AddDeviceOn(e.Device);
        }

        private void timerReconnectevent(object sender, ElapsedEventArgs e)
        {
            if (!client.Connected)
            {
                _logger.LogInformation("Reconnection timer triggered. Attempting to reconnect.");
                _ = Connect();
            }
            else
            {
                timerReconnect.Enabled = false;
            }
        }
    }
}
