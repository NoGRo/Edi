namespace Edi.Core.Services
{
    public interface iEdi
    {
        
        public Task Init();
        public Task Play(string Name, long Seek);
        public Task Filler(string Name, bool play = false, long seek = 0);
        public Task Pause();
        public Task Resume();
        public Task Stop();
    }
}