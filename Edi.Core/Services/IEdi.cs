namespace Edi.Core.Services
{
    public interface iEdi
    {
        public Task Play(string Name, long Seek);
        public Task Stop();

    }
}