using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using System;

namespace Edi.Core
{
    public interface IEdi
    {
        public Task Init();
        public DeviceManager DeviceManager { get; }
        public Trepo GetRepo<Trepo>()
        where Trepo : class , IRepository;
        public ConfigurationManager ConfigurationManager { get; }
        public IEnumerable<DefinitionGallery> Definitions { get; }
        public Task Play(string Name, long Seek = 0);
        public Task Stop();
        public Task Pause();
        public Task Resume(bool atCurrentTime);
        public Task Repack();

        public delegate void ChangeStatusHandler(string message);
        public event ChangeStatusHandler OnChangeStatus;
    }
}