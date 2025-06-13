using Buttplug.Client;
using Buttplug.Core.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;

namespace Edi.Core.Device.Buttplug
{
    public class ButtplugController
    {
        private readonly ButtplugConfig config;
        private readonly DeviceCollector deviceCollector;
        private readonly Dictionary<ButtplugClientDevice, (ActuatorType, ICollection<(uint, double)>)> lastCommands = new();
        private readonly ConcurrentDictionary<ButtplugClientDevice, CancellationTokenSource> customDelayDevices = new();
        private readonly ILogger _logger;

        public int DelayMin => config.MinCommandDelay;

        public ButtplugController(ButtplugConfig config, DeviceCollector deviceCollector, ILogger logger)
        {
            this.config = config;
            this.deviceCollector = deviceCollector;
            _logger = logger;

            deviceCollector.OnloadDevice += DeviceCollector_OnloadDevice;
            deviceCollector.OnUnloadDevice += DeviceCollector_OnUnloadDevice;

            _logger.LogInformation("ButtplugController initialized.");
        }

        private void DeviceCollector_OnUnloadDevice(IDevice device, List<IDevice> devices)
        {
            if (device is ButtplugDevice buttplugDevice && customDelayDevices.TryRemove(buttplugDevice.Device, out var cts))
            {
                cts.Cancel();
                _logger.LogInformation($"Device unloaded: {buttplugDevice.Name}.");
            }
        }

        private void DeviceCollector_OnloadDevice(IDevice device, List<IDevice> devices)
        {
            if (device is ButtplugDevice buttplugDevice && buttplugDevice.Actuator is ActuatorType.Vibrate or ActuatorType.Oscillate)
            {
                var cts = new CancellationTokenSource();
                if (customDelayDevices.TryAdd(buttplugDevice.Device, cts))
                {
                    var delay = buttplugDevice.Device.MessageTimingGap != 0
                        ? buttplugDevice.Device.MessageTimingGap
                        : Convert.ToUInt16(DelayMin);

                    _logger.LogInformation($"Device loaded: {buttplugDevice.Name}, Actuator: {buttplugDevice.Actuator}, Delay: {delay}ms.");
                    Task.Factory.StartNew(async () => await ExecuteDeviceCommandsAsync(buttplugDevice, delay, cts.Token), TaskCreationOptions.LongRunning);
                }
            }
        }

        private async Task ExecuteDeviceCommandsAsync(ButtplugDevice device, uint delay, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Starting command execution loop for device: {device.Name} with delay: {delay}ms.");

            while (!cancellationToken.IsCancellationRequested)
            {
                var commands = deviceCollector.Devices
                    .OfType<ButtplugDevice>()
                    .Where(x => x.Device == device.Device && !x.IsPause && x.CurrentCmd != null)
                    .Select(x => new
                    {
                        x.Device,
                        x.Actuator,
                        RemainingTime = x.CalculateSpeed().TimeUntilNextChange,
                        Cmd = (x.DeviceChannel, x.CalculateSpeed().Speed)
                    })
                    .GroupBy(x => new { x.Device, x.Actuator })
                    .ToImmutableDictionary(
                        g => g.Key,
                        g => new
                        {
                            Commands = g.OrderBy(x => x.Cmd.DeviceChannel).Select(x => x.Cmd).ToImmutableArray(),
                            RemainingTime = g.Min(x => x.RemainingTime)
                        });

                var nextDelay = DelayMin;

                foreach (var command in commands)
                {
                    var cmdValue = command.Value;

                    if (!lastCommands.TryGetValue(command.Key.Device, out var lastCommand)
                        || AreCommandsDifferent(lastCommand.Item2, cmdValue.Commands))
                    {
                        _ = SendCommandAsync(command.Key.Device, command.Key.Actuator, cmdValue.Commands);
                        lastCommands[command.Key.Device] = (command.Key.Actuator, cmdValue.Commands);

                        _logger.LogInformation($"Sending command to {command.Key.Device.Name} - Actuator: {command.Key.Actuator}, Commands: {string.Join(", ", cmdValue.Commands)}.");
                    }

                    if (cmdValue.RemainingTime / DelayMin < 2)
                        nextDelay = Math.Max(DelayMin, cmdValue.RemainingTime);
                }

                await Task.Delay(nextDelay, cancellationToken);
            }

            _logger.LogInformation($"Command execution loop ended for device: {device.Name}.");
        }

        private bool AreCommandsDifferent(ICollection<(uint Channel, double Speed)> lastCmds, ICollection<(uint Channel, double Speed)> newCmds)
        {
            if (lastCmds == null || newCmds == null || lastCmds.Count != newCmds.Count)
                return true;

            return !lastCmds.SequenceEqual(newCmds, new CmdComparer());
        }

        private async Task SendCommandAsync(ButtplugClientDevice device, ActuatorType actuator, IEnumerable<(uint, double)> commands)
        {
            try
            {
                switch (actuator)
                {
                    case ActuatorType.Vibrate:
                        await device.VibrateAsync(commands.Select(x => x.Item2));
                        break;
                    case ActuatorType.Oscillate:
                        await device.OscillateAsync(commands.Select(x => x.Item2));
                        break;
                }

                //_logger.LogInformation($"Command sent to {device.Name} - Actuator: {actuator}, Values: {string.Join(", ", commands.Select(x => x.Item2))}.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending command to {device.Name} - Actuator: {actuator}: {ex.Message}");
            }
        }

        private class CmdComparer : IEqualityComparer<(uint Channel, double Speed)>
        {
            public bool Equals((uint Channel, double Speed) x, (uint Channel, double Speed) y)
            {
                return x.Channel == y.Channel && Math.Abs(x.Speed - y.Speed) < 0.001;
            }

            public int GetHashCode((uint Channel, double Speed) obj)
            {
                return obj.Channel.GetHashCode() ^ obj.Speed.GetHashCode();
            }
        }
    }
}
