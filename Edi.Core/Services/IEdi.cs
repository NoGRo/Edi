using Edi.Core.Device.Interfaces;

namespace Edi.Core
{
    public interface IEdi
    {
        
        public Task Init();
        public IEnumerable<IDevice> Devices { get; }
        public EdiConfig Config { get; set; }
        public Task Play(string Name, long Seek = 0);
        public Task Stop();
        public Task Pause();
        public Task Resume();

    }
}