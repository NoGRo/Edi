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
    public class ButtplugDevice : IDevice
    {
        public ButtplugClientDevice Device { get; private set; }
        private ButtplugConfig config { get;  set; }
        public ActuatorType Actuator { get; }
        public uint Channel { get; }
        private FunscriptRepository repository { get; set; }
        public string Name { get; set; }
        public string SelectedVariant 
        { 
            get => selectedVariant; 
            set { 
                selectedVariant = value;
                if(CurrentGallery != null && !IsPause )
                    PlayGallery(CurrentGallery.Name, CurrentTime).GetAwaiter();
            } 
        }

        public IEnumerable<string> Variants => repository.GetVariants();

        private List<CmdLinear> queue { get; set; } = new List<CmdLinear>();
        public CmdLinear CurrentCmd { get;  set; }
        private DateTime CmdSendAt { get;  set; }
        private int CurrentTime => Convert.ToInt32((DateTime.Now - SyncSend).TotalMilliseconds + SeekTime);
        public int ReminingTime => CurrentCmd.Millis - Convert.ToInt32((DateTime.Now - CmdSendAt).TotalMilliseconds);
        
        public DateTime SyncSend { get; private set; } = DateTime.Now;
        public bool IsPause { get; private set; } = true;
        private long ResumeAt { get; set; }
        private long SeekTime { get; set; }

        private Timer timerCmdEnd = new Timer();

        public bool IsReady => true;

        public ButtplugDevice(ButtplugClientDevice device, ActuatorType actuator, uint channel, FunscriptRepository repository, ButtplugConfig config)
        {
            this.Device = device;
            Name = device.Name + (device.GenericAcutatorAttributes(actuator).Count() > 1 
                                    ? $" {actuator.ToString()}: {channel+1}" 
                                    : "" );
            
            Actuator = actuator;
            Channel = channel;
            this.config = config;
            this.repository = repository;
            timerCmdEnd.Elapsed += OnCommandEnd;

            SelectedVariant = Variants.FirstOrDefault(x => x.Contains(Actuator.ToString(),StringComparison.OrdinalIgnoreCase))
                                ?? repository.Config.DefaulVariant;
        }

        private FunscriptGallery CurrentGallery;
        private string selectedVariant;

        public async Task PlayGallery(string name, long seek = 0)
        {
            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
                return;

            SyncSend = DateTime.Now;
            CurrentGallery = gallery;
            PrepareQueue();

            SeekTime = seek;

            await PlayNext();
        }

        public void PrepareQueue()
        {
            var cmds = CurrentGallery.Commands.ToList();
            CmdLinear last = CurrentGallery.Loop ? cmds.LastOrDefault() : null;
            var at = 0;
            foreach (var cmd in cmds)
            {
                cmd.Prev = last;
                at += cmd.Millis;
                cmd.AbsoluteTime = at;
                last = cmd;
            }
            queue = cmds;
        }


        private async void OnCommandEnd(object sender, ElapsedEventArgs e)
        {
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

            if (CurrentGallery?.Loop == true)
            {
                await PlayGallery(CurrentGallery.Name);
            }

        }

        public async Task SendCmd(CmdLinear cmd)
        {

            if (Device == null)
                return;

            Task sendtask = Task.CompletedTask;


            if (cmd.Millis >= config.CommandDelay || (DateTime.Now - CmdSendAt).TotalMilliseconds >= config.CommandDelay)
            {
                switch (Actuator)
                {
                    case ActuatorType.Position:
                        sendtask = Device.LinearAsync(new[] { (cmd.buttplugMillis, cmd.LinearValue) });
                        break;
                    case ActuatorType.Rotate:
                        sendtask = Device.RotateAsync(Math.Min(1.0, Math.Max(0, CurrentCmd.Speed / (double)450)), CurrentCmd.Direction); ;
                        break;
                }

                CurrentCmd = cmd;
                //Debug.WriteLineIf(Name == "The Handy", $"{Name}: at:{cmd.AbsoluteTime} Cur:{CurrentTime}  {cmd.Millis}-{cmd.Value}");

                CmdSendAt = DateTime.Now;
                cmd.Sent = DateTime.Now;
            }

            timerCmdEnd.Interval = cmd.Millis;
            timerCmdEnd.Start();

            if (sendtask != null)
                try
                {
                    await sendtask;
                } catch  { }
        }


        public double CalculateSpeed()
        {
            var passes = DateTime.Now - CmdSendAt;
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


        private void Seek(long time)
        {
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


        public async Task Stop()
        {
            IsPause = true;
            ResumeAt = (long)CurrentTime;
            timerCmdEnd.Stop();
            await Device.Stop();
        }
    }
}
