namespace Edi.Core.Services
{
    public interface IEdi
    {
        
        public Task Init();
        public Task PlayGallery(string Name, bool play = true, long Seek = 0);

        public Task StopGallery();
        public Task SetFiller(string Name, bool play = false, long seek = 0);
        public Task StopFiller();
        public Task PlayReaction(string name);
        public Task StopReaction();
        public Task Pause();
        public Task Resume();

    }
}