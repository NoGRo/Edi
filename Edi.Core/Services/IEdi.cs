using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Definition;
using System;

namespace Edi.Core
{
    public interface IEdi
    {
        public Task Init();
        public IDeviceManager DeviceManager { get; }
        public ConfigurationManager ConfigurationManager { get; }
        public IEnumerable<DefinitionGallery> Definitions { get; }
        public Task Play(string Name, long Seek = 0);
        public Task Stop();
        public Task Pause();
        public Task Resume();

        public delegate void ChangeStatusHandler(string message);
        public event ChangeStatusHandler OnChangeStatus;
    }
}