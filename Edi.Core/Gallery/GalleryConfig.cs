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
        public string DefaulVariant { get; set; }
        public string SelectedVariant { get; set; }
        public string GalleryPath { get; set; }
    }
}
