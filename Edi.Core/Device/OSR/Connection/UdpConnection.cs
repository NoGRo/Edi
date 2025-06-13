using Microsoft.Extensions.Logging;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Edi.Core.Device.OSR.Connection
{
    class UdpConnection : IOSRConnection
    {
        private UdpClient UdpClient;
        private string Address;
        private int Port;
        private ILogger Logger;
        public bool IsReady => UdpClient != null;

        public UdpConnection(string udpAddress, int udpPort, ILogger logger)
        {
            Address = udpAddress;
            Port = udpPort;
            Logger = logger;
        }


        public void Connect()
        {
            UdpClient = new UdpClient
            {
                ExclusiveAddressUse = false,
            };
            UdpClient.Client.Blocking = false;
            UdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            UdpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var ipAddress = IPAddress.Parse(Address);
            UdpClient.Connect(ipAddress, Port);

            Logger.LogInformation($"TCode device now communicating over UDP {Address}:{Port}");
        }

        public void Disconnect()
        {
            try
            {
                if (UdpClient != null)
                {
                    UdpClient.Close();
                    UdpClient.Dispose();
                    UdpClient = null;
                }
            }
            catch { }
        }

        public string GetDeviceName()
        {
            return $"TCode ({Address} : {Port})";
        }

        public bool ValidateTCode()
        {
            return true;
        }

        public void WriteLine(string message)
        {
            var bytes = Encoding.ASCII.GetBytes($"{message}\n");
            UdpClient?.Send(bytes, bytes.Length);
        }
    }
}
