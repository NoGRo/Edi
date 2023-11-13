using Buttplug.Client;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Edi.Core.Device.Buttplug
{
    public class ButtplugController
    {
        private readonly ButtplugConfig config;
        private readonly DeviceManager deviceManager;
        private readonly Dictionary<ButtplugClientDevice, (ActuatorType, List<(uint, double)>)> lastCommands = new();
        private readonly ConcurrentDictionary<ButtplugClientDevice, CancellationTokenSource> customDelayDevices = new();

        public ButtplugController(ButtplugConfig config, DeviceManager deviceManager)
        {
            this.config = config;
            this.deviceManager = deviceManager;
            deviceManager.OnloadDevice += DeviceManager_OnloadDevice; ;
            deviceManager.OnUnloadDevice += DeviceManager_OnUnloadDevice; ;
            StartDeviceTasks();
        }
        private void StartDeviceTasks()
        {
            // Start a task for handling the rest of the devices with generic delay
            Task.Run(() => ExecuteGenericCommandsAsync());
        }
        private void DeviceManager_OnUnloadDevice(IDevice Device)
        {
            if (customDelayDevices.TryRemove((Device as ButtplugDevice)?.Device, out var cts))
            {
                cts.Cancel();
            }
        }

        private void DeviceManager_OnloadDevice(IDevice Device)
        {
            var device = Device as ButtplugDevice;
            if (device != null && device.Device.MessageTimingGap != 0)
            {
                var cts = new CancellationTokenSource();
                customDelayDevices.TryAdd(device.Device, cts);
                Task.Run(() => ExecuteDeviceCommandsAsync(device, device.Device.MessageTimingGap, cts.Token));
            }
        }


        private async Task ExecuteDeviceCommandsAsync(ButtplugDevice device, uint delay, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!device.IsPause)
                {
                    var command = new { device.Actuator, Cmd = (device.Channel, device.CalculateSpeed()) };
                    await SendCommandAsync(device.Device, command.Actuator, new List<(uint, double)> { command.Cmd });
                }
                await Task.Delay((int)delay, cancellationToken);
            }
        }

        private async Task ExecuteGenericCommandsAsync()
        {
            while (true)
            {
                var commands = deviceManager.Devices
                                            .OfType<ButtplugDevice>()
                                            .Where(x => x != null && x.Device.MessageTimingGap == 0 && !x.IsPause)
                                            .Select(x => new { x.Device, x.Actuator, Cmd = (x.Channel, x.CalculateSpeed()) })
                                            .GroupBy(x => new { x.Device, x.Actuator })
                                            .ToDictionary(g => g.Key, g => g.Select(x => x.Cmd).ToList());

                var sendTaks =  new List<Task>();
                foreach (var command in commands)
                {
                    
                    if (!lastCommands.TryGetValue(command.Key.Device, out var lastCommand) || lastCommand != (command.Key.Actuator, command.Value))
                    {
                        sendTaks.Add(SendCommandAsync(command.Key.Device, command.Key.Actuator, command.Value));
                        lastCommands[command.Key.Device] = (command.Key.Actuator, command.Value);
                        //await Task.Delay(Delay);
                    }
                }
                await Task.Delay(Delay);

            }
        }



        private async Task SendCommandAsync(ButtplugClientDevice device, ActuatorType actuator, List<(uint, double)> command)
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

        public int Delay => config.CommandDelay;
    }
}
