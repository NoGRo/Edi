using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device
{
    public interface IDevice
    {
        public Task SendGallery(string name,long seek = 0);
        public Task Pause();
        public Task Resume();
    }
}
