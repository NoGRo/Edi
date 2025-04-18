using Microsoft.Extensions.Logging;
using Serilog.Core;
using System.IO.Ports;

namespace Edi.Core.Device.OSR.Connection
{
    public class SerialConnection : IOSRConnection
    {
        private SerialPort SerialPort;
        private int RetryCount = 0;
        private ILogger Logger;


        public bool IsReady { get => SerialPort.IsOpen; }

        public SerialConnection(string comPort, ILogger logger)
        {
            SerialPort = new SerialPort(comPort, 115200, Parity.None, 8, StopBits.One);
            Logger = logger;
        }

        public void Connect()
        {
            try
            {
                SerialPort.ReadTimeout = 1000;
                SerialPort.WriteTimeout = 1000;               

                //for romeo hardware
                SerialPort.RtsEnable = (RetryCount == 3);
                SerialPort.DtrEnable = (RetryCount == 3);
                if (RetryCount == 3)
                    RetryCount = 0;

                SerialPort.Open();

                var readWaits = 0;
                var maxWait = 10;  // 1s wait any random message
                var initText = "";
                while (readWaits < maxWait)
                {
                    if (SerialPort.BytesToRead > 0)
                    {
                        initText += SerialPort.ReadExisting();
                        readWaits = 0;
                        maxWait = 20; // 2s Ensure read wait all Start-Up sequence 
                        if (initText.Contains("System is Ready!\r\n")) // detect Start-Up sequence End 
                            break;
                    }
                    Thread.Sleep(100);
                    readWaits++;
                }
                Logger.LogInformation(initText);
                Thread.Sleep(100);
                SerialPort.DiscardInBuffer();
                Logger.LogInformation($"TCode device initialized on port {SerialPort.PortName}");
            }
            catch (Exception)
            {
                RetryCount++;
                throw;
            }
        }

        public void Disconnect()
        {
            if (SerialPort.IsOpen)
            {
                SerialPort.Close();
            }
        }

        public string GetDeviceName()
        {
            if (!SerialPort.IsOpen)
                return string.Empty;

            SerialPort.DiscardInBuffer();
            SerialPort.Write("d0\n");
            var tryCount = 0;

            while (SerialPort.BytesToRead == 0)
            {
                if (tryCount++ >= 5)
                    throw new Exception("Timeout waiting for TCode Name response");
                Thread.Sleep(100);
            }
            var response = SerialPort.ReadExisting();
            var name = response.Split('\n')
                                .Select(x => x.Trim())
                                .FirstOrDefault(x => !string.IsNullOrEmpty(x));

            if (name == null)
                throw new Exception("Fail get valid Name response");

            return name;
        }

        public bool ValidateTCode()
        {
            if (!SerialPort.IsOpen)
                return false;

            SerialPort.DiscardInBuffer();

            SerialPort.Write("d1\n");
            var tryCount = 0;

            while (SerialPort.BytesToRead == 0)
            {
                if (tryCount++ >= 5)
                    throw new Exception("Timeout waiting for TCode response");
                Thread.Sleep(100);
            }
            var protocol = SerialPort.ReadExisting();

            return (protocol.Contains("tcode", StringComparison.OrdinalIgnoreCase));
        }

        public void WriteLine(string message)
        {
            SerialPort.WriteLine(message);
        }
    }
}
