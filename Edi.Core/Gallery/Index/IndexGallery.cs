using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Edi.Core.Gallery.Index
{
    public class IndexGallery: IGallery
    {

        public string Name { get; set; }
        public string Variant { get; set; }
        public int Duration { get; set; }
        public int StartTime { get; set; }
        public bool Repeats { get; set; }

        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>();

    }
}
