using System.Collections.Generic;
using System.IO;
using Edi.Core.Funscript;

namespace Edi.Core.Gallery.models
{
    public class Gallery
    {
        public string Name { get; set; }
        public string Variant { get; set; }
        public virtual List<CmdLinear> Commands { get; set; } = new List<CmdLinear>();
        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>();
    }
}