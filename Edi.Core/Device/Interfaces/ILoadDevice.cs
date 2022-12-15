using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Interfaces
{
    public interface ILoadDevice
    {
        public void LoadDevice(IDevice device);
        public void UnloadDevice(IDevice device);
    }
}
