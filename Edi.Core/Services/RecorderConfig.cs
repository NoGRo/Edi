using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core
{
    [AddINotifyPropertyChangedInterface]
    public class RecorderConfig
    {
        public int X { get; set; } 
        public int Y { get; set; } 
        public int Width { get; set; } 
        public int Height { get; set; }
        public int FrameRate { get; set; } = 30;
        public string OutputName { get; set; } = "output";
        public string FfmpegCodec { get; set; } = "-c:v h264_nvenc -preset fast";
    }
}