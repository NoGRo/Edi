using Buttplug.Core.Messages;
using Edi.Core.Device;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
using MQTTnet;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;


namespace Edi.Core.Device.Mqtt
{
    public class MqttDevice : DeviceBase<FunscriptRepository, FunscriptGallery>
    {
        private readonly MqttClient mqttClient;
        private readonly string topic;
        public int CurrentCmdTime => CurrentCmd == null
                    ? 0
                    : Math.Min(CurrentCmd.Millis, Convert.ToInt32(CurrentTime - (CurrentCmd.AbsoluteTime - CurrentCmd.Millis)));
        public int ReminingCmdTime => CurrentCmd == null
                    ? 0
                    : Math.Max(0, Convert.ToInt32(CurrentCmd.AbsoluteTime - CurrentTime));
        private CmdLinear _currentCmd;

        public CmdLinear CurrentCmd
        {
            get => _currentCmd;
            set => Interlocked.Exchange(ref _currentCmd, value);
        }
        public int currentCmdIndex { get; set; }
        public MqttDevice(FunscriptRepository repository, MqttClient mqttClient, string topic, ILogger logger) : base(repository, logger)
        {
            this.mqttClient = mqttClient;
            this.topic = topic;
            Name = topic;
        }

        internal override Task applyRange()
        {
            _ = send("range", new Range(Min, Max), false);
            return Task.CompletedTask;
        }
        internal override void SetVariant()
        {
            _ = send("variant", selectedVariant, false);
        }
        public override Task PlayGallery(string name, long seek = 0)
        {
            var task = base.PlayGallery(name, seek);
            _ = send("play", new Play(name, seek, selectedVariant));
            return task;
        }
        public override async Task PlayGallery(FunscriptGallery gallery, long seek = 0)
        {
            var cmds = gallery?.Commands;
            if (cmds == null) return;

            currentCmdIndex = Math.Max(0, cmds.FindIndex(x => x.AbsoluteTime > CurrentTime));

            while (currentCmdIndex >= 0 && currentCmdIndex < cmds.Count)
            {
                CurrentCmd = cmds[currentCmdIndex];
                //CurrentCmd.Sent = DateTime.Now;

                try
                {
                    await send("command", new Command(CurrentCmd.Millis, CurrentCmd.GetValueInRange(Min, Max)));
                    // Usa el nuevo token de cancelación aquí
                    await Task.Delay(Math.Max(0, ReminingCmdTime), playCancelTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    return; // Salimos de la función si la tarea fue cancelada.
                }

                currentCmdIndex = cmds.FindIndex(currentCmdIndex, x => x.AbsoluteTime > CurrentTime);
                if (currentCmdIndex != -1)
                    continue;

                currentCmdIndex = cmds.FindIndex(x => x.AbsoluteTime > CurrentTime);
                if (currentCmdIndex < 0)
                    break; // Si aún así no hay más comandos, sale del bucle.
            }
        }

        public override async Task StopGallery()
        {
            await send("stop", "stop");
        }

        private async Task send(string topic, object payload, bool defaultToken = true)
        {
            await mqttClient.PublishAsync(new()
            {
                Topic = this.topic + topic,
                Payload = new ReadOnlySequence<byte>(JsonSerializer.SerializeToUtf8Bytes(payload))
            }, playCancelTokenSource.Token);
        }
        private record Play(string gallery, long seek, string variant);
        private record Command(long millis, int value);
        private record Range(int min, int max);
    }
}
