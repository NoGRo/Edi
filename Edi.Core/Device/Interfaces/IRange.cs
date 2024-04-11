using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Interfaces
{
    internal interface IRange
    {
        public int Min{ get; set; }
        public int Max { get; set; }    
    }
}
