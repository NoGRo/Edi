using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery
{
    public class GalleryConfig
    {
        public string DefaulVariant { get; set; }
        public string SelectedVariant { get; set; }
        public string GalleryPath { get; set; }
        public string UserDataPath { get; set; }

        public List<GalleryDefinition> Definitions { get; set; }
    }
}
