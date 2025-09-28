using Edi.Core.Services;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class HandyConfig
    {
        public string Key { get; set; }
    }
}
