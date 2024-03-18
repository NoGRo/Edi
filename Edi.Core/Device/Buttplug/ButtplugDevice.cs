using Buttplug.Client;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using PropertyChanged;
using System.Runtime.InteropServices;
using System.Timers;
using Timer = System.Timers.Timer;
using System;
using System.Diagnostics;



namespace Edi.Core.Device.Buttplug
{


    //OSR6 I don't know if you can use this class because it is all designed for a single stream to go under bluetooth it has the rate limit other things
    [AddINotifyPropertyChangedInterface]
    public class ButtplugDevice : DeviceBase<FunscriptRepository, FunscriptGallery>
    {
        public ButtplugClientDevice Device { get; private set; }
        private ButtplugConfig config { get;  set; }
        public ActuatorType Actuator { get; }
        public uint Channel { get; }

        
        public CmdLinear CurrentCmd { get; set; }
        public int currentCmdIndex { get; set; }
        private DateTime lastCmdSendAt { get;  set; }
        public int CurrentCmdTime => CurrentCmd == null ? 0 : Math.Min(CurrentCmd.Millis, Convert.ToInt32(this.CurrentTime - (CurrentCmd.AbsoluteTime - CurrentCmd.Millis)) );
        public int ReminingCmdTime => CurrentCmd == null ? 0 : Math.Max(0, Convert.ToInt32((CurrentCmd.AbsoluteTime) - this.CurrentTime));
        
        private double vibroSteps;
        private CancellationTokenSource cancelTokenSource = new CancellationTokenSource();

        public ButtplugDevice(ButtplugClientDevice device, ActuatorType actuator, uint channel, FunscriptRepository repository, ButtplugConfig config)
            : base(repository)
        {
            this.Device = device;
            Name = device.Name + (device.GenericAcutatorAttributes(actuator).Count() > 1 
                                    ? $" {actuator}: {channel+1}" 
                                    : "" );
            
            Actuator = actuator;
            Channel = channel;
            var acutators = Device.GenericAcutatorAttributes(Actuator);

            if (acutators.Any())
                vibroSteps = Device.GenericAcutatorAttributes(Actuator)[(int)Channel].StepCount;

            if (vibroSteps == 0)
                vibroSteps = 1;
            vibroSteps = (100.0 / vibroSteps);

            this.config = config;
            
            SelectedVariant = Variants.FirstOrDefault(x => x.Contains(Actuator.ToString(),StringComparison.OrdinalIgnoreCase))
                                ?? Variants.FirstOrDefault("");
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

                var sendtask = SendCmd();


                try
                {
                    await WaitAsync(Math.Max(0, (int)ReminingCmdTime), cancelTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    break; 
                }

                currentCmdIndex = cmds.FindIndex(currentCmdIndex, x => x.AbsoluteTime > CurrentTime);


                if (currentCmdIndex < 0)
                {

                    currentCmdIndex = cmds.FindIndex(x => x.AbsoluteTime > CurrentTime);
                    if (currentCmdIndex < 0) 
                        break; // Si aún así no hay más comandos, sale del bucle.
                }
            }
        }

        public async Task SendCmd()
        {
            if (Device == null)
                return;

            if(CurrentCmd.Millis <= 0)
            {
                await Task.Delay(1);
                return;
            }
            Task sendtask = Task.CompletedTask;
            if (CurrentCmd.Millis >= config.CommandDelay 
                || (DateTime.Now - lastCmdSendAt).TotalMilliseconds >= config.CommandDelay)
            {
                lastCmdSendAt = DateTime.Now;
                switch (Actuator)
                {
                    case ActuatorType.Position:
                        sendtask = Device.LinearAsync(new[] { ((uint)(ReminingCmdTime), CurrentCmd.LinearValue) });
                        break;
                    case ActuatorType.Rotate:
                        sendtask = Device.RotateAsync(Math.Min(1.0, Math.Max(0, CurrentCmd.Speed / (double)450)), CurrentCmd.Direction); ;
                        break;
                }
            }

            await sendtask.ConfigureAwait(false);

        }

        public override async Task StopGallery()
        {
            cancelTokenSource.Cancel();
            cancelTokenSource = new CancellationTokenSource();

            await Device.Stop();
        }

        public (double Speed, int TimeUntilNextChange) CalculateSpeed()
        {
            if (CurrentCmd == null)
                return (0, 0); // Si no hay comando actual, no hay velocidad ni cambio.

            var distanceToTravel = CurrentCmd.Value - CurrentCmd.InitialValue;
            var elapsedFraction = (double)CurrentCmdTime / CurrentCmd.Millis;
            var travel = Math.Round(distanceToTravel * elapsedFraction, 0);
            travel = travel is double.NaN or double.PositiveInfinity or double.NegativeInfinity ? 0 : travel;
            var currVal = Math.Abs(Math.Max(0, Math.Min(100,CurrentCmd.InitialValue + Convert.ToInt16(travel))));

            var speed = (int)Math.Round(currVal / vibroSteps) * vibroSteps;
            speed = Math.Min(1.0, Math.Max(0, speed / (double)100));

            // Calculamos el tiempo hasta el próximo cambio. 
            // Suponemos que el tiempo hasta el próximo cambio es proporcional a la distancia hasta el próximo vibroStep.
            var nextStepDistance = vibroSteps - (currVal % vibroSteps);
            var nextStepFraction = nextStepDistance / distanceToTravel;
            var timeUntilNextChange = (elapsedFraction + nextStepFraction) * CurrentCmd.Millis - CurrentCmdTime;

            // Ajustamos el tiempo para asegurarnos de que no sea negativo ni infinito.
            timeUntilNextChange = timeUntilNextChange < 0 ? ReminingCmdTime : timeUntilNextChange;
            timeUntilNextChange = Math.Min(ReminingCmdTime, timeUntilNextChange);
            return (speed, Convert.ToInt32(timeUntilNextChange));
        }

        private const int FinalWaitThreshold = 20; // Threshold for precise final wait.

        public async Task WaitAsync(double milliseconds, CancellationToken cancellationToken = default)
        {
            if (milliseconds <= 0) return;

            var stopwatch = Stopwatch.StartNew();
            var initialWaitTime = Math.Max(0, milliseconds - FinalWaitThreshold);

            if (initialWaitTime > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(initialWaitTime), cancellationToken);
            }

            while (stopwatch.ElapsedMilliseconds < milliseconds)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }


    }

}

