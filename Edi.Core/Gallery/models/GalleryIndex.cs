using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Edi.Core.Gallery.models
{
    public class GalleryIndex : Gallery
    {
        public int Duration { get; set; }
        public int StartTime { get; set; }
        public int EndTime { get; set; }
        public bool HasSpacer { get; set; }
        public bool Repeats { get; set; }

    }
}
