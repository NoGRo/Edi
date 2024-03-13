using Buttplug.Client;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Edi.Core.Device.Buttplug
{
    public class ButtplugController
    {
        private readonly ButtplugConfig config;
        private readonly DeviceManager deviceManager;
        private readonly Dictionary<ButtplugClientDevice, (ActuatorType, ICollection<(uint, double)>)> lastCommands = new();
        private readonly ConcurrentDictionary<ButtplugClientDevice, CancellationTokenSource> customDelayDevices = new();
        private readonly List<int> SignalQueue = new();
        public int DelayMin => config.CommandDelay;
        public ButtplugController(ButtplugConfig config, DeviceManager deviceManager)
        {
            this.config = config;
            this.deviceManager = deviceManager;
            deviceManager.OnloadDevice += DeviceManager_OnloadDevice; ;
            deviceManager.OnUnloadDevice += DeviceManager_OnUnloadDevice; ;
            
        }



        private void DeviceManager_OnUnloadDevice(IDevice Device)
        {
            ButtplugDevice? buttplugDevice = (Device as ButtplugDevice);
            if (buttplugDevice != null && customDelayDevices.TryRemove(buttplugDevice.Device, out var cts))
            {
                cts.Cancel();
            }
        }

        private void DeviceManager_OnloadDevice(IDevice Device)
        {
            var device = Device as ButtplugDevice;
            if (device != null && device.Actuator is ActuatorType.Vibrate or ActuatorType.Oscillate)
            {
                var cts = new CancellationTokenSource();
                if (customDelayDevices.TryAdd(device.Device, cts))
                {
                    var delay = device.Device.MessageTimingGap != 0 ? device.Device.MessageTimingGap : Convert.ToUInt16(DelayMin);
                    Task.Run(() => ExecuteDeviceCommandsAsync(device, delay, cts.Token));
                }
            }
        }


        private async Task ExecuteDeviceCommandsAsync(ButtplugDevice device, uint delay, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var commands = deviceManager.Devices
                                                .OfType<ButtplugDevice>()
                                                .Where(x => x != null && x.Device == device.Device && !x.IsPause  && x.CurrentCmd != null)
                                                .Select(x => new { x.Device, x.Actuator, x.ReminingTime, Cmd = (x.Channel, x.CalculateSpeed())})
                                                .GroupBy(x => new { x.Device, x.Actuator })
                                                .ToImmutableDictionary(g => g.Key, g => new { cmds = g.Select(x => x.Cmd).ToImmutableArray(), ReminingTime = g.Min(x => x.ReminingTime) });

                var sendTaks = new List<Task>();
                var NextDelay = DelayMin;
                foreach (var command in commands)
                {
                    var cmdValue = command.Value;

                    if (!lastCommands.TryGetValue(command.Key.Device, out var lastCommand)
                        || AreCommandsDifferent(lastCommand.Item2, cmdValue.cmds))
                    {
                        sendTaks.Add(SendCommandAsync(command.Key.Device, command.Key.Actuator, cmdValue.cmds));
                        lastCommands[command.Key.Device] = (command.Key.Actuator, cmdValue.cmds);
                    }

                    if (cmdValue.ReminingTime / DelayMin < 2)
                        NextDelay = Math.Max(DelayMin, cmdValue.ReminingTime);

                    if (sendTaks.Count > 0)
                    {
                        Debug.WriteLine($"Enviando comando a {command.Key.Device} - Actuador: {command.Key.Actuator}, Comandos: {string.Join(", ", cmdValue.cmds)}, NextDelay: {NextDelay}, ReminingTime: {cmdValue.ReminingTime}");
                    }
                }
                await Task.Delay(NextDelay);
            }
            }
            private bool AreCommandsDifferent(ICollection<(uint Channel, double Speed)> lastCmds, ICollection<(uint Channel, double Speed)> newCmds)
            {
                // Early exit para casos básicos
                if (lastCmds == null || newCmds == null || lastCmds.Count != newCmds.Count)
                    return true;

                // Usa SequenceEqual con un comparador personalizado para verificar si las secuencias son diferentes
                return !lastCmds.SequenceEqual(newCmds, new CmdComparer());
            }

            // Comparador personalizado para (uint Channel, double Speed)
            class CmdComparer : IEqualityComparer<(uint Channel, double Speed)>
            {
                public bool Equals((uint Channel, double Speed) x, (uint Channel, double Speed) y)
                {
                    // Considera dos comandos iguales si tanto el Channel como el Speed son iguales
                    return x.Channel == y.Channel && x.Speed == y.Speed;
                }

                public int GetHashCode((uint Channel, double Speed) obj)
                {
                    // Calcula un hash code basado en Channel y Speed
                    return obj.Channel.GetHashCode() ^ obj.Speed.GetHashCode();
                }
            }


        private async Task SendCommandAsync(ButtplugClientDevice device, ActuatorType actuator, IEnumerable<(uint, double)> command)
        {
            switch (actuator)
            {
                case ActuatorType.Vibrate:
                    await device.VibrateAsync(command);
                    break;
                case ActuatorType.Oscillate:
                    await device.OscillateAsync(command);
                    break;
            }
        }

        
    }
}
