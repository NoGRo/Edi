using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Interfaces
{
    public interface IDeviceManager : ILoadDevice, IDevice
    {
        public List<IDevice> Devices { get; }
    }
}
