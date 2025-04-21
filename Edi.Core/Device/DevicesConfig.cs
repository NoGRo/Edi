using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Device.Interfaces;

namespace Edi.Core.Device
{
    [AddINotifyPropertyChangedInterface]
    public class DevicesConfig
    {
        public Dictionary<string, DeviceConfig> Devices { get; set; } = new Dictionary<string, DeviceConfig>();
    }

    public class DeviceConfig : IRange
    {
        public string Variant { get; set; }

        public int Min { get; set; } = 0;
        public int Max { get; set; } = 100;

        public string Channel { get; set; }
    }
}
