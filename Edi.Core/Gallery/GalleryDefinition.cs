using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery
{
    public class GalleryDefinition
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public long StartTime { get; set; }
        public long EndTime { get; set; }
        public bool Loop { get; set; }


    }
}
