
using Buttplug.Client;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
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

        private OSRDevice? Device;
        private DeviceManager DeviceManager;
        private FunscriptRepository Repository;
        private readonly Timer TimerPing = new(5000);
        private int AliveCheckFails = 0;

        public OSRProvider(FunscriptRepository repository, ConfigurationManager config, DeviceManager deviceManager)
        {
            Config = config.Get<OSRConfig>();

            this.DeviceManager = deviceManager;
            this.Repository = repository;

            TimerPing.Elapsed += TimerPingEvent;
        }

        public async Task Init()
        {
            TimerPing.Start();
            await Connect();
        }

        private void OnStatusChange(string e)
        {
            StatusChange?.Invoke(null, e);
        }

        private async Task Connect()
        {
            await UnloadDevice();
            if (Config.COMPort == null)
            {
                return;
            }

            var port = new SerialPort(Config.COMPort, 115200, Parity.None, 8, StopBits.One);

            try
            {
                port.ReadTimeout = 1000;
                port.WriteTimeout = 1000;
                port.Open();

                Device = new(port, Repository, Config);
                if (!Device.AlivePing())
                {
                    OnStatusChange("Device unverifiable as TCode device");
                    return;
                }

                _ = Device.ReturnToHome();

                AliveCheckFails = 0;
                DeviceManager.LoadDevice(Device);
            }
            catch (Exception e)
            {
                OnStatusChange("Error");
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
        }

        private void TimerPingEvent(object sender, ElapsedEventArgs e)
        {
            if (Device == null)
                _ = Connect();
            else
            {
                if (!Device.AlivePing() && ++AliveCheckFails >= 3)
                    _ = UnloadDevice();
                else
                {
                    AliveCheckFails = 0;
                }
            }
        }

        private async Task UnloadDevice()
        {
            if (Device != null)
            {
                await Device.Stop();
                if (Device.DevicePort.IsOpen)
                    Device.DevicePort.Close();
                await DeviceManager.UnloadDevice(Device);
            }

            Device = null;
        }
    }
}
