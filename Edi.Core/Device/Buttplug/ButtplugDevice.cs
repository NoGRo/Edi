using Buttplug.Client;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using PropertyChanged;
using Edi.Core.Gallery.Funscript;



namespace Edi.Core.Device.Buttplug
{


    //OSR6 I don't know if you can use this class because it is all designed for a single stream to go under bluetooth it has the rate limit other things
    [AddINotifyPropertyChangedInterface]
    public class ButtplugDevice : DeviceBase<FunscriptRepository, FunscriptGallery>, IRange
    {
        public ButtplugClientDevice Device { get; private set; }
        private ButtplugConfig config { get;  set; }
        public ActuatorType Actuator { get; }
        public uint Channel { get; }

        private CmdLinear _currentCmd;

        public CmdLinear CurrentCmd
        {
            get => _currentCmd;
            set => Interlocked.Exchange(ref _currentCmd, value);
        }
        private DateTime lastCmdSendAt { get;  set; }
        public int currentCmdIndex { get; set; }
        public int CurrentCmdTime
        {
            get
            {
                CmdLinear localCmd = null;
                Interlocked.CompareExchange(ref localCmd, _currentCmd, null);
                return localCmd == null
                    ? 0
                    : Math.Min(localCmd.Millis, Convert.ToInt32(this.CurrentTime - (localCmd.AbsoluteTime - localCmd.Millis)));
            }
        }
        public int RemainingCmdTime 
        {
            get
            {
                CmdLinear localCmd = null;
                Interlocked.CompareExchange(ref localCmd, _currentCmd, null);
                return localCmd == null 
                    ? 0 
                    : Math.Max(0, Convert.ToInt32(localCmd.AbsoluteTime - this.CurrentTime));
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
            
        }

        public override string ResolveDefaultVariant()
        => Variants.FirstOrDefault(x => x.Contains(Actuator.ToString(), StringComparison.OrdinalIgnoreCase))
            ?? base.ResolveDefaultVariant();
        
        public override async Task PlayGallery(FunscriptGallery gallery, long seek = 0)
        {

            var cmds = gallery?.Commands;
            if (cmds == null) return;

            if (Actuator == ActuatorType.Vibrate)
            {
                SeekTime += config.MotorInercialDelay;
            }

            currentCmdIndex = Math.Max(0, cmds.FindIndex(x => x.AbsoluteTime > CurrentTime));

            while (currentCmdIndex >= 0 && currentCmdIndex < cmds.Count)
            {
                CurrentCmd =  cmds[currentCmdIndex];
                //CurrentCmd.Sent = DateTime.Now;

                var sendtask = SendCmd();

                try
                {
                    // Usa el nuevo token de cancelación aquí
                    await Task.Delay(Math.Max(0, RemainingCmdTime), playCancelTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    return; // Salimos de la función si la tarea fue cancelada.
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
        public override async Task StopGallery()
        {
            await Device?.Stop() ;
        }

        public async Task SendCmd()
        {
            if (Device == null)
                return;

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
                switch (Actuator)
                {
                    case ActuatorType.Position:
                        sendtask = Device.LinearAsync(new[] { ((uint)(RemainingCmdTime), Math.Min(1.0, Math.Max(0, CurrentCmd.GetValueInRange(Min,Max) / 100f))) });
                        break;
                    case ActuatorType.Rotate:
                        sendtask = Device.RotateAsync(Math.Min(1.0, Math.Max(0, CurrentCmd.Speed / 400f)), RotateDirection);
                        RotateTotalMillis += RemainingCmdTime;

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

        }



        public (double Speed, int TimeUntilNextChange) CalculateSpeed()
        {

            if (CurrentCmd == null)
                return (0, 0); // Si no hay comando actual, no hay velocidad ni cambio.

            var initialValue = CurrentCmd.Prev?.GetValueInRange(Min, Max) ?? 0;
            var distanceToTravel = CurrentCmd.GetValueInRange(Min, Max) - initialValue;

            var elapsedFraction = (double)CurrentCmdTime / CurrentCmd.Millis;
            var travel = Math.Round(distanceToTravel * elapsedFraction, 0);
            travel = travel is double.NaN or double.PositiveInfinity or double.NegativeInfinity ? 0 : travel;
            var currVal = Math.Abs(Math.Max(0, Math.Min(100, initialValue + Convert.ToInt16(travel))));

            var speed = (int)Math.Round(currVal / vibroSteps) * vibroSteps;
            speed = Math.Min(1.0, Math.Max(0, speed / (double)100));

            // Calculamos el tiempo hasta el próximo cambio. 
            // Suponemos que el tiempo hasta el próximo cambio es proporcional a la distancia hasta el próximo vibroStep.
            var nextStepDistance = vibroSteps - (currVal % vibroSteps);
            var nextStepFraction = nextStepDistance / distanceToTravel;
            var timeUntilNextChange = (elapsedFraction + nextStepFraction) * CurrentCmd.Millis - CurrentCmdTime;

            // Ajustamos el tiempo para asegurarnos de que no sea negativo ni infinito.
            if(timeUntilNextChange < 0)
                timeUntilNextChange = RemainingCmdTime;
                
            timeUntilNextChange = Math.Min(RemainingCmdTime, timeUntilNextChange);
            return (speed, Convert.ToInt32(timeUntilNextChange));
        }

    }

}

