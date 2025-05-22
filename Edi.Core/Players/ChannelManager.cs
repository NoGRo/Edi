using CsvHelper;
using System.Collections.Concurrent;

public class ChannelManager<T>
{
    public const string MAIN_CHANNEL = "main";
    private ConcurrentDictionary<string, T> channels = new();
    private List<string> activeChannels = new() { MAIN_CHANNEL };
    private Func<T> factory;

    public ChannelManager(Func<T> factory)
    {
        this.factory = factory;
        EnsureChannel(MAIN_CHANNEL);
    }

    public event Action<string> OnFirstCustomChannelCreated;

    public IEnumerable<string> Channels => channels.Keys;
    public List<string> ActiveChannels => activeChannels;
    
    public IEnumerable<T> GetActive() => activeChannels.Select(c => channels[c]);
    public T Get(string name) => channels[name];

    private void EnsureChannel(string name)
    { 
        channels.TryAdd(name, factory());
    }
    private readonly SemaphoreSlim semaphore = new(1, 1);

    private void UseChannels(params string[] requestedChannels)
    {
        var names = (requestedChannels == null || requestedChannels.FirstOrDefault() == null)
            ? channels.Keys.ToList()
            : requestedChannels.Distinct().ToList();

        string newChannel = null;

        if (channels.ContainsKey(MAIN_CHANNEL) && names.Count > 0 && names.First() != MAIN_CHANNEL)
        {
            var oldMain = channels[MAIN_CHANNEL];
            channels.Clear();
            newChannel = names.First();
            channels[newChannel] = oldMain;
           
        }

        foreach (var name in names)
            EnsureChannel(name);

        activeChannels = names;

        if (newChannel != null) 
            OnFirstCustomChannelCreated?.Invoke(newChannel);
    }

    public async void WithChannel(string  channel, Action<T> action)
    {
        await WithChannels(new[] { channel }, a =>
        {
            action(a);
            return Task.CompletedTask;
        });
    }
    public async Task WithChannels(string[] channelNames, Func<T, Task> action)
    {
        await semaphore.WaitAsync();
        try
        {
            UseChannels(channelNames);
            var active = GetActive().ToList();
            await Task.WhenAll(active.Select(a =>action(a)));
        }
        finally
        {
            semaphore.Release();
        }
    }
   
}
