using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Gallery.Definition;
using PropertyChanged;

namespace Edi.Core.Gallery
{
    [AddINotifyPropertyChangedInterface]
    public class GalleryConfig
    {
        public string GalleryPath { get; set; }
        public bool GenerateDefinitionFromChapters { get; set; } = true;
        public bool GenerateChaptersFromDefinition { get; set; } = false;
        public bool InproveLoopDetection { get; internal set; } = true;
    }
}
