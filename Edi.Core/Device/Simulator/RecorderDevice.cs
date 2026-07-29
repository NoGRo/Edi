using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PropertyChanged;
using System.Text;

namespace Edi.Core.Device.Simulator;

[AddINotifyPropertyChangedInterface]
public class RecorderDevice : SimulatorDevice, IRange, IHiddenDevice
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;
    private readonly object recordingLock = new();
    private readonly SemaphoreSlim fileLock = new(1, 1);
    private RecordingTimeline timeline;
    private long recordingStartedAt;
    private CancellationTokenSource flushCancellation;
    private Task flushTask = Task.CompletedTask;

    internal override bool SelfManagedLoop => true;

    public RecorderDevice(
        FunscriptRepository repository,
        DefinitionRepository definitionRepository,
        ILogger<RecorderDevice> logger,
        TimeProvider timeProvider = null)
        : base(repository, definitionRepository, logger)
    {
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        Name = "Recorder";
    }

    public bool IsRecording { get; private set; }
    public string OutputFilePath { get; private set; }
    public int RecordedActionCount { get; private set; }

    public string StartRecording(string outputFilePath = null)
    {
        lock (recordingLock)
        {
            if (IsRecording)
                return OutputFilePath;

            var startedAt = timeProvider.GetUtcNow();
            var outputDirectory = Path.Combine(
                global::Edi.Core.Edi.OutputDir,
                "Recordings");
            Directory.CreateDirectory(outputDirectory);

            OutputFilePath = outputFilePath
                ?? Path.Combine(
                    outputDirectory,
                    $"{SanitizeFileName(Name)}_{startedAt:yyyy-MM-dd_HH-mm-ss-fff}.funscript");

            var parentDirectory = Path.GetDirectoryName(OutputFilePath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
                Directory.CreateDirectory(parentDirectory);

            timeline = new RecordingTimeline();
            recordingStartedAt = timeProvider.GetTimestamp();
            flushCancellation = new CancellationTokenSource();
            IsRecording = true;
            RecordedActionCount = 1;
            flushTask = FlushPeriodically(flushCancellation.Token);
        }

        logger.LogInformation(
            "Recorder {RecorderName} started writing {OutputFilePath}.",
            Name,
            OutputFilePath);
        return OutputFilePath;
    }

    public async Task StopRecording()
    {
        CancellationTokenSource cancellation;
        Task backgroundFlush;

        lock (recordingLock)
        {
            if (!IsRecording)
                return;

            timeline.StopSegment(ElapsedMilliseconds());
            IsRecording = false;
            cancellation = flushCancellation;
            backgroundFlush = flushTask;
            flushCancellation = null;
            flushTask = Task.CompletedTask;
        }

        cancellation.Cancel();
        try
        {
            await backgroundFlush;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }

        await FlushToDisk(throwOnFailure: true);
        logger.LogInformation(
            "Recorder {RecorderName} stopped. Recording saved to {OutputFilePath}.",
            Name,
            OutputFilePath);
    }

    protected override Task PlayGallery(
        FunscriptGallery gallery,
        long seek,
        CancellationToken cancellationToken)
    {
        lock (recordingLock)
        {
            if (IsRecording && gallery?.Commands?.Count > 0)
            {
                timeline.StartSegment(
                    gallery,
                    seek,
                    Min,
                    Max,
                    ElapsedMilliseconds());
            }
        }

        return base.PlayGallery(gallery, seek, cancellationToken);
    }

    public override async Task StopGallery()
    {
        lock (recordingLock)
        {
            if (IsRecording)
                timeline.StopSegment(ElapsedMilliseconds());
        }

        await base.StopGallery();
    }

    internal override Task applyRange()
    {
        lock (recordingLock)
        {
            if (IsRecording && currentGallery?.Commands?.Count > 0)
            {
                timeline.StartSegment(
                    currentGallery,
                    CurrentTime,
                    Min,
                    Max,
                    ElapsedMilliseconds());
            }
        }

        return Task.CompletedTask;
    }

    private long ElapsedMilliseconds()
        => (long)timeProvider
            .GetElapsedTime(recordingStartedAt, timeProvider.GetTimestamp())
            .TotalMilliseconds;

    private async Task FlushPeriodically(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(FlushInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await FlushToDisk();
    }

    private async Task FlushToDisk(bool throwOnFailure = false)
    {
        List<FunScriptAction> actions;
        string outputPath;

        lock (recordingLock)
        {
            if (timeline == null || string.IsNullOrWhiteSpace(OutputFilePath))
                return;

            actions = timeline.Snapshot(ElapsedMilliseconds());
            outputPath = OutputFilePath;
            RecordedActionCount = actions.Count;
        }

        var script = new FunScriptFile { actions = actions };
        var json = JsonConvert.SerializeObject(script, Formatting.Indented);
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";

        await fileLock.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Recorder {RecorderName} could not write {OutputFilePath}.",
                Name,
                outputPath);
            TryDelete(temporaryPath);
            if (throwOnFailure)
                throw;
        }
        finally
        {
            fileLock.Release();
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(
            (string.IsNullOrWhiteSpace(value) ? "Recorder" : value)
            .Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A later recording must not fail because a temporary file could not be removed.
        }
    }
}
