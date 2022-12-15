using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy
{
    public interface IDeviceUpload
    {
        public long CurrentTime { get; }
        public bool IsPlaying { get; }
        public Task Play(long? timeMs);
        public Task Seek(long timeMs);
        public Task Stop();

    }
}
