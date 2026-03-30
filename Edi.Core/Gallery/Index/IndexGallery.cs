using Edi.Core.Funscript.FileJson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Edi.Core.Gallery.Index
{
    public class IndexGallery : IGallery
    {

        public string Name { get; set; }
        public string Variant { get; set; }
        public int Duration { get; set; }
        public long StartTime { get; set; }
        public bool Loop { get; set; }
        public string Bundle { get; set; } 
        public List<FunScriptAction> Actions { get; set; } = new List<FunScriptAction>();   
        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>();

    }
}
