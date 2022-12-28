using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Gallery.Definition;

namespace Edi.Core.Gallery
{
    public class GalleryConfig
    {
        public string DefaulVariant { get; set; }
        public string SelectedVariant { get; set; }
        public string GalleryPath { get; set; }
        public List<DefinitionGallery> Definitions { get; set; }
    }
}
