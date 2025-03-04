using System;
using System.Diagnostics;
using System.IO;
using Edi.Core.Funscript;
using Edi.Core;
using Newtonsoft.Json;
using System.Net.WebSockets;

namespace Edi.Core;

public class Recorder
{
    private Process ffmpegProcess;

    private string outputFileName => Path.Combine(Edi.GalleryDir, config.OutputName + ".mp4");
    private string funscriptFileName  => Path.ChangeExtension(outputFileName, ".funscript");
    private FunScriptFile funscript = new();
    private DateTime recordingStartTime;

    public bool IsRecording { get; set; }
    public RecorderConfig config { get; set; }

    private string currentChapter = "";

    public Recorder(ConfigurationManager configurationManager)
    {
        funscript.metadata ??= new FunScriptMetadata();
        
        config = configurationManager.Get<RecorderConfig>();
        
    }

        public void StartRecord()
        {
            if (IsRecording)
                throw new InvalidOperationException("Recording already in progress");

            

            funscript.path = funscriptFileName;
            funscript.filename = Path.GetFileNameWithoutExtension(funscriptFileName);

            ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = GenerateFfmpegCommand(),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardInput = true, // Añadido para permitir enviar comandos como "q"
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            ffmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                Debug.WriteLine(e.Data);
                if (e.Data != null && e.Data.Contains("frame=") && !IsRecording)
                {
                    var frameStr = e.Data.Split(new[] { "frame=" }, StringSplitOptions.None)[1].Trim().Split(' ')[0];
                    if (int.TryParse(frameStr, out int frameNum))
                    {
                        recordingStartTime = DateTime.Now.AddSeconds(-frameNum / 30.0); // 30 FPS
                        IsRecording = true;
                    }
                }
            };

        ffmpegProcess.Start();
            ffmpegProcess.BeginErrorReadLine();
        }

    public string GenerateFfmpegCommand()
    {
        return $"-y -f gdigrab -framerate {config.FrameRate} -offset_x {config.X} -offset_y {config.Y} " +
               $"-video_size {config.Width}x{config.Height} -i desktop {config.FfmpegCodec} \"{outputFileName}\"";
    }
    public void Play(string name, long seek = 0)
    {
        if (!IsRecording) 
            throw new InvalidOperationException("No recording in progress");

        if (!string.IsNullOrEmpty(currentChapter) && name != currentChapter)
        {
            var lastChapter = funscript.metadata.chapters.Find(c => c.name == currentChapter);
            lastChapter.EndTimeMilis = (long)(DateTime.Now - recordingStartTime).TotalMilliseconds - (1000 / config.FrameRate); // Frame anterior (30 FPS)
            currentChapter = "";
        }

        if (funscript.metadata.chapters.Exists(c => c.name == name)) 
            return;

        var chapter = new FunScriptChapter 
        { 
            name = name,
            StartTimeMilis = (long)(DateTime.Now - recordingStartTime).TotalMilliseconds + seek
        };

        funscript.metadata.chapters.Add(chapter);
        currentChapter = name;
    }

    public void Stop()
    {
        if (!IsRecording) 
            throw new InvalidOperationException("No recording in progress");

        if (string.IsNullOrEmpty(currentChapter)) 
            return;

        var chapter = funscript.metadata.chapters.Find(c => c.name == currentChapter);

        if (chapter == null) 
            return;

        chapter.EndTimeMilis = (long)(DateTime.Now - recordingStartTime).TotalMilliseconds;
        currentChapter = "";
    }

    public void End()
    {
        if (!IsRecording)
            throw new InvalidOperationException("No recording in progress");

        // Finalizar el capítulo actual, si lo hay
        Stop();
        funscript.Save(funscriptFileName);

        if (ffmpegProcess != null && !ffmpegProcess.HasExited)
        {
            try
            {
                // Enviar el comando "q" a FFmpeg para terminar la grabación de manera limpia
                ffmpegProcess.StandardInput.WriteLine("q");
                ffmpegProcess.StandardInput.Flush();

                // Esperar a que FFmpeg termine de procesar y cierre (con un tiempo máximo)
                bool exited = ffmpegProcess.WaitForExit(5000); // Espera hasta 5 segundos

                if (!exited)
                {
                    // Si no termina en el tiempo establecido, forzar el cierre
                    ffmpegProcess.Kill();
                    ffmpegProcess.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al intentar finalizar FFmpeg: {ex.Message}");
                // En caso de error, forzar el cierre como respaldo
                ffmpegProcess.Kill();
                ffmpegProcess.WaitForExit();
            }
        }

        IsRecording = false;
        ffmpegProcess?.Dispose();
    }
    public void AdjustChaptersByFrames(int frameOffset)
    {
        funscript = FunScriptFile.TryRead(funscriptFileName);
        if (funscript == null)
            return;

        if (funscript.metadata?.chapters == null || !funscript.metadata.chapters.Any())
            return; // Nada que ajustar si no hay capítulos

        double MsPerFrame = 1000.0 / config.FrameRate; // 30 FPS, precalculado
        long offsetMs = (long)(frameOffset * MsPerFrame);

        foreach (var chapter in funscript.metadata.chapters)
        {
            chapter.StartTimeMilis += offsetMs;
            chapter.EndTimeMilis += offsetMs; // Simplificado: asumimos que siempre hay EndTimeMilis si es relevante
        }

        funscript.Save(funscriptFileName); // Guardar los cambios
    }
}