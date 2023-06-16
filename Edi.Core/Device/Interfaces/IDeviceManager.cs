using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Interfaces
{
    public interface IDeviceManager
    {
        Task Init();
        public List<IDevice> Devices { get; }
        internal void LoadDevice(IDevice device);
        internal void UnloadDevice(IDevice device);
        public void SelectVariant(string deviceName, string variant);

        public Task PlayGallery(string name, long seek = 0);
        public Task Pause();
        public Task Resume();
        

        public delegate void OnUnloadDeviceHandler(IDevice device);
        public delegate void OnloadDeviceHandler(IDevice device);
        public event OnUnloadDeviceHandler OnUnloadDevice;
        public event OnloadDeviceHandler OnloadDevice;
    }
}
