using Edi.Core.Device.Interfaces;
using System;

namespace Edi.Core
{
    public interface IEdi
    {
        public Task Init();
        public IDeviceManager DeviceManager { get; }

        public delegate void ChangeStatusHandler(string message);
        public event ChangeStatusHandler OnChangeStatus;
        public EdiConfig Config { get; set; }
        public Task Play(string Name, long Seek = 0);
        public Task Stop();
        public Task Pause();
        public Task Resume();
    }
}