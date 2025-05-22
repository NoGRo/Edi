using Edi.Core.Device.Interfaces;

namespace Edi.Core.Players
{
    public interface IPlayBack
    {
        void Add(IDevice device);
        void Remove(IDevice device);

        Task Intensity(int Max);

        Task Play(string name, long seek = 0);
        Task Stop();
        Task Pause(bool untilResume = false);
        Task Resume(bool atCurrentTime = false);
   
    }

    public interface IPlayBackChannels
    {
        public 
        Task Play(string name, long seek = 0, string[] channels = null);
        Task Stop(string[] channels = null);
        Task Pause(bool untilResume = false, string[] channels = null);
        Task Resume(bool atCurrentTime = false, string[] channels = null);
        Task Intensity(int max, string[] channels = null);
    }
}