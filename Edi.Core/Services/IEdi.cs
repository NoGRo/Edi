namespace Edi.Core
{
    public interface IEdi
    {
        
        public Task Init();
        public EdiConfig Config { get; set; }
        public Task Play(string Name, long Seek = 0);
        public Task Stop();
        public Task Pause();
        public Task Resume();

    }
}