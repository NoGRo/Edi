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
using System.Xml.Linq;
using Timer = System.Timers.Timer;

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

        private List<CmdLinear> queue { get; set; } = new List<CmdLinear>();
        public CmdLinear CurrentCmd { get;  set; }
        private DateTime CmdSendAt { get;  set; }
        public int CurrentCmdTime => CurrentCmd == null ? 0 : Convert.ToInt32(this.CurrentTime - (CurrentCmd.AbsoluteTime - CurrentCmd.Millis));
        public int ReminingTime => CurrentCmd == null ? 0 : Math.Max(0, Convert.ToInt32((CurrentCmd.AbsoluteTime) - this.CurrentTime));
        
        private Timer timerCmdEnd = new Timer();
        private double vibroSteps;

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
            timerCmdEnd.Elapsed += OnCommandEnd;
            
            SelectedVariant = Variants.FirstOrDefault(x => x.Contains(Actuator.ToString(),StringComparison.OrdinalIgnoreCase))
                                ?? Variants.FirstOrDefault("");
        }


        public override async Task PlayGallery(FunscriptGallery gallery, long seek = 0)
        {
            PrepareQueue(gallery);
            await PlayNext();
        }

        public void PrepareQueue(FunscriptGallery gallery)
        {
            
            var cmds = gallery.Commands.ToList();
            CmdLinear last = gallery.Loop ? cmds.LastOrDefault() : null;
            var at = 0;
            foreach (var cmd in cmds)
            {
                cmd.Prev = last;
                at += cmd.Millis;
                cmd.AbsoluteTime = at;
                last = cmd;
            }
            lock (queue) {
                queue = cmds;
            }
        }


        private async void OnCommandEnd(object sender, ElapsedEventArgs e)
        {
            /*
            // Calcular el tiempo esperado para que el comando actual finalice.
            var expectedEndTime = CmdSendAt.AddMilliseconds(CurrentCmd?.Millis ?? 0);
            var delay = e.SignalTime - expectedEndTime;

            // Imprimir en la consola la información del retraso.
            Debug.WriteLine($"Timer Command End: SignalTime={e.SignalTime}, ExpectedEndTime={expectedEndTime}, Delay={delay.TotalMilliseconds} ms");
            */
            await PlayNext();
        }
        private async Task PlayNext()
        {
            timerCmdEnd.Stop();
            
            Seek(CurrentTime);

            CmdLinear nextcmd = null;
            if (queue?.Any() == true)
            {
                IsPause = false;
                nextcmd = queue.First();
                queue.RemoveAt(0);
            }
            if (nextcmd != null)
            {
                await SendCmd(nextcmd);
                return;
            }
        }

        public async Task SendCmd(CmdLinear cmd)
        {

            if (Device == null)
                return;

            if(cmd.Millis <= 0)
            {
                await Task.Delay(1);
                await PlayNext();
                return;
            }
            Task sendtask = Task.CompletedTask;
            if (cmd.Millis >= config.CommandDelay || (DateTime.Now - CmdSendAt).TotalMilliseconds >= config.CommandDelay)
            {
             
                CurrentCmd = cmd;
                CmdSendAt = DateTime.Now;
                cmd.Sent = DateTime.Now;

                switch (Actuator)
                {
                    case ActuatorType.Position:
                        sendtask = Device.LinearAsync(new[] { (cmd.buttplugMillis, cmd.LinearValue) });
                        break;
                    case ActuatorType.Rotate:
                        sendtask = Device.RotateAsync(Math.Min(1.0, Math.Max(0, CurrentCmd.Speed / (double)450)), CurrentCmd.Direction); ;
                        break;
                }
            }

            timerCmdEnd.Interval = Math.Max(1, cmd.Millis); ;
            timerCmdEnd.Start();

            if (sendtask != null)
            {
                try
                {
                    await sendtask;
                }
                catch { }
            }
        }


        public double CalculateSpeed()
        {
            
            if(CurrentCmd == null)
                return 0;
            


            var distanceToTravel = CurrentCmd.Value - CurrentCmd.InitialValue;
            var travel = Math.Round(distanceToTravel * ((double)CurrentCmdTime / CurrentCmd.Millis), 0);
            travel = travel is double.NaN or double.PositiveInfinity or double.NegativeInfinity ? 0 : travel;
            var currVal = Math.Abs(CurrentCmd.InitialValue + Convert.ToInt16(travel));

            //Debug.WriteLine($"{CurrentCmd.InitialValue}- {travel} - {currVal}");

            var speed = (int)Math.Round(currVal / vibroSteps) * vibroSteps;
            speed = Math.Min(1.0, Math.Max(0, speed / (double)100));
            return speed;
        }


        private void Seek(long time)
        {
            lock (queue) {
                int index = queue.FindIndex(x => x.AbsoluteTime > time);
                if (index > 0)
                    queue.RemoveRange(0, index);
                else if (index == -1 && queue.Any())
                    queue.Clear();

                var next = queue.FirstOrDefault();

                if (next == null) 
                    return;
                next.Millis = Convert.ToInt32(next.AbsoluteTime - time);
            }
        }

        public override async Task StopGallery()
        {
            timerCmdEnd.Stop();
              await Device.Stop();
        }
    }
}

