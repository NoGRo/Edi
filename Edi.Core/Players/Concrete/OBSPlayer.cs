using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Edi.Core.Players
{

    public class OBSPlayer : ProxyPlayer
    {
        private class ObsMessage
        {
            public int op;
            public ObsData d;
        }

        private class ObsData
        {
            public ObsEventData eventData;
        }

        private class ObsEventData
        {
            public ObsOutputState outputState;
            public string outputPath;
        }

        public class Op0Response
        {
            public int op = 1;
            public Op0ResponseData d = new();
        }

        public class Op0ResponseData
        {
            public int rpcVersion = 1;
        }

        private enum ObsOutputState
        {
            OBS_WEBSOCKET_OUTPUT_STARTING,
            OBS_WEBSOCKET_OUTPUT_STARTED,
            OBS_WEBSOCKET_OUTPUT_STOPPING,
            OBS_WEBSOCKET_OUTPUT_STOPPED
        }

        private class RecordingChapter
        {
            public string name;
            public long startTime;
            public long endTime;
            public long seekStartTime;
            public long RecordingLength
            {
                get { return endTime - seekStartTime; }
            }
        }

        private DateTime _recordingStartTime;
        public long RecordingAbsolueTime => (long)(DateTime.Now - _recordingStartTime).TotalMilliseconds;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _receiveLoop;
        private readonly Uri _obsUrl = new("ws://127.0.0.1:4455");

        private bool recording = false;
        private RecordingChapter currentChapter = null;
        private readonly Dictionary<string, List<RecordingChapter>> recordingChapters = new();
        private FunScriptFile currentScript;

        private readonly EdiConfig config;
        private readonly PlayerLogService logService;


        public string Channel { get; set; }
        public event IEdi.ChangeStatusHandler OnChangeStatus;

        public OBSPlayer(DevicePlayer dp, ConfigurationManager cfg, PlayerLogService logService)
            : base(dp)
        {
            this.logService = logService;
            config = cfg.Get<EdiConfig>();

            _ = ConnectAsync();
        }


        public override async Task Play(string name, long seek = 0)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
            {
                await ConnectAsync();
            }

            var scriptName = name;
            logService.AddLog($"OBS: Play script {scriptName} seeking to {seek}ms");

            if (!recording)
            {
                logService.AddLog("OBS: Not recording, skipping chapter record");
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var recordingTime = RecordingAbsolueTime;

            if (currentChapter != null)
            {
                currentChapter.endTime = recordingTime + 500; // add 500ms buffer
                logService.AddLog(
                    $"Saving chapter {currentChapter.name}: {currentChapter.startTime}(seekTime {currentChapter.seekStartTime}) -> {currentChapter.endTime} ({currentChapter.RecordingLength}ms)"
                );
                if (!recordingChapters.ContainsKey(currentChapter.name))
                {
                    recordingChapters[currentChapter.name] = new List<RecordingChapter>();
                }
                recordingChapters[currentChapter.name].Add(currentChapter);
            }

            logService.AddLog($"Starting new chapter {scriptName}");
            currentChapter = new RecordingChapter
            {
                name = scriptName,
                startTime = recordingTime - seek,
                seekStartTime = recordingTime,
            };

            var overlappingChapter = recordingChapters
                .Values.FirstOrDefault(l => l.Find(c => c.endTime > currentChapter.startTime) != null)
                ?.Find(c => c.endTime > currentChapter.startTime);

            if (overlappingChapter != null && overlappingChapter.name != scriptName)
            {
                logService.AddLog(
                    $"{scriptName}({currentChapter.startTime}ms) overlaps with previous chapter {overlappingChapter.name}({overlappingChapter.startTime} -> {overlappingChapter.endTime} ({overlappingChapter.RecordingLength}ms))"
                );
                overlappingChapter.endTime = currentChapter.startTime;
            }
        }


        public override async Task Stop()
        {
            logService.AddLog("Stop script");
            if (!recording)
            {
                logService.AddLog("OBS: Not recording, skipping chapter record");
                return;
            }
            var recordingTime = RecordingAbsolueTime;

            if (currentChapter != null)
            {
                currentChapter.endTime = recordingTime + 500; // add 500ms buffer
                logService.AddLog(
                    $"Saving chapter {currentChapter.name}: {currentChapter.startTime}(seekTime {currentChapter.seekStartTime}) -> {currentChapter.endTime} ({currentChapter.RecordingLength}ms)"
                );
                if (!recordingChapters.ContainsKey(currentChapter.name))
                {
                    recordingChapters[currentChapter.name] = new List<RecordingChapter>();
                }
                recordingChapters[currentChapter.name].Add(currentChapter);
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[8192];
            try
            {
                while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        logService.AddLog("OBS WS closed by server");
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", ct);
                        break;
                    }

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = JsonConvert.DeserializeObject<ObsMessage>(msg);

                    var op = message.op;

                    if (op == 0)
                    {
                        var response = new Op0Response();
                        var json = JsonConvert.SerializeObject(response);
                        _ = SendAsync(json, ct);
                    }

                    if (op == 5)
                    {
                        var data = message.d.eventData;

                        switch (data.outputState)
                        {
                            case ObsOutputState.OBS_WEBSOCKET_OUTPUT_STARTED:
                                logService.AddLog("OBS: Recording started");

                                _recordingStartTime = DateTime.Now;
                                recording = true;
                                recordingChapters.Clear();
                                currentChapter = null;

                                currentScript = new();

                                break;
                            case ObsOutputState.OBS_WEBSOCKET_OUTPUT_STOPPED:
                                var videoPath = data.outputPath;
                                logService.AddLog("OBS: Recording stopped");
                                if (videoPath != null && currentScript != null)
                                {
                                    logService.AddLog("OBS: Finalizing script...");
                                    foreach (var chapters in recordingChapters.Values)
                                    {
                                        var chapter = chapters
                                            .OrderByDescending(chapter => chapter.RecordingLength)
                                            .FirstOrDefault();

                                        currentScript.metadata.chapters.Add(
                                            new FunScriptChapter
                                            {
                                                name = chapter.name,
                                                startTime = FormatMs(chapter.startTime),
                                                endTime = FormatMs(chapter.endTime),
                                            }
                                        );

                                        currentScript.actions.Add(
                                            new FunScriptAction
                                            {
                                                at = chapter.startTime,
                                                pos = 0
                                            }
                                        );
                                        currentScript.actions.Add(
                                            new FunScriptAction
                                            {
                                                at = chapter.endTime,
                                                pos = 0
                                            }
                                        );
                                    }

                                    var scriptPath = Path.ChangeExtension(
                                        videoPath,
                                        ".funscript"
                                    );
                                    currentScript.Save(scriptPath);
                                    logService.AddLog($"Saved Script to {scriptPath}");

                                    recording = false;
                                }
                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logService.AddLog($"OBS WS receive error: {ex.Message}: {ex.StackTrace}");
            }
        }

        private async Task SendAsync(string json, CancellationToken ct)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
            {
                logService.AddLog("OBS WS not connected");
                _ = ConnectAsync();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private async Task ConnectAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _ws = new ClientWebSocket();
            logService.AddLog($"Attempting to connect to {_obsUrl}");
            try
            {
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    await _ws.ConnectAsync(_obsUrl, timeoutCts.Token);
                }
                logService.AddLog("OBS WS connected");

                logService.AddLog("Starting ReceiveLoop");
                _receiveLoop = Task.Run(async () =>
                {
                    try
                    {
                        await ReceiveLoop(_cts.Token);
                    }
                    catch (Exception ex)
                    {
                        logService.AddLog($"OBS WS ReceiveLoop error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                logService.AddLog($"OBS WS connect failed: {ex.Message}");
            }
        }

        private string FormatMs(long milliseconds)
        {
            var ts = TimeSpan.FromMilliseconds(milliseconds);
            var hours = (long)ts.TotalHours;
            return $"{hours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }
    }
}