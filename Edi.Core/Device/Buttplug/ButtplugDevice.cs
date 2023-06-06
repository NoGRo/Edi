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
using System.Text;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.Buttplug
{
    internal class ButtplugDevice : IDevice 
    {
        private ButtplugClientDevice device { get; set; }
        public ActuatorType Actuator { get; }
        public uint Channel { get; }
        private FunscriptRepository repository { get; set; }
        public string Name => $"{device.Name} ({Actuator}:{Channel})";

        private string selectedVariant;
        public string SelectedVariant { get => selectedVariant ?? repository.Config.DefaulVariant; set => selectedVariant = value; }

        public IEnumerable<string> Variants => repository.GetVariants();

        private  CmdLinear CurrentCmd { get;  set; }
        private DateTime SendAt { get;  set; }
        private static double CurrentTime => (DateTime.Now - SyncSend).TotalMilliseconds;

        private static Timer timerCmdEnd = new Timer();

        private double lastSpeedSend = 0;
        public ButtplugDevice(ButtplugClientDevice device, ActuatorType actuator, uint channel, FunscriptRepository repository)
        {
            this.device = device;
            Actuator = actuator;
            Channel = channel;
            this.repository = repository;
            vibCommandTimer.Interval = (device.MessageTimingGap == 0 ) ? 20: device.MessageTimingGap;
            timerCmdEnd.Elapsed += OnCommandEnd;
            vibCommandTimer.Elapsed += FadeVibratorCmd;
        }

        private Timer vibCommandTimer = new Timer(50);
        public async  Task SendCmd(CmdLinear cmd)
        {
            vibCommandTimer.Stop();
            if (device == null)
                return;

            Task sendtask = Task.CompletedTask;

            CurrentCmd = cmd;

            switch (Actuator)
            {
                case ActuatorType.Vibrate or ActuatorType.Oscillate:
                    vibCommandTimer.Start(); //constant resend Vibrations
                    //ButtplugController.start?
                    break;
                case ActuatorType.Position:
                    sendtask = device.LinearAsync(new[] { (cmd.buttplugMillis, cmd.LinearValue) });
                    break;
                case ActuatorType.Rotate:
                    sendtask = device.RotateAsync(Math.Min(1.0, Math.Max(0, CurrentCmd.Speed / (double)450)), CurrentCmd.Direction); ;
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


        private async void FadeVibratorCmd(object? sender, ElapsedEventArgs e)
        {

            if (device == null)
            {
                vibCommandTimer.Stop();
                return;
            }

            double speed = CalculateSpeed();

            try
            {
                switch (Actuator)
                {
                    case ActuatorType.Vibrate:
                        if (speed != lastSpeedSend)
                            await device.VibrateAsync(new[] { (Channel, speed) });
                        break;
                    case ActuatorType.Oscillate:
                        if (speed != lastSpeedSend)
                            await device.OscillateAsync(new[] { (Channel, speed) });
                        break;



                }
                lastSpeedSend = speed;
            }
            catch (ButtplugDeviceException)
            {
                device = null;
            }
        }

        private double CalculateSpeed()
        {
            var passes = DateTime.Now - SendAt;
            var distanceToTravel = CurrentCmd.Value - CurrentCmd.InitialValue;
            var travel = Math.Round(distanceToTravel * (passes.TotalMilliseconds / CurrentCmd.Millis), 0);
            travel = travel is double.NaN or double.PositiveInfinity or double.NegativeInfinity ? 0 : travel;
            var currVal = Math.Abs(CurrentCmd.InitialValue + Convert.ToInt16(travel));

            var actuadores = device.GenericAcutatorAttributes(Actuator);
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
                var cmd = queue.First();
                queue.RemoveAt(0);
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
            
            ResumeAt = (long)CurrentTime;
            timerCmdEnd.Stop();
            vibCommandTimer.Stop();
            await device.Stop();
        }
        public async Task Resume()
        {
            await Seek(ResumeAt);
            await PlayNext();
        }
    }
}
