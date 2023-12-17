
using Buttplug.Client;
using Buttplug.Client.Connectors.WebsocketConnector;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.OSR
{
    public class OSRProvider : IDeviceProvider
    {
        public readonly OSRConfig Config;
        public event EventHandler<string> StatusChange;

        private OSRDevice? device;
        private DeviceManager deviceManager;
        private FunscriptRepository repository;
        private Timer timerReconnect = new Timer(20000);

        public OSRProvider(FunscriptRepository repository, ConfigurationManager config, DeviceManager deviceManager)
        {
            Config = config.Get<OSRConfig>();

            this.deviceManager = deviceManager;
            this.repository = repository;
        }

        public async Task Init()
        {
            timerReconnect.Elapsed += timerReconnectevent;
            timerReconnect.Start();
            await Connect();
        }

        private void OnStatusChange(string e)
        {
            StatusChange?.Invoke(null, e);
        }

        private async Task Connect()
        {
            timerReconnect.Enabled = false;
            try
            {
                var port = new SerialPort(Config.COMPort, 115200, Parity.None, 8, StopBits.One);
                port.Open();

                device = new OSRDevice(port, repository, Config);
                _ = device.ReturnToHome();

                deviceManager.LoadDevice(device);
            }
            catch (Exception e)
            {
                timerReconnect.Enabled = true;
                OnStatusChange("Error");
            }
        }

        private void timerReconnectevent(object sender, ElapsedEventArgs e)
        {
            if (device == null)
                Connect();
            else
                timerReconnect.Enabled = false;
        }
    }
}
