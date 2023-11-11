using Buttplug.Client;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using NAudio.CoreAudioApi;
using System;
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

        public ButtplugController(ButtplugConfig config, DeviceManager deviceManager)
        {
            this.config = config;
            this.deviceManager = deviceManager;
           
            Task.Run(ExecuteAsync);
        }

        private async Task ExecuteAsync()
        {
            while (true)
            {
                var commands = deviceManager.Devices
                                            .OfType<ButtplugDevice>()
                                            .Where(x => x != null && (x.Actuator is ActuatorType.Vibrate or ActuatorType.Oscillate) && !x.IsPause)
                                            .SelectMany(x => new[] { (x.Device, x.Actuator, Cmd: (x.Channel, x.CalculateSpeed())) })
                                            .GroupBy(x => new { x.Device, x.Actuator })
                                            .ToDictionary(g => g.Key, g => g.Select(x => x.Cmd).ToList());

                foreach (var command in commands)
                {
                    if (!lastCommands.TryGetValue(command.Key.Device, out var lastCommand) 
                        || lastCommand != (command.Key.Actuator, command.Value))
                    {
                        switch (command.Key.Actuator)
                        {
                            case ActuatorType.Vibrate:
                                await command.Key.Device.VibrateAsync(command.Value);
                                break;
                            case ActuatorType.Oscillate:
                                await command.Key.Device.OscillateAsync(command.Value);
                                break;
                        }
                        lastCommands[command.Key.Device] = (command.Key.Actuator, command.Value);
                    }
                }

                await Task.Delay(Delay);
            }
        }

        public int Delay => config.CommandDelay;
    }
}
