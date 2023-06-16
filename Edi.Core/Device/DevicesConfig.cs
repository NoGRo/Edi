using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device
{
    [AddINotifyPropertyChangedInterface]
    public class DevicesConfig
    {
        public Dictionary<string, string> DeviceVariant { get; set; } = new Dictionary<string, string>();
    }
}
