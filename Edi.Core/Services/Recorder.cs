using System;
using System.Diagnostics;
using System.IO;
using Edi.Core.Funscript;
using Edi.Core;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Xml.Linq;

namespace Edi.Core
{
    public class Recorder : IRecorder
    {
        private string outputFileName => config.OutputName;
        private string funscriptFileName => Path.ChangeExtension(outputFileName, ".funscript");
        private FunScriptFile funscript = new();
        private DateTime recordingStartTime;

        public bool IsRecording { get; set; }
        public RecorderConfig config { get; set; }
        public string CurrentChapter { get; set; } = "";
        private long currentTime => Convert.ToInt64(Math.Round((DateTime.Now - recordingStartTime).TotalMilliseconds));

        // Evento para actualizar el label de estado
        public event EventHandler<string> StatusUpdated;

        public Recorder(ConfigurationManager configurationManager)
        {
            config = configurationManager.Get<RecorderConfig>();
            UpdateStatus("Recorder ready");
        }

        public void Start()
        {
            if (IsRecording)
            {
                UpdateStatus("Recording already in progress");
            }

            CurrentChapter = "";
            funscript = new();
            funscript.metadata ??= new FunScriptMetadata() { chapters = new() };
            funscript.path = funscriptFileName;
            funscript.actions = new();
            funscript.filename = Path.GetFileNameWithoutExtension(funscriptFileName);

            recordingStartTime = DateTime.Now;
            funscript.actions.AddRange(new[]
            {
                new FunScriptAction { at = 0, pos = 0 },
                new FunScriptAction { at = 500, pos = 100 },
                new FunScriptAction { at = 1000, pos = 0 }
            });
            IsRecording = true;

            UpdateStatus("Recording started");

        }

        public void AddChapter(string name, long seek = 0, int? addPointAtPosition = null)
        {
            if (!IsRecording)
            {
                UpdateStatus("No recording in progress");
                return;
            }

            if (!string.IsNullOrEmpty(CurrentChapter) && name != CurrentChapter)
            {
                var lastChapter = funscript.metadata.chapters.Find(c => c.name == CurrentChapter);
                lastChapter.EndTimeMilis = Convert.ToInt64(currentTime - 1 - seek);
                CurrentChapter = "";
            }

            if (addPointAtPosition != null)
            {
                AddPoint(addPointAtPosition.Value, 1 - seek);
            }

            if (funscript.metadata.chapters.Exists(c => c.name == name))
            {
                UpdateStatus($"Chapter '{name}' already exists");
                return;
            }

            var chapter = new FunScriptChapter
            {
                name = name,
                StartTimeMilis = currentTime - seek
            };

            funscript.metadata.chapters.Add(chapter);
            CurrentChapter = name;
            UpdateStatus($"Chapter '{name}' { (addPointAtPosition.HasValue ? $" Point at {addPointAtPosition}" : "" )}");
        }

        public void AddPoint(int position, long seek = 0)
        {
            if (!IsRecording)
            {
                UpdateStatus("No recording in progress"); 
                return;
            }

            var action = new FunScriptAction
            {
                at = currentTime - seek,
                pos = position
            };
            funscript.actions.Add(action);
            UpdateStatus($"Chapter '{currentTime}' Point at {position}");
        }

        public void EndChapter(int? addPointAtPosition = null, long seek = 0)
        {
            if (!IsRecording)
            {
                UpdateStatus("No recording in progress");
                return;
            }

            if (addPointAtPosition != null)
            {
                AddPoint(addPointAtPosition.Value, seek);
            }

            if (string.IsNullOrEmpty(CurrentChapter))
            {
                UpdateStatus("No active chapter");
                return;
            }

            var chapter = funscript.metadata.chapters.Find(c => c.name == CurrentChapter);
            if (chapter == null)
            {
                UpdateStatus($"Chapter '{CurrentChapter}' not found");
                return;
            }

            chapter.EndTimeMilis = currentTime - seek;
            UpdateStatus($"Chapter '{CurrentChapter}' ended");
            CurrentChapter = "";
        }

        public void Stop()
        {
            if (!IsRecording)
            {
                UpdateStatus("No recording in progress");
                throw new InvalidOperationException("No recording in progress");
            }

            EndChapter();
            funscript.Save(funscriptFileName);
            IsRecording = false;
            UpdateStatus($"Recording stopped - Saved as: {Path.GetFileName(funscriptFileName)}");
        }

        private void UpdateStatus(string message)
        {
            StatusUpdated?.Invoke(this, message);
        }
    }
}