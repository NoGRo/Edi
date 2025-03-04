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

    private string outputFileName;
    private string funscriptFileName;
    private readonly FunScriptFile funscript = new();
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

            outputFileName = Path.Combine(Edi.GalleryDir, config.OutputName+ ".mp4");

            funscriptFileName = Path.ChangeExtension(outputFileName, ".funscript");
            funscript.path = funscriptFileName;
            funscript.filename = Path.GetFileNameWithoutExtension(funscriptFileName);

            ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y  -f gdigrab -framerate 30 -offset_x {config.X} -offset_y {config.Y} -video_size {config.Width}x{config.Height} -i desktop {config.FfmpegCodec} \"{outputFileName}\"",
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

    public void Play(string name, long seek = 0)
    {
        if (!IsRecording) 
            throw new InvalidOperationException("No recording in progress");

        if (!string.IsNullOrEmpty(currentChapter) && name != currentChapter) 
            Stop();

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
}