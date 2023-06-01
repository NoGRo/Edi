namespace Edi.Core.Services
{
    public interface IEdi
    {
        
        public Task Init();
        public Task Play(string Name, long Seek = 0);
        public Task Stop();
        public Task Pause();
        public Task Resume();

    }
}