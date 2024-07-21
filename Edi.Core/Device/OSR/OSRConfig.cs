using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Funscript;
using PropertyChanged;


namespace Edi.Core.Device.OSR
{
    [AddINotifyPropertyChangedInterface]
    public class OSRConfig
    {
        public string? COMPort { get; set; } = null;
        public bool EnableMultiAxis { get; set; } = false;
        public RangeConfiguration RangeLimits { get; set; } = new RangeConfiguration();
    }

    public class RangeConfiguration
    {
        public CmdRange Linear { get; set; } = new CmdRange();
        public CmdRange Roll { get; set; } = new CmdRange();
        public CmdRange Pitch { get; set; } = new CmdRange();
        public CmdRange Twist { get; set; } = new CmdRange();
        public CmdRange Sway { get; set; } = new CmdRange();
        public CmdRange Surge { get; set; } = new CmdRange();
    }
}
