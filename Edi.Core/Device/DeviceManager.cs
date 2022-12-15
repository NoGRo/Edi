using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device
{
    public class DeviceManager : ILoadDevice, ISendGallery
    {
        public List<ISendGallery> Devices { get; set; } =  new List<ISendGallery>();    

        public void LoadDevice(ISendGallery device)
        {
            Devices.Add(device); 
        }

        public async Task SendGallery(string name, long seek = 0)
        {
            Devices = Devices.Where(x => x != null).ToList();

            Devices.AsParallel().ForAll(async x => await x.SendGallery(name,seek));
        }

        public void UnloadDevice(ISendGallery device)
        {
            Devices.Remove(device);
        }
    }
}
