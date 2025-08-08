using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core
{
    [AddINotifyPropertyChangedInterface]
    public class EdiConfig
    {
        public bool Filler { get; set; } = true;
        public bool Gallery { get; set; } = true;
        public bool Reactive { get; set; } = true;
        public IEnumerable<string> Channels { get; set; } = [];
        public string ExecuteOnReady { get; set; } = "";
        public bool UseHttps { get; set; } = false;
        public bool UseLogs { get;  set; } = false;
    }
}
