using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Interfaces
{

    public interface IDevice
    {
        public bool IsReady { get; }
        string SelectedVariant { get; set; }
        IEnumerable<string> Variants { get; }
        string Name { get; set; }
        public Task PlayGallery(string name, long seek = 0);
        public Task Stop();
    }
}
