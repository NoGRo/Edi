namespace Edi.Core.Services
{
    public interface IEdi
    {
        
        public Task Init();
        public Task Gallery(string Name, long Seek = 0);
        public Task StopGallery();
        public Task Pause();
        public Task Resume();

    }
}