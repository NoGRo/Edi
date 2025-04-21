using Buttplug.Client;
using Buttplug.Core.Messages;
using Edi.Core.Funscript;
using PropertyChanged;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;

namespace Edi.Core.Device.Buttplug
{
    // OSR6: I am not sure if this class can be used because it is all designed for a single stream to run under Bluetooth with rate limits and other constraints.
    [AddINotifyPropertyChangedInterface]
    public class ButtplugDevice : DeviceBase<FunscriptRepository, FunscriptGallery>, IRange
    {
        private readonly ILogger _logger;
        public ButtplugClientDevice Device { get; private set; }
        private ButtplugConfig config { get; set; }
        public ActuatorType Actuator { get; }
        public uint Channel { get; }

        private CmdLinear _currentCmd;

        public CmdLinear CurrentCmd
        {
            get => _currentCmd;
            set => Interlocked.Exchange(ref _currentCmd, value);
        }

        private DateTime lastCmdSendAt { get; set; }
        public int currentCmdIndex { get; set; }

        public int CurrentCmdTime
        {
            get
            {
                CmdLinear localCmd = null;
                Interlocked.CompareExchange(ref localCmd, _currentCmd, null);
                return localCmd == null
                    ? 0
                    : Math.Min(localCmd.Millis, Convert.ToInt32(CurrentTime - (localCmd.AbsoluteTime - localCmd.Millis)));
            }
        }

        public int ReminingCmdTime
        {
            get
            {
                CmdLinear localCmd = null;
                Interlocked.CompareExchange(ref localCmd, _currentCmd, null);
                return localCmd == null
                    ? 0
                    : Math.Max(0, Convert.ToInt32(localCmd.AbsoluteTime - CurrentTime));
            }
        }

        public int Min { get; set; }
        public int Max { get; set; } = 100;
        private double vibroSteps;

        private readonly Random RandomRotate = new((int)DateTime.Now.Ticks);
        private bool RotateDirection = true;
        private float? RotateMillisDirChange = null;
        private float RotateTotalMillis = 0;
        private readonly float RotateMinimumMillisDirChange = 500;
        private readonly float RotateMaximumMillisDirChange = 2500;

        public ButtplugDevice(ButtplugClientDevice device, ActuatorType actuator, uint channel, FunscriptRepository repository, ButtplugConfig config, ILogger logger)
            : base(repository, logger)
        {
            _logger = logger;
            Device = device;
            Name = device.Name + (device.GenericAcutatorAttributes(actuator).Count() > 1
                                    ? $" {actuator}: {channel + 1}"
                                    : "");

            Actuator = actuator;
            Channel = channel;
            var acutators = Device.GenericAcutatorAttributes(Actuator);

            if (acutators.Any())
                vibroSteps = Device.GenericAcutatorAttributes(Actuator)[(int)Channel].StepCount;

            if (vibroSteps == 0)
                vibroSteps = 1;
            vibroSteps = 100.0 / vibroSteps;

            this.config = config;
            _logger.LogInformation($"ButtplugDevice initialized with Device: {Name}, Actuator: {Actuator}, Channel: {Channel}");
        }

        public override string DefaultVariant()
        => Variants.FirstOrDefault(x => x.Contains(Actuator.ToString(), StringComparison.OrdinalIgnoreCase))
            ?? base.DefaultVariant();

        public override async Task PlayGallery(FunscriptGallery gallery, long seek = 0)
        {
            _logger.LogInformation($"Starting PlayGallery with Device: {Name}, Gallery: {gallery?.Name ?? "Unknown"}, Seek: {seek}");

            var cmds = gallery?.Commands;
            if (cmds == null) return;

            if (Actuator == ActuatorType.Vibrate)
            {
                SeekTime += config.MotorInercialDelay;
            }

            currentCmdIndex = Math.Max(0, cmds.FindIndex(x => x.AbsoluteTime > CurrentTime));

            while (currentCmdIndex >= 0 && currentCmdIndex < cmds.Count)
            {
                CurrentCmd = cmds[currentCmdIndex];
                //_logger.LogInformation($"Executing command at index: {currentCmdIndex} with AbsoluteTime: {CurrentCmd.AbsoluteTime}");

                var sendtask = SendCmd();

                try
                {
                    // Using the new cancellation token here
                    await Task.Delay(Math.Max(0, ReminingCmdTime), playCancelTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning($"PlayGallery canceled for Device: {Name}");
                    return; // Exit the function if the task was canceled
                }

                currentCmdIndex++;
            }
            _logger.LogInformation($"PlayGallery completed for Device: {Name}");
        }

        public override async Task StopGallery()
        {
            _logger.LogInformation($"Stopping gallery playback for Device: {Name}");
            await Device?.Stop();
        }

        public async Task SendCmd()
        {
            if (Device == null)
            {
                _logger.LogWarning($"Device is null, command not sent.");
                return;
            }

            if (CurrentCmd.Millis <= 0)
            {
                await Task.Delay(1);
                return;
            }

            Task sendtask = Task.CompletedTask;
            if (CurrentCmd.Millis >= config.MinCommandDelay
                || (DateTime.Now - lastCmdSendAt).TotalMilliseconds >= config.MinCommandDelay)
            {
                lastCmdSendAt = DateTime.Now;
                var remainingCmdTime = ReminingCmdTime;
                _logger.LogInformation($"Sending command for Device: {Name}, Actuator: {Actuator}, CurrentCmd: in-{remainingCmdTime} pos-{CurrentCmd.GetValueInRange(Min, Max)} with AbsoluteTime: {CurrentCmd.AbsoluteTime}");

                switch (Actuator)
                {
                    case ActuatorType.Position:
                        sendtask = Device.LinearAsync((uint)remainingCmdTime, Math.Min(1.0, Math.Max(0, CurrentCmd.GetValueInRange(Min, Max) / (double)100)));
                        break;
                    case ActuatorType.Rotate:
                        sendtask = Device.RotateAsync(Math.Min(1.0, Math.Max(0, CurrentCmd.Speed / 450f)), RotateDirection);
                        RotateTotalMillis += remainingCmdTime;

                        if (RotateMillisDirChange == null || RotateTotalMillis >= RotateMillisDirChange)
                        {
                            if (RotateMillisDirChange != null)
                            {
                                RotateTotalMillis = 0;
                                RotateDirection = !RotateDirection;
                            }

                            var next = RandomRotate.NextSingle();
                            RotateMillisDirChange = (1 - next) * RotateMinimumMillisDirChange + next * RotateMaximumMillisDirChange;
                        }

                        break;
                }
            }

            await sendtask.ConfigureAwait(false);
            //_logger.LogInformation($"Command sent successfully for Device: {Name}, Actuator: {Actuator}");
        }

        public (double Speed, int TimeUntilNextChange) CalculateSpeed()
        {
            if (CurrentCmd == null)
            {
                _logger.LogInformation("No current command. Speed calculation returns zero values.");
                return (0, 0); // If no current command, return zero speed and time.
            }

            var initialValue = CurrentCmd.Prev?.GetValueInRange(Min, Max) ?? 0;
            var distanceToTravel = CurrentCmd.GetValueInRange(Min, Max) - initialValue;

            var elapsedFraction = (double)CurrentCmdTime / CurrentCmd.Millis;
            var travel = Math.Round(distanceToTravel * elapsedFraction, 0);
            travel = travel is double.NaN or double.PositiveInfinity or double.NegativeInfinity ? 0 : travel;
            var currVal = Math.Abs(Math.Max(0, Math.Min(100, initialValue + Convert.ToInt16(travel))));

            var speed = (int)Math.Round(currVal / vibroSteps) * vibroSteps;
            speed = Math.Min(1.0, Math.Max(0, speed / 100));

            // Calculate the time until the next change.
            // We assume the time until the next change is proportional to the distance to the next vibroStep.
            var nextStepDistance = vibroSteps - currVal % vibroSteps;
            var nextStepFraction = nextStepDistance / distanceToTravel;
            var timeUntilNextChange = (elapsedFraction + nextStepFraction) * CurrentCmd.Millis - CurrentCmdTime;

            // Adjust time to ensure it's not negative or infinite.
            if (timeUntilNextChange < 0)
                timeUntilNextChange = ReminingCmdTime;

            timeUntilNextChange = Math.Min(ReminingCmdTime, timeUntilNextChange);
            //_logger.LogInformation($"Speed calculation for Device: {Name}, Speed: {speed}, TimeUntilNextChange: {Convert.ToInt32(timeUntilNextChange)}");

            return (speed, Convert.ToInt32(timeUntilNextChange));
        }
    }
}
