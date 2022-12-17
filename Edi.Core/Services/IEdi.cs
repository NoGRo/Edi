namespace Edi.Core.Services
{
    public interface iEdi
    {
        public Task Init();
        public Task Play(string Name, long Seek);
        public Task Pause();
        public Task Resume();
    }
}