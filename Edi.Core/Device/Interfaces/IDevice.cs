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
        string Name { get; }
        protected Task PlayGallery(string name, long seek = 0);
        protected Task Pause();
        protected Task Resume();
    }
}
