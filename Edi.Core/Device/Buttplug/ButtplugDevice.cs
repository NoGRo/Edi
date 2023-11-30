using Buttplug;
using Buttplug.Client;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using PropertyChanged;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.Buttplug
{
    [AddINotifyPropertyChangedInterface]
    public class ButtplugDevice : IDevice
    {
        
        public ActuatorType Actuator { get; }
        public uint Channel { get; }
        
        public string Name { get; set; } 
        public string SelectedVariant { get; set; }
        public bool IsReady => true;
        public IEnumerable<string> Variants => repository.GetVariants();
        public ButtplugClientDevice Device { get; private set; }
        private FunscriptRepository repository { get; set; }
        
        private FunscriptGallery CurrentGallery;
        public CmdLinear CurrentCmd { get;  set; }
        private DateTime SendAt { get;  set; }
        private double CurrentTime => (DateTime.Now - SyncSend).TotalMilliseconds;
        public int ReminingTime => CurrentCmd is null? 0 : CurrentCmd.Millis - Convert.ToInt32((DateTime.Now - SendAt).TotalMilliseconds);

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private Task commandLoopTask;

        
        public ButtplugDevice(ButtplugClientDevice device, ActuatorType actuator, uint channel, FunscriptRepository repository)
        {
            this.Device = device;
            Name = device.Name + (device.GenericAcutatorAttributes(actuator).Count() > 1 
                                    ? $" {actuator.ToString()}: {channel+1}" 
                                    : "" );
            
            Actuator = actuator;
            Channel = channel;
            this.repository = repository;
            SelectedVariant = Variants.FirstOrDefault(x => x.Contains(Actuator.ToString(),StringComparison.OrdinalIgnoreCase))
                                ?? repository.Config.DefaulVariant;
        }

        public async  Task SendCmd(CmdLinear cmd)
        {

            if (Device == null)
                return;

            CurrentCmd = cmd;
            SendAt = DateTime.Now;
            cmd.Sent = DateTime.Now;
            try
            {
                switch (Actuator)
                {
                    case ActuatorType.Position:
                        await Device.LinearAsync(new[] { (cmd.buttplugMillis, cmd.LinearValue) });
                        break;
                    case ActuatorType.Rotate:
                        await Device.RotateAsync(Math.Min(1.0, Math.Max(0, CurrentCmd.Speed / (double)450)), CurrentCmd.Direction); ;
                        break;
                }
            }
            catch (Exception ex) { 
                Debug.WriteLine(ex);
            }

        }

        public double CalculateSpeed()
        {
            var passes = DateTime.Now - SendAt;
            if(CurrentCmd == null)
                return 0;
            var distanceToTravel = CurrentCmd.Value - CurrentCmd.InitialValue;
            var travel = Math.Round(distanceToTravel * (passes.TotalMilliseconds / CurrentCmd.Millis), 0);
            travel = travel is double.NaN or double.PositiveInfinity or double.NegativeInfinity ? 0 : travel;
            var currVal = Math.Abs(CurrentCmd.InitialValue + Convert.ToInt16(travel));

            //Debug.WriteLine($"{CurrentCmd.InitialValue}- {travel} - {currVal}");

            var actuadores = Device.GenericAcutatorAttributes(Actuator);
            double steps = actuadores[(int)Channel].StepCount;
            if (steps == 0)
                steps = 1;
            steps = (100.0 / steps);
            var speed = (int)Math.Round(currVal / (double)steps) * steps;
            speed = (Math.Min(1.0, Math.Max(0, speed / (double)100)));
            return speed;
        }

        private  List<CmdLinear> queue { get; set; } = new List<CmdLinear>();
        public  DateTime SyncSend { get; private set; }
        public bool IsPause { get; private set; } = true;
        private long ResumeAt { get; set; }
        
        public async Task PlayGallery(string name,long seek = 0)
        {
            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
                return;

            CurrentGallery = gallery;
            SyncSend = DateTime.Now;
            queue = CurrentGallery.Commands.AddAbsoluteTime();
            
            if (seek != 0)
                await Seek(seek);

            IsPause = false;
            StartCommandLoop();
        }
        private void StartCommandLoop()
        {
            cancellationTokenSource.Cancel(); // Asegurar cancelar cualquier bucle existente
            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;

            commandLoopTask = Task.Run(async () =>
            {
                    await ExecuteNextCommand(token);
               
            }, token);
        }

        private async Task ExecuteNextCommand(CancellationToken token)
        {
            DateTime lastCommandSendTime = DateTime.MinValue;

            while (!token.IsCancellationRequested && !IsPause)
            {
                if (queue.Any())
                {
                    // Obtener el tiempo actual
                    var currentTime = DateTime.Now;

                    // Calcular el intervalo desde el último comando enviado
                    var intervalSinceLastCommand = (currentTime - lastCommandSendTime).TotalMilliseconds;

                    var nextcmd = queue.First();
                    //Debug.WriteLine($"queued: {Name} at {queue.Count()}");
                    queue.RemoveAt(0);
                    // Si el intervalo es menor a 50 ms, descartar los comandos anteriores
                    //if (intervalSinceLastCommand < 50)
                    //{
                    //    Debug.WriteLine($"Descartado: {nextcmd} at {DateTime.Now:HH:mm:ss.fff}");
                    //    var val = nextcmd.Millis - Convert.ToInt32(intervalSinceLastCommand);
                    //    await Task.Delay(val > 0 ? val : nextcmd.Millis);




                    //    continue; // Continuar con el siguiente ciclo del bucle
                    //}

                    // Enviar el siguiente comando en la cola

                    lastCommandSendTime = DateTime.Now;
                    //Debug.WriteLine($"Sending command: {Name} at {DateTime.Now:HH:mm:ss.fff}");
                    await SendCmd(nextcmd);
                    Debug.WriteLine($"Enviado: {Name} at {DateTime.Now:HH:mm:ss.fff}");

                    // Actualizar el tiempo del último comando enviado


                    // Esperar el tiempo restante del intervalo si es necesario
                    var remainingWaitTime = nextcmd.Millis - ((DateTime.Now - lastCommandSendTime).TotalMilliseconds);
                    if (remainingWaitTime > 0)
                    {
                        await Task.Delay((int)remainingWaitTime, token);
                    }
                    else
                    {
                        Debug.WriteLine($"Descartado: {nextcmd} at {DateTime.Now:HH:mm:ss.fff}");
                        var val = nextcmd.Millis - Convert.ToInt32(intervalSinceLastCommand);
                        await Task.Delay(val > 0 ? val : nextcmd.Millis);
                    }
                }
                else if (CurrentGallery?.Loop == true)
                {
                    lastCommandSendTime = DateTime.MinValue;

                    await RestartGallery();
                }
                else
                {
                    // Si no hay comandos y no se debe hacer loop, hacer una espera corta para evitar uso excesivo de CPU
                    await Pause();
                }
            }
        }
        private async Task RestartGallery()
        {
            queue = CurrentGallery.Commands.AddAbsoluteTime();
            SyncSend = DateTime.Now;
            await ExecuteNextCommand(cancellationTokenSource.Token);

        }

        private async Task Seek(long time)
        {
            queue = queue.Where(x => x.AbsoluteTime > time).ToList();
            var next = queue.FirstOrDefault();
            if (next == null) 
                return;
            next.Millis = Convert.ToInt32(next.AbsoluteTime - time);
        }

        public async Task Pause()
        {
            IsPause = true;
            ResumeAt = (long)CurrentTime;
            cancellationTokenSource.Cancel();
            await Device.Stop();
        }
    }
}
