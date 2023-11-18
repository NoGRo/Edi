using Buttplug;
using Buttplug.Client;
using Buttplug.Core;
using Buttplug.Core.Messages;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.Buttplug
{
    public class ButtplugDevice : IDevice
    {
        public ButtplugClientDevice Device { get; private set; }
        public ActuatorType Actuator { get; }
        public uint Channel { get; }
        private FunscriptRepository repository { get; set; }
        public string Name { get; set; } 
        public string SelectedVariant { get; set; }

        public IEnumerable<string> Variants => repository.GetVariants();

        private  CmdLinear CurrentCmd { get;  set; }
        private DateTime SendAt { get;  set; }
        private static double CurrentTime => (DateTime.Now - SyncSend).TotalMilliseconds;

        private static Timer timerCmdEnd = new Timer();

        private double lastSpeedSend = 0;

        public bool IsReady => true;

        public ButtplugDevice(ButtplugClientDevice device, ActuatorType actuator, uint channel, FunscriptRepository repository)
        {
            this.Device = device;
            Name = device.Name + (device.GenericAcutatorAttributes(actuator).Count() > 1 
                                    ? $" {actuator.ToString()}: {channel+1}" 
                                    : "" );
            
            Actuator = actuator;
            Channel = channel;
            this.repository = repository;
            timerCmdEnd.Elapsed += OnCommandEnd;

            SelectedVariant = Variants.FirstOrDefault(x => x.Contains(Actuator.ToString(),StringComparison.OrdinalIgnoreCase))
                                ?? repository.Config.DefaulVariant;
        }

        public async  Task SendCmd(CmdLinear cmd)
        {

            if (Device == null)
                return;

            Task sendtask = Task.CompletedTask;

            CurrentCmd = cmd;

            switch (Actuator)
            {
                case ActuatorType.Position:
                    sendtask = Device.LinearAsync(new[] { (cmd.buttplugMillis, cmd.LinearValue) });
                    break;
                case ActuatorType.Rotate:
                    sendtask = Device.RotateAsync(Math.Min(1.0, Math.Max(0, CurrentCmd.Speed / (double)450)), CurrentCmd.Direction); ;
                    break;
            }

            SendAt = DateTime.Now;
            cmd.Sent = DateTime.Now;
            
            var time = cmd.AbsoluteTime != 0
                        ? cmd.AbsoluteTime - CurrentTime
                        : cmd.Millis;

            if (time <= 0)
                time = 1;

            timerCmdEnd.Stop();
            timerCmdEnd.Interval = time;
            timerCmdEnd.Start();

            if (sendtask != null)
                await sendtask;
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

            var actuadores = Device.GenericAcutatorAttributes(Actuator);
            double steps = actuadores[(int)Channel].StepCount;
            if (steps == 0)
                steps = 1;
            steps = (100.0 / steps);
            var speed = (int)Math.Round(currVal / (double)steps) * steps;
            speed = (Math.Min(1.0, Math.Max(0, speed / (double)100)));
            return speed;
        }

        private static List<CmdLinear> queue { get; set; } = new List<CmdLinear>();
        public static DateTime SyncSend { get; private set; }
        public bool IsPause { get; private set; } = true;
        private long ResumeAt { get; set; }
        

        private FunscriptGallery CurrentGallery;
        public async Task PlayGallery(string name,long seek = 0)
        {
            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
                return;

            CurrentGallery = gallery;
            SyncSend = DateTime.Now;
            queue = CurrentGallery.Commands.ToList();
            queue.AddAbsoluteTime();

            if (seek != 0)
                await Seek(seek);


            await PlayNext();
        }
        private async Task PlayNext()
        {
            timerCmdEnd.Stop();
            if (queue?.Any() == true)
            {
                IsPause = false;
                var cmd = queue.First();
                queue.RemoveAt(0);
                if(cmd!= null)
                    await SendCmd(cmd);

            }
            else if (CurrentGallery.Loop)
            {
                await PlayGallery(CurrentGallery.Name);
            }

        }

        private async Task Seek(long time)
        {
            queue = queue.Where(x => x.AbsoluteTime > time).ToList();
            var next = queue.FirstOrDefault();
            if (next == null) 
                return;
            next.Millis = Convert.ToInt32(next.AbsoluteTime - time);
        }
        private async void OnCommandEnd(object sender, ElapsedEventArgs e)
        {
            await PlayNext(); 
        }

        public async Task Pause()
        {
            IsPause = true;
            ResumeAt = (long)CurrentTime;
            timerCmdEnd.Stop();
            await Device.Stop();
        }
    }
}
