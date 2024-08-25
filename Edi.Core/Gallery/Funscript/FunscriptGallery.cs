using System.Collections.Generic;
using System.IO;
using Edi.Core.Funscript;

namespace Edi.Core.Gallery.Funscript
{
    public class FunscriptGallery : IGallery
    {
        public string Name { get; set; }
        public string Variant { get; set; }
        public List<CmdLinear> Commands
        {
            get
            {
                var enumValues = Enum.GetValues<Axis>();
                foreach (var enumValue in enumValues)
                {
                    if (AxesCommands.ContainsKey(enumValue))
                        return AxesCommands[enumValue];
                }

                return null;
            }
            set
            {
                AxesCommands[Axis.Default] = value;
            }
        }
        public virtual Dictionary<Axis, List<CmdLinear>> AxesCommands { get; set; } = new Dictionary<Axis, List<CmdLinear>>();
        public int Duration { get; set; }
        public bool Loop { get; set; }

        public FunscriptGallery Clone()
        {
            var gallery = new FunscriptGallery
            {
                Name = Name,
                Variant = Variant,
                Loop = Loop,
            };

            foreach (var axis in AxesCommands.Keys)
            {
                gallery.AxesCommands[axis] = AxesCommands[axis].Clone();
            }

            return gallery;
        }
    }
}