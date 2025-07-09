using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.EStim
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class EStimConfig
    {
        public int DeviceId { get; set; } = -1;

    }
}
