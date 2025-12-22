using Edi.Core.Services;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Funscript.Command;
using Edi.Core.Players;
using Microsoft.AspNetCore.Hosting;

namespace Edi.Core.Device.Simulator
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class RecorderConfig
    {
        public bool Record { get; set; } = false;
    }

    [AddINotifyPropertyChangedInterface]
    public class RecorderDevice : DeviceBase<FunscriptRepository, FunscriptGallery>, IRange
    {
        private readonly ILogger _logger;
        private readonly SyncPlaybackFactory syncPlaybackFactory;
        private readonly string _outputFilePath;
        private readonly DateTime _recordingStartTime;
        public long RecordingAbsolueTime => (long)(DateTime.Now - _recordingStartTime).TotalMilliseconds;

        private readonly object _lock = new object();
        private readonly System.Timers.Timer _flushTimer;
        private bool _isFlushing = false;
        private int _lastWritePosition = 0; // Cambiado a int para Substring
        private string _postActionsContent = "]}"; // Contenido posterior al array actions
        private SyncPlayback syncPrev;
        private List<FunScriptAction> _actions = new() { new() { at = 0, pos = 0 } };
        private ScriptBuilder scriptBuilder =  new();

        internal override bool SelfManagedLoop => true;

        public RecorderDevice(FunscriptRepository repository, ILogger logger, SyncPlaybackFactory syncPlaybackFactory)
            : base(repository, logger)
        {
            _logger = logger;
            this.syncPlaybackFactory = syncPlaybackFactory;
            Name = "Output Recorder Device";
                        // Inicializa metadata con valores útiles
         
            // Asegura que la carpeta de salida exista antes de usarla
            var outputPath = Path.Combine(Edi.OutputDir, "Recordings");
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }
            _recordingStartTime = DateTime.Now;
            var filename = $"Session_{_recordingStartTime:yyyy-MM-dd_HH-mm-ss}.funscript";
            _outputFilePath = Path.Combine(outputPath, filename);

            _flushTimer = new System.Timers.Timer(10000); // 10 segundos
            _flushTimer.Elapsed += (s, e) => FlushToDisk();
            _flushTimer.Start();
            _logger.LogInformation($"OutputRecorderDevice initialized. Output file: {_outputFilePath}");
        }

        public override string DefaultVariant()
            => Variants.FirstOrDefault() ?? base.DefaultVariant();
        
        public override async Task PlayGallery(FunscriptGallery gallery, long seek = 0)
        {
            _logger.LogInformation($"PlayGallery called on Recorder: {Name}, Gallery: {gallery?.Name ?? "Unknown"}, Seek: {seek}");
            savePrevius();

            var cmds = gallery?.Commands;
            if (cmds == null)
                return;

            _logger.LogInformation($"PlayGallery finished adding commands for Recorder: {Name}");

            syncPrev = syncPlaybackFactory.Create(gallery.Name , seek);
        }



        public override async Task StopGallery()
        {
            savePrevius();
            _logger.LogInformation($"Stopping gallery playback for Recorder: {Name}");

            await Task.CompletedTask;
        }

        private void savePrevius()
        {
            if (syncPrev == null)
            {
                addNonAction();
                return;
            }

            var gallery = repository.Get(syncPrev.GalleryName, this.selectedVariant);

            if(gallery == null)
            {
                addNonAction();
                return;
            }

            var cmds = gallery.Commands.Where(c => c.AbsoluteTime > syncPrev.Seek);
            if (!cmds.Any() && !syncPrev.IsLoop)
            {
                addNonAction();
                return;
            }

            var millisFrist = cmds.First().AbsoluteTime - syncPrev.Seek;

            scriptBuilder.AddCommandMillis(millisFrist, cmds.First().Value);

            if(cmds.Count() > 1)
                scriptBuilder.addCommands(cmds.Skip(1));

            while (scriptBuilder.TotalTime < syncPrev.PlaybackDuration
                    && syncPrev.IsLoop)
            {
                scriptBuilder.addCommands(gallery.Commands);
            }

            scriptBuilder.CutToTime(syncPrev.PlaybackDuration);

            var offset = Convert.ToInt64((syncPrev.SendTime - _recordingStartTime).TotalMicroseconds);

            var newActiosn = scriptBuilder.Generate(offset)
                                .Select(c => new FunScriptAction
                                {
                                    at = c.AbsoluteTime,
                                    pos = Convert.ToInt32(c.Value)
                                });

            syncPrev = null;
            _actions.AddRange(newActiosn);

            
        }

        private void addNonAction()
        {
            _actions.Add(new()
            {
                at = RecordingAbsolueTime,
                pos = _actions.Last().pos
            });
        }

        private void FlushToDisk()
        {
            if (_isFlushing) return;
            _isFlushing = true;
            try
            {
                List<FunScriptAction> newActions;
                FunScriptAction lastAction = null;
                lock (_lock)
                {
                    if (_actions.Count == 0 || _actions.Count == 1)
                    {
                        _isFlushing = false;
                        return;
                    }
                    newActions = _actions.Take(_actions.Count-1).ToList();
                    lastAction = _actions.Last();
                    _actions.Clear();
                    if (lastAction != null)
                    {
                        _actions.Add(lastAction);
                    }
                }

                if (!File.Exists(_outputFilePath))
                {
                    var initialFunscript = new FunScriptFile { actions = new List<FunScriptAction>()};
                    var initialJson = JsonConvert.SerializeObject(initialFunscript, Formatting.Indented);
                    File.WriteAllText(_outputFilePath, initialJson);
                    _lastWritePosition = initialJson.LastIndexOf("]");
                    _postActionsContent = initialJson.Substring(_lastWritePosition);
                }
                else if (_lastWritePosition == 0)
                {
                    // Solo la primera vez: leer el archivo y buscar el último elemento con regex
                    string content = File.ReadAllText(_outputFilePath);
                    var match = Regex.Match(content, @"actions""\s*:\s*\[(.*)\](.*)}", RegexOptions.Singleline);
                    if (match.Success)
                    {
                        // match.Groups[1] = contenido del array, match.Groups[2] = posterior
                        int arrayStart = content.IndexOf("[", content.IndexOf("actions"));
                        int arrayEnd = content.LastIndexOf("]");
                        _lastWritePosition = arrayEnd;
                        _postActionsContent = content.Substring(arrayEnd);
                    }
                    else
                    {
                        // fallback
                        _lastWritePosition = content.LastIndexOf("]");
                        _postActionsContent = content.Substring(_lastWritePosition);
                    }
                }

                // Escribir solo los nuevos actions en la posición guardada
                using (var fs = new FileStream(_outputFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Position = _lastWritePosition;
                    using (var writer = new StreamWriter(fs))
                    {
                        bool needsComma = false;
                        // Detectar si ya hay al menos un elemento en el array
                        if (_lastWritePosition > 0)
                        {
                            // Leer el carácter anterior para ver si es '[' o ',')
                            fs.Position = _lastWritePosition - 1;
                            int prevChar = fs.ReadByte();
                            if (prevChar != '[') // Si no es el inicio del array, hay elementos previos
                                needsComma = true;
                            fs.Position = _lastWritePosition;
                        }
                        for (int i = 0; i < newActions.Count; i++)
                        {
                            if (needsComma) writer.Write(",");
                            writer.Write(JsonConvert.SerializeObject(newActions[i]));
                            needsComma = true;
                        }
                        writer.Write(_postActionsContent);
                        writer.Flush();
                        _lastWritePosition = (int)fs.Position - _postActionsContent.Length;
                    }
                }

                _logger.LogInformation($"Appended {newActions.Count} new actions to {_outputFilePath} (regex/seek optimized)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to flush session recording to {_outputFilePath}");
            }
            finally
            {
                _isFlushing = false;
            }
        }
    }
}