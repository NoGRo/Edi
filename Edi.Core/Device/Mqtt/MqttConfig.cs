using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Mqtt
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class MqttConfig
    {
        public string Server { get; set; } = "localhost:1883";
        public string[] Topics { get; set; } = { "edi/device/" };
    }
}
