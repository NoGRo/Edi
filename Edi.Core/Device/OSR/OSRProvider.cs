﻿using Buttplug.Client;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
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

        private ILogger logger;
        private OSRDevice? Device;
        private DeviceManager DeviceManager;
        private FunscriptRepository Repository;
        private readonly Timer TimerPing = new(5000);
        private int AliveCheckFails = 0;
        private int RetryCount = 0;

        public OSRProvider(FunscriptRepository repository, ConfigurationManager config, DeviceManager deviceManager, ILogger logger)
        {
            this.logger = logger;
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
            TimerPing.Stop();   
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

                //for romeo hardware
                port.RtsEnable = (RetryCount == 3);
                if (RetryCount == 3) 
                    RetryCount = 0;

                port.Open();

                var readWaits = 0;
                var maxWait = 10;  // 1s wait any random message
                var initText = "";
                while (readWaits < maxWait)
                {
                    if (port.BytesToRead > 0)
                    {
                        initText+= port.ReadExisting();
                        readWaits = 0;
                        maxWait = 20; // 2s Ensure read wait all Start-Up sequence 
                        if (initText.Contains("System is Ready!\r\n")) // detect Start-Up sequence End 
                            break;
                    }
                    Thread.Sleep(100);
                    readWaits++;
                }
                logger.LogInformation(initText);
                Thread.Sleep(100);
                port.DiscardInBuffer(); 
                Device = new(port, Repository, Config, logger);
                
                _ = Device.ReturnToHome();
                logger.LogInformation($"TCode device initialized on port {Config.COMPort}");

                AliveCheckFails = 0;
                DeviceManager.LoadDevice(Device);
            }
            catch (Exception e)
            {
                OnStatusChange("Error");
                logger.LogError(e, $"Error while attempting to connect TCode device: {e.Message}");
                RetryCount++;
                if (port.IsOpen)
                {
                    port.Close();
                    Device = null; 
                }
            }
            finally {
                TimerPing.Start();
            }
        }

        private void TimerPingEvent(object sender, ElapsedEventArgs e)
        {
            if (Device == null)
                _ = Connect();
            else
            {
                if (!Device.AlivePing())
                {
                    logger.LogWarning($"TCode device liveness check failed");
                    if  (++AliveCheckFails >= 3)
                        _ = UnloadDevice();
                }
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
                logger.LogInformation("Unloaded TCode device");
            }

            Device = null;
        }
    }
}
