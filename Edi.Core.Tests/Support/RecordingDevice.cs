using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Tests.Support;

internal sealed class RecordingDevice : IDevice
{
    private readonly FunscriptRepository repository;
    private readonly object commandLock = new();
    private readonly List<PlaybackCommand> commands = [];
    private readonly SemaphoreSlim commandChanged = new(0);
    private long sequence;

    public RecordingDevice(FunscriptRepository repository, string name = "Test Device")
    {
        this.repository = repository;
        Name = name;
    }

    public Func<string, long, Task>? PlayBehavior { get; set; }
    public Func<Task>? StopBehavior { get; set; }

    public string? Channel { get; set; }
    public string? SelectedVariant { get; set; } = "default";
    public string Name { get; set; }
    public bool IsReady { get; set; } = true;
    public IEnumerable<string> Variants => repository.GetVariants();

    public IReadOnlyList<PlaybackCommand> Commands
    {
        get
        {
            lock (commandLock)
            {
                return commands.ToList();
            }
        }
    }

    public string DefaultVariant() => "default";

    public Task PlayGallery(string name, long seek = 0)
    {
        var gallery = repository.Get(name, SelectedVariant)
                      ?? throw new InvalidOperationException(
                          $"The integration fixture could not resolve gallery '{name}' " +
                          $"with variant '{SelectedVariant}'.");

        Record(PlaybackCommandKind.Play, gallery.Name, seek);
        return PlayBehavior?.Invoke(name, seek) ?? Task.CompletedTask;
    }

    public Task Stop()
    {
        Record(PlaybackCommandKind.Stop);
        return StopBehavior?.Invoke() ?? Task.CompletedTask;
    }

    public async Task<PlaybackCommand> WaitForPlayAsync(
        string galleryName,
        int occurrence,
        TimeSpan? timeout = null)
        => await WaitForCommandAsync(
            command => command.Kind == PlaybackCommandKind.Play
                       && command.GalleryName == galleryName,
            occurrence,
            timeout);

    public async Task<PlaybackCommand> WaitForPlayAsync(
        string galleryName,
        long seek,
        TimeSpan? timeout = null)
        => await WaitForCommandAsync(
            command => command.Kind == PlaybackCommandKind.Play
                       && command.GalleryName == galleryName
                       && command.Seek == seek,
            occurrence: 1,
            timeout);

    public async Task<PlaybackCommand> WaitForStopAsync(
        int occurrence,
        TimeSpan? timeout = null)
        => await WaitForCommandAsync(
            command => command.Kind == PlaybackCommandKind.Stop,
            occurrence,
            timeout);

    private async Task<PlaybackCommand> WaitForCommandAsync(
        Func<PlaybackCommand, bool> predicate,
        int occurrence,
        TimeSpan? timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));

        while (true)
        {
            lock (commandLock)
            {
                var matches = commands.Where(predicate).ToList();

                if (matches.Count >= occurrence)
                    return matches[occurrence - 1];
            }

            await commandChanged.WaitAsync(cancellation.Token);
        }
    }

    private void Record(PlaybackCommandKind kind, string? galleryName = null, long seek = 0)
    {
        var command = new PlaybackCommand(
            Interlocked.Increment(ref sequence),
            kind,
            galleryName,
            seek);

        lock (commandLock)
        {
            commands.Add(command);
        }

        commandChanged.Release();
    }
}
