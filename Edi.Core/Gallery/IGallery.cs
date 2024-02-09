using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery
{
    public interface IGallery
    {
        public string Name { get; set; }
        public string Variant { get; set; }
    }
}

