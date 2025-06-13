using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.OSR.Connection
{
    public interface IOSRConnection
    {
        bool IsReady { get; }
        void Connect();
        void Disconnect();
        bool ValidateTCode();
        string GetDeviceName();
        void WriteLine(string message);
    }
}
