using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Device.Interfaces;
using Edi.Core.Services;

namespace Edi.Core.Device
{
    [AddINotifyPropertyChangedInterface]
    [GameConfig]
    public class DevicesConfig
    {
        public Dictionary<string, DeviceConfig> Devices { get; set; } = new Dictionary<string, DeviceConfig>();
    }

    [AddINotifyPropertyChangedInterface]
    public class DeviceConfig : IRange
    {
        public string Variant { get; set; }
        public string Channel { get; set; }
        
        public int Min { get; set; } = 0;
        public int Max { get; set; } = 100;
        private int? offsetMS;
        public int? OffsetMS
        {
            get => offsetMS;
            set => offsetMS = value is null
                ? null
                : DeviceOffset.Normalize(value.Value);
        }
    }
}
