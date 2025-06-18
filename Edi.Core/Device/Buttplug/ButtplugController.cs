using Buttplug.Client;
using Buttplug.Core.Messages;
using System;
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
        private CancellationTokenSource globalCts;
        private Task globalDeviceTask;
        private readonly ILogger _logger;
        private readonly object _lock = new();

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

        // Método auxiliar para evitar repeticiones
        private static bool IsValidActuator(ButtplugDevice x) => x.Actuator is ActuatorType.Vibrate or ActuatorType.Oscillate;

        private void DeviceCollector_OnUnloadDevice(IDevice device, List<IDevice> devices)
        {
            lock (_lock)
            {
                if (!devices.OfType<ButtplugDevice>().Any(IsValidActuator))
                {
                    globalCts?.Cancel(true);
                    globalCts = null;
                    globalDeviceTask = null;
                    _logger.LogInformation($"No remaining devices of correct actuator type. Global device thread stopped.");
                }
            }
        }

        private void DeviceCollector_OnloadDevice(IDevice device, List<IDevice> devices)
        {
            lock (_lock)
            {
                if (device is ButtplugDevice && devices.OfType<ButtplugDevice>().Any(IsValidActuator)
                    && (globalDeviceTask == null || globalDeviceTask.IsCompleted))
                {
                    globalCts = new CancellationTokenSource();
                    globalDeviceTask = Task.Factory.StartNew(async () => await ExecuteDeviceCommandsAsync(globalCts.Token), TaskCreationOptions.LongRunning);
                }
            }
        }

        private async Task ExecuteDeviceCommandsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Agrupar todos los ButtplugDevice por su ButtplugClientDevice y ActuatorType
                var allDevices = deviceCollector.Devices.OfType<ButtplugDevice>().Where(IsValidActuator).ToList();
                var grouped = allDevices
                    .GroupBy(x => (x.Device, x.Actuator))
                    .ToList();

                var nextDelay = DelayMin;

                foreach (var group in grouped)
                {
                    var clientDevice = group.Key.Device;
                    var actuator = group.Key.Actuator;
                    // Obtener todos los canales para este device y actuator
                    var channels = group.Select(x => x.DeviceChannel).Distinct().OrderBy(x => x).ToList();
                    // Construir el array de valores para todos los canales
                    var values = new double[channels.Max() + 1];
                    foreach (var channel in channels)
                    {
                        var dev = group.FirstOrDefault(x => x.DeviceChannel == channel);
                        if (dev == null || dev.IsPause || dev.CurrentCmd == null)
                            values[channel] = 0;
                        else
                            values[channel] = dev.CalculateSpeed().Speed;

                        // Calcular el menor RemainingTime para el delay
                        if (dev != null && !dev.IsPause && dev.CurrentCmd != null)
                        {
                            var remaining = dev.CalculateSpeed().TimeUntilNextChange;
                            if (remaining / DelayMin < 2)
                                nextDelay = Math.Max(DelayMin, remaining);
                        }
                    }
                    // Solo enviar si hay cambios respecto al último comando
                    if (!lastCommands.TryGetValue(clientDevice, out var lastCmd) ||
                        lastCmd.Item1 != actuator ||
                        !lastCmd.Item2.SequenceEqual(values.Select((v, i) => ((uint)i, v)), CmdComparer.Instance))
                    {
                        await SendCommandAsync(clientDevice, actuator, values.Select((v, i) => ((uint)i, v)));
                        lastCommands[clientDevice] = (actuator, values.Select((v, i) => ((uint)i, v)).ToArray());

                        _logger.LogInformation($"Sending command to {clientDevice.Name} - Actuator: {actuator}, Values: {string.Join(", ", values)}.");
                    }
                }
                try
                {
                    await Task.Delay(nextDelay, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
            _logger.LogInformation($"Global command execution loop ended.");
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
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending command to {device.Name} - Actuator: {actuator}: {ex.Message}");
            }
        }

        private class CmdComparer : IEqualityComparer<(uint Channel, double Speed)>
        {
            public static readonly CmdComparer Instance = new CmdComparer();
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
