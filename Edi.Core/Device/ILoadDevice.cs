using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device
{
    public interface ILoadDevice
    {
        public void LoadDevice(ISendGallery device);
    }
}
