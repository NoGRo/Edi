namespace Edi.Core.Tests.Players;

public class ChannelManagerConcurrencyTests
{
    [Fact]
    public async Task NullChannelSelectionTargetsEveryActiveChannel()
    {
        var nextId = 0;
        var manager = new ChannelManager<int>(() => Interlocked.Increment(ref nextId));
        manager.UseChannels("left", "right");
        var visited = new HashSet<int>();

        await manager.WithChannels(null, channel =>
        {
            lock (visited)
            {
                visited.Add(channel);
            }

            return Task.CompletedTask;
        });

        Assert.Equal(2, visited.Count);
    }

    [Fact]
    public async Task ConcurrentOperationsAreSerialized()
    {
        var manager = new ChannelManager<object>(() => new object());
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var first = manager.WithChannels(null, async _ =>
        {
            firstStarted.TrySetResult();
            await releaseFirst.Task;
        });
        await firstStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        var second = manager.WithChannels(null, _ =>
        {
            secondStarted.TrySetResult();
            return Task.CompletedTask;
        });

        Assert.False(secondStarted.Task.IsCompleted);

        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.True(secondStarted.Task.IsCompletedSuccessfully);
    }
}
