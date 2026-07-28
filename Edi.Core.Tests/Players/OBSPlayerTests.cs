using Edi.Core.Players;

namespace Edi.Core.Tests.Players;

public class OBSPlayerTests
{
    [Fact]
    public async Task DisabledChapterGeneratorForwardsEveryPlaybackCommandOnce()
    {
        var inner = new RecordingPlayerChannels();
        var player = new OBSPlayer(
            inner,
            new EdiConfig { UseObsChapterGenerator = false },
            new OBSConfig(),
            new PlayerLogService());
        var channels = new[] { "left" };

        await player.Play("scene", 125, channels);
        await player.Pause(true, channels);
        await player.Resume(true, channels);
        await player.Intensity(70, channels);
        await player.Stop(channels);

        Assert.Equal(
            [
                "Play:scene:125:left",
                "Pause:True:left",
                "Resume:True:left",
                "Intensity:70:left",
                "Stop:left"
            ],
            inner.Commands);
    }

    private sealed class RecordingPlayerChannels : IPlayerChannels
    {
        public List<string> Channels { get; } = ["main"];
        public List<string> Commands { get; } = [];

        public event Action<List<string>>? ChannelsChanged;

        public void ResetChannels(List<string>? channels = null)
            => ChannelsChanged?.Invoke(channels ?? []);

        public Task Play(string name, long seek = 0, string[]? channels = null)
        {
            Commands.Add($"Play:{name}:{seek}:{Format(channels)}");
            return Task.CompletedTask;
        }

        public Task Stop(string[]? channels = null)
        {
            Commands.Add($"Stop:{Format(channels)}");
            return Task.CompletedTask;
        }

        public Task Pause(bool untilResume = false, string[]? channels = null)
        {
            Commands.Add($"Pause:{untilResume}:{Format(channels)}");
            return Task.CompletedTask;
        }

        public Task Resume(bool atCurrentTime = false, string[]? channels = null)
        {
            Commands.Add($"Resume:{atCurrentTime}:{Format(channels)}");
            return Task.CompletedTask;
        }

        public Task Intensity(int max, string[]? channels = null)
        {
            Commands.Add($"Intensity:{max}:{Format(channels)}");
            return Task.CompletedTask;
        }

        private static string Format(string[]? channels)
            => channels is null ? "<all>" : string.Join(",", channels);
    }
}
