using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Interfaces
{

    public interface IDevice
    {
        string Name { get; set; }
        string Channel { get; set; }
        bool IsReady { get; }

        string SelectedVariant { get; set; }
        string DefaultVariant();
        IEnumerable<string> Variants { get; }

        Task PlayGallery(string name, long seek = 0);
        Task Stop();
    }
}
