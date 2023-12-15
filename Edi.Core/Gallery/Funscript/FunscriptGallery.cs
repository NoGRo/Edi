using System.Collections.Generic;
using System.IO;
using Edi.Core.Funscript;

namespace Edi.Core.Gallery.CmdLineal
{
    public class FunscriptGallery : IGallery
    {
        public string Name { get; set; }
        public string Variant { get; set; }
        public List<CmdLinear> Commands { get => AxisCommands[Axis.Default]; }
        public virtual Dictionary<Axis, List<CmdLinear>> AxisCommands { get; set; } = new Dictionary<Axis, List<CmdLinear>>();

        public bool Loop { get; set; }

        public FunscriptGallery Clone()
        {
            var gallery = new FunscriptGallery
            {
                Name = this.Name,
                Variant = this.Variant,
                Loop = this.Loop,
            };

            foreach (var axis in AxisCommands.Keys)
            {
                gallery.AxisCommands[axis] = AxisCommands[axis].Clone();
            }

            return gallery;
        }
    }
}