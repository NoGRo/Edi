using System.Collections.Generic;
using System.IO;
using Edi.Core.Funscript;

namespace Edi.Core.Gallery
{
    public class Gallery
    {
        public string Name { get; set; }
        public string Variant { get; set; }
        public virtual List<CmdLinear> Commands { get; set; }
        public Dictionary<string, FileInfo> Assets;
    }
}