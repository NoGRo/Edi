using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device
{
    public interface ISendGallery
    {
        public Task SendGallery(string name,long seek = 0);
    }
}
