using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR.Connection;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Device.OSR.Connection;
using Microsoft.Extensions.Logging;
using System.Timers;
using Timer = System.Timers.Timer;
using Edi.Core.Services;

namespace Edi.Core.Device.OSR
{
    public class OSRProvider : IDeviceProvider
    {
        public readonly OSRConfig Config;
        public event EventHandler<string> StatusChange;

        private ILogger logger;
        private OSRDevice Device;
        private DeviceCollector DeviceCollector;
        private FunscriptRepository Repository;
        private readonly Timer TimerPing = new(5000);
        private int AliveCheckFails = 0;
        private int RetryCount = 0;
        private IOSRConnection Connection;

        public OSRProvider(FunscriptRepository repository, ConfigurationManager config, DeviceCollector deviceCollector, ILogger<OSRProvider> logger)
        {
            this.logger = logger;
            Config = config.Get<OSRConfig>();

            DeviceCollector = deviceCollector;
            Repository = repository;

            TimerPing.Elapsed += TimerPingEvent;
        }

        public async Task Init()
        {
            if (Connection != null)
            {
                Connection.Disconnect();
            }

            if (Config.COMPort != null)
                Connection = new SerialConnection(Config.COMPort, logger);
            else if (Config.UdpAddress != null)
            {
                var splitAddress = Config.UdpAddress.Split(':');
                Connection = new UdpConnection(splitAddress[0].Trim(), int.Parse(splitAddress[1]), logger);
            }
            else
            {
                Connection = null;
            }

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
            if (Connection == null)
            {
                return;
            }

            try
            {
                Connection.Connect();
                Device = new(Connection, Repository, Config, logger);

                _ = Device.ReturnToHome();

                AliveCheckFails = 0;
                DeviceCollector.LoadDevice(Device);
            }
            catch (Exception e)
            {
                OnStatusChange("Error");
                logger.LogError(e, $"Error while attempting to connect TCode device: {e.Message}");
                if (Connection?.IsReady == true)
                {
                    Connection.Disconnect();
                    Device = null;
                }
            }
            finally
            {
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
                    if (++AliveCheckFails >= 3)
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
                Connection.Disconnect();
                DeviceCollector.UnloadDevice(Device);
                logger.LogInformation("Unloaded TCode device");
            }

            Device = null;
        }
    }
}
