using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
using PropertyChanged;
using System.Diagnostics;

namespace Edi.Core.Device.DgLab;

[AddINotifyPropertyChangedInterface]
public sealed class DgLabDevice
    : DeviceBase<FunscriptRepository, FunscriptGallery>,
      IDeviceWithConfiguration
{
    private static readonly TimeSpan WaveformLifetime =
        TimeSpan.FromMilliseconds(100);

    private readonly IDgLabController controller;
    private readonly DgLabChannel deviceChannel;
    private readonly ILogger logger;
    private DgLabChannelConfig channelConfiguration = new();
    private int lastPower = -1;
    private long playbackSequence;

    public DgLabDevice(
        IDgLabController controller,
        DgLabChannel deviceChannel,
        FunscriptRepository repository,
        ILogger logger)
        : base(repository, logger)
    {
        this.controller = controller;
        this.deviceChannel = deviceChannel;
        this.logger = logger;
        Name = $"{controller.Name} {deviceChannel}";
    }

    public DgLabChannel DeviceChannel => deviceChannel;
    public DgLabChannelConfig DgLabConfiguration =>
        channelConfiguration;

    public override bool IsReady
    {
        get => controller.IsConnected;
        set { }
    }

    public void ApplyConfiguration(DeviceConfig configuration)
    {
        configuration.DgLab ??= new DgLabChannelConfig();
        configuration.DgLab.Normalize();
        channelConfiguration = configuration.DgLab;
    }

    public override Task PlayGallery(
        FunscriptGallery gallery,
        long seek = 0)
        => PlayGallery(gallery, seek, playCancelTokenSource.Token);

    protected override async Task PlayGallery(
        FunscriptGallery gallery,
        long seek,
        CancellationToken cancellationToken)
    {
        var playbackId = Interlocked.Increment(
            ref playbackSequence);
        var started = Stopwatch.StartNew();
        var lastFrameCompletedAt = TimeSpan.Zero;
        var frameCount = 0;
        lastPower = -1;
        logger.LogInformation(
            "DG-Lab playback {PlaybackId} requested on {Channel}. Gallery: {Gallery}, Seek: {Seek}",
            playbackId,
            deviceChannel,
            gallery.Name,
            seek);
        using var timer = new PeriodicTimer(WaveformLifetime);
        try
        {
            do
            {
                var frame = DgLabSignalMapper.Map(
                    gallery,
                    CurrentTime,
                    channelConfiguration,
                    Min,
                    Max);
                if (frame.Power != lastPower)
                {
                    await controller.SetPower(
                        deviceChannel,
                        frame.Power,
                        cancellationToken);
                    lastPower = frame.Power;
                }

                await controller.WriteWaveform(
                    deviceChannel,
                    frame.Waveform,
                    cancellationToken);
                frameCount++;

                var completedAt = started.Elapsed;
                if (frameCount == 1)
                {
                    logger.LogInformation(
                        "DG-Lab playback {PlaybackId} sent its first beat in {ElapsedMilliseconds} ms. Active: {IsActive}, Power: {Power}, ScriptTime: {ScriptTime}",
                        playbackId,
                        completedAt.TotalMilliseconds,
                        frame.IsActive,
                        frame.Power,
                        CurrentTime);
                }
                else
                {
                    var gap = completedAt - lastFrameCompletedAt;
                    if (gap >= TimeSpan.FromMilliseconds(300))
                    {
                        logger.LogWarning(
                            "DG-Lab playback {PlaybackId} had a {GapMilliseconds} ms beat gap after frame {FrameCount}. ScriptTime: {ScriptTime}",
                            playbackId,
                            gap.TotalMilliseconds,
                            frameCount,
                            CurrentTime);
                    }
                }

                lastFrameCompletedAt = completedAt;
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "DG-Lab playback {PlaybackId} for {Gallery} was superseded after {ElapsedMilliseconds} ms and {FrameCount} frames",
                playbackId,
                gallery.Name,
                started.ElapsedMilliseconds,
                frameCount);
        }
    }

    public override async Task StopGallery()
    {
        lastPower = 0;
        await controller.Stop(deviceChannel, CancellationToken.None);
    }
}
