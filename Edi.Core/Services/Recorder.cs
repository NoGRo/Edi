using System;
using System.Diagnostics;
using System.IO;
using Edi.Core.Funscript;
using Edi.Core;
using Newtonsoft.Json;
using System.Net.WebSockets;

namespace Edi.Core;

public class Recorder : IRecorder
{
    private Process ffmpegProcess;

    private string outputFileName => config.OutputName;
    private string funscriptFileName => Path.ChangeExtension(outputFileName, ".funscript");
    private FunScriptFile funscript = new();
    private DateTime recordingStartTime;

    public bool IsRecording { get; set; }
    public RecorderConfig config { get; set; }

    private string currentChapter = "";
    private long currentTime => Convert.ToInt64(Math.Round((DateTime.Now - recordingStartTime).TotalMilliseconds / MsPerFrame) * (MsPerFrame));
    private double MsPerFrame => (1000.0 / config.FrameRate);
    private double frameRate => config.FrameRate;

    private List<(DateTime arrivalTime, double ffmpegTimeMs)> timeSamples = new();
    private const int REQUIRED_SAMPLES = 3; // Reducido de 5 a 3
    private bool delayCalculated;


    public Recorder(ConfigurationManager configurationManager)
    {
        config = configurationManager.Get<RecorderConfig>();

    }
    private DateTime processStartTime;
    private double estimatedDelayMs = 0;
    public void StartRecord()
    {
        if (IsRecording)
            throw new InvalidOperationException("Recording already in progress");

        currentChapter = "";
        funscript = new();
        funscript.metadata ??= new FunScriptMetadata() { chapters = new() };
        funscript.path = funscriptFileName;
        funscript.actions = new();
        funscript.filename = Path.GetFileNameWithoutExtension(funscriptFileName);

        ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = config.FfmpegFullCommand.Remove(0, 7),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = true, // Añadido para permitir enviar comandos como "q"
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        ffmpegProcess.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data?.Contains("time=") != true || !TimeSpan.TryParse(e.Data.Split("time=")[1].Split(' ')[0], out var time))
                return;

            var sample = (DateTime.Now, time.TotalMilliseconds);
            if (IsRecording) return;

            timeSamples.Add(sample);
            if (timeSamples.Count <= 1 || delayCalculated) return; // Salta el primer mensaje

            if (timeSamples.Count > REQUIRED_SAMPLES)
            {
                estimatedDelayMs = timeSamples.Skip(1).Take(REQUIRED_SAMPLES)
                    .Average(s => (s.arrivalTime - processStartTime).TotalMilliseconds - s.ffmpegTimeMs);
                recordingStartTime = processStartTime.AddMilliseconds(-estimatedDelayMs);
                IsRecording = delayCalculated = true;
                Console.WriteLine($"Retraso promedio (muestras {REQUIRED_SAMPLES}): {estimatedDelayMs:F2} ms");
            }
        };
        processStartTime = DateTime.Now;
        ffmpegProcess.Start();
        ffmpegProcess.BeginErrorReadLine();
    }

    public void AddChapter(string name, long seek = 0, int? addPointAtPosition = null)
    {
        if (!IsRecording)
            throw new InvalidOperationException("No recording in progress");

        if (!string.IsNullOrEmpty(currentChapter) && name != currentChapter)
        {
            var lastChapter = funscript.metadata.chapters.Find(c => c.name == currentChapter);
            lastChapter.EndTimeMilis = Convert.ToInt64(currentTime - MsPerFrame - seek); // Frame anterior (30 FPS)
            currentChapter = "";
        }

        if (addPointAtPosition != null)
            AddPoint(addPointAtPosition.Value);

        if (funscript.metadata.chapters.Exists(c => c.name == name))
            return;

        var chapter = new FunScriptChapter
        {
            name = name,
            StartTimeMilis = currentTime - seek
        };

        funscript.metadata.chapters.Add(chapter);
        currentChapter = name;
    }
    public void AddPoint(int position)
    {
        if (!IsRecording)
            throw new InvalidOperationException("No recording in progress");

        funscript.actions.Add(new FunScriptAction
        {
            at = currentTime,
            pos = position
        });
    }
    public void EndChapter(int? addPointAtPosition = null)
    {
        if (!IsRecording)
            throw new InvalidOperationException("No recording in progress");

        if (addPointAtPosition != null)
            AddPoint(addPointAtPosition.Value);


        if (string.IsNullOrEmpty(currentChapter))
            return;

        var chapter = funscript.metadata.chapters.Find(c => c.name == currentChapter);

        if (chapter == null)
            return;

        chapter.EndTimeMilis = currentTime;
        currentChapter = "";
    }
    public void Stop()
    {
        if (!IsRecording)
            throw new InvalidOperationException("No recording in progress");

        // Finalizar el capítulo actual, si lo hay
        EndChapter();
        funscript.Save(funscriptFileName);

        try
        {
            if ((ffmpegProcess?.HasExited) != false)
                return;

            ffmpegProcess.StandardInput.WriteLine("q");
            ffmpegProcess.StandardInput.Flush();

            if (!ffmpegProcess.WaitForExit(5000))
                ffmpegProcess.Kill();


        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error al intentar finalizar FFmpeg: {ex.Message}");
            ffmpegProcess?.Kill();
        }
        finally
        {
            IsRecording = false;
            ffmpegProcess?.Dispose();
        }
    }

    // Método genérico para aplicar el ajuste
    private void ApplyTimeAdjustment(long offsetMs)
    {
        funscript = FunScriptFile.TryRead(funscriptFileName);
        if (funscript == null)
            return;

        if (funscript.metadata?.chapters == null || !funscript.metadata.chapters.Any())
            return;

        foreach (var chapter in funscript.metadata.chapters)
        {
            chapter.StartTimeMilis += offsetMs;
            if (chapter.EndTimeMilis != 0)
                chapter.EndTimeMilis += offsetMs;
        }

        foreach (var action in funscript.actions)
            action.at += offsetMs;

        funscript.Save(funscriptFileName);
    }

    public void AdjustByFrames(int frameOffset)
    {

        long offsetMs = (long)(frameOffset * MsPerFrame);
        ApplyTimeAdjustment(offsetMs);

    }
}