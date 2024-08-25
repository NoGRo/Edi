using System.Text.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Edi.Core.Funscript;
using CsvHelper.Configuration;
using Edi.Core.Gallery;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.Definition;
using System.Runtime.CompilerServices;
using System.Threading;
using PropertyChanged;
using System.Timers;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;
using System.Reflection;
using System.Security;
using static Edi.Core.Device.Handy.HandyV3Device;
using System.Net;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    internal class HandyV3Device : DeviceBase<FunscriptRepository,FunscriptGallery> 
    {

        public string Key { get; set; }

        
        private static long timeSyncAvrageOffset;
        
        public HttpClient Client = null;




        private string CurrentBundle = "default";
        public HandyV3Device(HttpClient Client, FunscriptRepository repository): base(repository) 
        {
            Key = Client.DefaultRequestHeaders.GetValues("X-Connection-Key").First();
            //make unique nane 
            Name = $"The Handy [{Key}]";

            this.Client = Client;
        }

    
        internal override async Task applyRange()
        {
            Debug.WriteLine($"Handy: {Key} Slide {Min}-{Max}");
            var request = new SlideRequest(Min, Max);
            await Client.PutAsync("slide", new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"));
        }
        private long totalBufferTime;
        public override async Task PlayGallery(FunscriptGallery gallery, long seek = 0)
        {

            var loop = gallery.Loop 
                        && seek > gallery.Commands.Take(100).Last().AbsoluteTime;
            //seek is in inital bufer time, in this case manage self loop True
            var index = 0;
            if (!loop)
                index = gallery.Commands.FindIndex(x => x.AbsoluteTime >= seek);

            var buf = gallery.Commands.Skip(index).Take(100);

            //_ = SendPoints(buf,true, loop);

            if (totalBufferTime >= currentGallery.Duration && loop)
                return;

            while (totalBufferTime < (gallery.Duration - seek))
            {
                buf = currentGallery.Commands.Skip(index + buf.Count()).Take(100);
                //await SendPoints(buf, false);
            }
        }

        //AddPioint 
        //private Task SendPoints(IEnumerable<CmdLinear> cmds, bool flush, bool Loop = true)
        //{
        //    if (flush)
        //        totalBufferTime = 0;
        //    totalBufferTime += cmds.Sum(x => x.Millis);

        //}

        public override async Task StopGallery()
        {

            Debug.WriteLine($"Handy: {Key} Stop");
            try
            {
                await Client.PutAsync("hsp/stop", null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Handy: {Key} Error: {ex.Message}");
            }
        }


        internal async Task updateServerTime()
        {
            timeSyncAvrageOffset = await ServerTimeSync.SyncServerTimeAsync();
            Debug.WriteLine($"Handy: [Offset {timeSyncAvrageOffset}]");
        }

      
        private long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +  timeSyncAvrageOffset;
        
        
        


    }

    internal record Playback(int StartTime, long ServerTime, double PlaybackRate, bool Loop);

    internal record PointData(List<Point> Points, bool Flush, int TailPointStreamIndex);
    internal record Point(int T, int X);

    internal static class CmdLinealToPointsExtended
    {
        public static IEnumerable<Point> TakePoints(this List<CmdLinear> cmds, long seek = 0)
        {
            return cmds
                .Where(x => x.AbsoluteTime > seek)
                .Take(100)
                .Select(x => new Point(x.Millis, Convert.ToInt32(x.Value)))
                .ToList();
        }
    }

    // Example usage:
    // await ServerTimeSync.SyncServerTimeAsync(10);
    // var serverTime = ServerTimeSync.GetEstimatedServerTime();
    // Console.WriteLine($"Estimated Server Time: {serverTime}");



}
