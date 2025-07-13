using Edi.Core.Device.Interfaces;

namespace Edi.Core.Players
{
    public interface IPlayer
    {
        void Add(IDevice device);
        void Remove(IDevice device);

        Task Intensity(int Max);

        Task Play(string name, long seek = 0);
        Task Stop();
        Task Pause(bool untilResume = false);
        Task Resume(bool atCurrentTime = false);
   
    }
}