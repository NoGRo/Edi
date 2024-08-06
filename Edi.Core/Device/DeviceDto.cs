using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Device.Interfaces;

namespace Edi.Core.Device
{
    public class DeviceDto : IRange
    {
        public string Name { get; set; }
        public IEnumerable<string> Variants { get; set; }
        public bool IsReady { get; set; }
        public string SelectedVariant { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
    }
}
