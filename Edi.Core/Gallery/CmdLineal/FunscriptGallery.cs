using System.Collections.Generic;
using System.IO;
using Edi.Core.Funscript;

namespace Edi.Core.Gallery.CmdLineal
{
    public class FunscriptGallery: IGallery
    {
        public string Name { get; set; }
        public string Variant { get; set; }
        public virtual List<CmdLinear> Commands { get; set; } = new List<CmdLinear>();
        public bool Repeats { get; set; }
    }
}