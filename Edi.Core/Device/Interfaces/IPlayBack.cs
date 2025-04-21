namespace Edi.Core.Device.Interfaces
{
    public interface IPlayBack
    {
        void UseChannels(params string[] channelNames);
        Task Intensity(int Max);
        Task Pause(bool untilResume = false);
        Task Play(string name, long seek = 0);
        Task Resume(bool atCurrentTime = false);
        Task Stop();
    }
}