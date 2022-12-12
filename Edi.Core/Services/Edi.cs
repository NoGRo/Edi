using Edi.Core.Device;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Services
{
    public class Edi : iEdi
    {
        private readonly DeviceManager deviceManager;

        public Edi(DeviceManager deviceManager)
        {
            this.deviceManager = deviceManager;
        }

        public async Task Play(string Name, long Seek)
        {
            await deviceManager.SendGallery(Name);
        }

        public Task Stop()
        {
            throw new NotImplementedException();
        }
    }
}
