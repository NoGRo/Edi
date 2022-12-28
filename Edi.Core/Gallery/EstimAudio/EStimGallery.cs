using System.Collections.Generic;
using System.IO;
using Edi.Core.Funscript;
using NAudio.Wave;

namespace Edi.Core.Gallery.EStimAudio
{
    public class EStimGallery
    {
        public string Name { get; set; }
        public string Variant { get; set; }
        public string AudioPath { get; set; }
        public long StartTime { get; set; }
        public long Duration { get; set; }
        public bool Repeats { get; set; }
    }
}