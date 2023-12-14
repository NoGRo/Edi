using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery.Index
{
    internal class BundleDefinition
    {
        public List<string>  Galleries { get; set; }
        public string BundleName { get; set; } = "default";
    }
}
