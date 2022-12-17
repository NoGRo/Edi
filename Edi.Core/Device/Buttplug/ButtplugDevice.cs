using Buttplug;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Timers;
using static Buttplug.ServerMessage.Types;
using Timer = System.Timers.Timer;

namespace Edi.Core.Device.Buttplug
{
    internal class ButtplugDevice :  IDevice, IEqualityComparer<ButtplugDevice>
    {
        private ButtplugClientDevice device { get; set; }
        private IGalleryRepository repository { get; set; }
        public string Name => device.Name;

        private  CmdLinear CurrentCmd { get;  set; }
        private DateTime SendAt { get;  set; }
        private static double CurrentTime => (DateTime.Now - SyncSend).TotalMilliseconds;

        private static Timer timerCmdEnd = new Timer();

        public ButtplugDevice(ButtplugClientDevice device, IGalleryRepository repository)
        {
            this.device = device;
            this.repository = repository;
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
         
            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.LinearCmd))
            {
                sendtask = device.SendLinearCmd(cmd.buttplugMillis, cmd.LinearValue);
            }
            if (device.AllowedMessages.ContainsKey(MessageAttributeType.VibrateCmd))
            {
                vibCommandTimer.Start();
            }
            CurrentCmd = cmd;
            
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

            var passes = DateTime.Now - SendAt;
            var distanceToTravel = CurrentCmd.Value - CurrentCmd.InitialValue;
            var travel = Math.Round(distanceToTravel * (passes.TotalMilliseconds / CurrentCmd.Millis), 0);
            travel = travel is double.NaN ? 0 : travel;
            var currVal = Math.Abs(CurrentCmd.InitialValue + Convert.ToInt16(travel));
            var speed = Math.Min(1.0, Math.Max(0, currVal / (double)100));

            try
            {
                await device.SendVibrateCmd(speed);
            }
            catch (ButtplugDeviceException)
            {
                device = null;
            }
        }

        private static List<CmdLinear> queue { get; set; } = new List<CmdLinear>();
        public static DateTime SyncSend { get; private set; }

        private GalleryIndex CurrentGallery;
        public async Task SendGallery(string name,long seek = 0)
        {
            var gallery = repository.Get(name, device.AllowedMessages.ContainsKey(MessageAttributeType.VibrateCmd) ? "vibrator" : null);
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
            else if (CurrentGallery.Repeats)
            {
                await SendGallery(CurrentGallery.Name);
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
            PlayNext(); 
        }

        public bool Equals(ButtplugDevice? x, ButtplugDevice? y)
            => x?.device == y?.device;
        

        public int GetHashCode([DisallowNull] ButtplugDevice obj)
        {
            var h= new HashCode() ;
            h.Add(obj.device);
            return h.ToHashCode();
        }

        public Task Pause()
        {
            throw new NotImplementedException();
        }

        public Task Resume()
        {
            throw new NotImplementedException();
        }
    }
}
