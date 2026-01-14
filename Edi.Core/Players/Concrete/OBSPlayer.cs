using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Services;
using Newtonsoft.Json;
using PropertyChanged;
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
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class OBSConfig
    {
        public string wsUrl { get; set; } = "ws://127.0.0.1:4455";
    }

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
            public string outputState;
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

        private DateTime recordingStartTime;
        public long RecordingAbsolueTime => (long)(DateTime.Now - recordingStartTime).TotalMilliseconds;

        private ClientWebSocket ws;
        private CancellationTokenSource cts;
        private Task _receiveLoop;
        private Uri obsUrl => new Uri(config?.wsUrl ?? "ws://127.0.0.1:4455");

        private bool recording = false;
        private RecordingChapter currentChapter = null;
        private readonly Dictionary<string, List<RecordingChapter>> recordingChapters = new();
        private FunScriptFile currentScript;

        private readonly EdiConfig ediConfig;
        private readonly OBSConfig config;
        private readonly PlayerLogService logService;

        private bool isConnected => ws != null && ws.State == WebSocketState.Open;


        public OBSPlayer(DevicePlayer dp, ConfigurationManager cfg, PlayerLogService logService)
            : base(dp)
        {
            this.logService = logService;
            ediConfig = cfg.Get<EdiConfig>();
            config = cfg.Get<OBSConfig>();

            if (ediConfig.UseObsChapterGenerator == false)
                return;

            _ = ConnectAsync();
        }


        public override async Task Play(string name, long seek = 0)
        {
            if (ediConfig.UseObsChapterGenerator == false)
                return;

            if (isConnected == false && ediConfig.UseObsChapterGenerator)
            {
                await ConnectAsync();
                if (isConnected == false)
                    return;

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

            SaveChapter();

            var startTime = recordingTime - seek;
            logService.AddLog($"Starting new chapter {scriptName} at {startTime}ms");
            currentChapter = new RecordingChapter
            {
                name = scriptName,
                startTime = startTime,
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
            if (ediConfig.UseObsChapterGenerator == false)
                return;

            if (!recording)
            {
                logService.AddLog("OBS: Not recording, skipping chapter record");
                return;
            }

            SaveChapter();
        }

        private void SaveChapter()
        {
            if (currentChapter != null)
            {
                currentChapter.endTime = RecordingAbsolueTime + 500; // add 500ms buffer
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
                while (!ct.IsCancellationRequested && isConnected)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        logService.AddLog("OBS WS closed by server");
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", ct);
                        recording = false;
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
                            case "OBS_WEBSOCKET_OUTPUT_STARTED":
                                logService.AddLog("OBS: Recording started");

                                recordingStartTime = DateTime.Now;
                                recording = true;
                                recordingChapters.Clear();
                                currentChapter = null;

                                currentScript = new();

                                break;
                            case "OBS_WEBSOCKET_OUTPUT_STOPPED":
                                var videoPath = data.outputPath;
                                logService.AddLog("OBS: Recording stopped");
                                if (videoPath != null && currentScript != null && recording)
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
            if (isConnected == false)
            {
                logService.AddLog("OBS WS not connected");
                _ = ConnectAsync();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private async Task ConnectAsync()
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();

            ws = new ClientWebSocket();
            logService.AddLog($"Attempting to connect to {obsUrl}");
            try
            {
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    await ws.ConnectAsync(obsUrl, timeoutCts.Token);
                }
                logService.AddLog("OBS WS connected");

                logService.AddLog("Starting ReceiveLoop");
                _receiveLoop = Task.Run(async () =>
                {
                    try
                    {
                        await ReceiveLoop(cts.Token);
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