using System.Collections.ObjectModel;

namespace Edi.Core.Players
{
    public interface IPlayerChannels
    {
        void ResetChannels(List<string> channels = null);
        List<string> Channels { get; }
        event Action<List<string>> ChannelsChanged;
        Task Play(string name, long seek = 0, string[] channels = null);
        Task Stop(string[] channels = null);
        Task Pause(bool untilResume = false, string[] channels = null);
        Task Resume(bool atCurrentTime = false, string[] channels = null);
        Task Intensity(int max, string[] channels = null);
    }
}