using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Interfaces
{

    public interface IDevice
    {
        
        string SelectedVariant { get; set; }
        IEnumerable<string> Variants { get; }

        string Name { get; set; }
        public bool IsReady { get; }
        public Task PlayGallery(string name, long seek = 0);
        public Task Stop();
    }
}
