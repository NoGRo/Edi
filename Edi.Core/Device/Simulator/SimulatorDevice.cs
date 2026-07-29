using Edi.Core.Funscript.Command;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
using PropertyChanged;

namespace Edi.Core.Device.Simulator;

[AddINotifyPropertyChangedInterface]
public abstract class SimulatorDevice
    : DeviceBase<FunscriptRepository, FunscriptGallery>
{
    private const int RefreshRateMilliseconds = 16;

    private readonly DefinitionRepository definitionRepository;
    private readonly ILogger logger;
    private CmdLinear currentCommand;

    protected SimulatorDevice(
        FunscriptRepository repository,
        DefinitionRepository definitionRepository,
        ILogger logger)
        : base(repository, logger)
    {
        this.definitionRepository = definitionRepository;
        this.logger = logger;
    }

    public double ProgressValue { get; set; }
    public string GalleryName { get; set; } = "-";
    public string GalleryType { get; set; } = "-";
    public string GalleryLoop { get; set; } = "-";
    public string GallerySeek { get; set; } = "-";
    public string GalleryDuration { get; set; } = "-";
    public string GalleryCurrentTime { get; set; } = FormatTime(0);

    public override string DefaultVariant()
        => Variants.FirstOrDefault() ?? base.DefaultVariant();

    public override Task PlayGallery(FunscriptGallery gallery, long seek = 0)
        => PlayGallery(gallery, seek, playCancelTokenSource.Token);

    protected override async Task PlayGallery(
        FunscriptGallery gallery,
        long seek,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Starting simulated playback with {DeviceName}, gallery {GalleryName}, seek {Seek}.",
            Name,
            gallery?.Name ?? "Unknown",
            seek);
        SetGalleryInfo(gallery, seek);

        var commands = gallery?.Commands;
        if (commands == null || commands.Count == 0)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var currentTime = CurrentTime;
                var commandIndex = commands.FindIndex(
                    command => command.AbsoluteTime > currentTime);

                if (commandIndex < 0)
                {
                    if (!gallery.Loop)
                        break;

                    await Task.Delay(
                        RefreshRateMilliseconds,
                        cancellationToken);
                    continue;
                }

                currentCommand = commands[commandIndex];
                UpdateProgress(currentTime);

                await Task.Delay(
                    RefreshRateMilliseconds,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            ResetProgress();
        }
    }

    public override Task StopGallery()
    {
        ResetProgress();
        return Task.CompletedTask;
    }

    internal static int ScalePosition(double position, int min, int max)
    {
        var normalizedPosition = Math.Clamp(position, 0, 100);
        var scaledPosition =
            min + (max - min) * normalizedPosition / 100;
        return (int)Math.Round(scaledPosition);
    }

    internal static string FormatTime(long milliseconds)
    {
        var totalMilliseconds = Math.Max(0, milliseconds);
        var hours = totalMilliseconds / 3_600_000;
        var minutes = totalMilliseconds / 60_000 % 60;
        var seconds = totalMilliseconds / 1_000 % 60;
        var remainingMilliseconds = totalMilliseconds % 1_000;

        return $"{hours:00}:{minutes:00}:{seconds:00}.{remainingMilliseconds:000}";
    }

    private void UpdateProgress(long currentTime)
    {
        if (currentCommand == null)
            return;

        var progress =
            (currentTime
                - (currentCommand.AbsoluteTime - currentCommand.Millis))
            / (double)currentCommand.Millis;
        progress = Math.Clamp(progress, 0, 1);

        var interpolatedPosition =
            currentCommand.InitialValue
            + (currentCommand.Value - currentCommand.InitialValue) * progress;

        ProgressValue = ScalePosition(interpolatedPosition, Min, Max);
        GalleryCurrentTime = FormatTime(currentTime);
    }

    private void SetGalleryInfo(FunscriptGallery gallery, long seek)
    {
        if (gallery == null)
        {
            GalleryName = "Unavailable";
            GalleryType = "-";
            GalleryLoop = "-";
            GallerySeek = "-";
            GalleryDuration = "-";
            GalleryCurrentTime = FormatTime(0);
            return;
        }

        var definition = definitionRepository.Get(gallery.Name);
        GalleryName = gallery.Name;
        GalleryType = definition?.Type ?? "gallery";
        GalleryLoop = gallery.Loop ? "Yes" : "No";
        GallerySeek = FormatTime(seek);
        GalleryDuration = FormatTime(gallery.Duration);
        GalleryCurrentTime = FormatTime(seek);
    }

    private void ResetProgress()
    {
        currentCommand = null;
        ProgressValue = 0;
        GalleryCurrentTime = FormatTime(0);
    }
}
