using Edi.Core.Gallery.Index;
using Microsoft.Extensions.Logging;

namespace Edi.Core.Device.AutoBlow
{
    internal sealed class VacuGlide2Device : AutoBlowDevice
    {
        public VacuGlide2Device(
            HttpClient client,
            IndexRepository repository,
            ILogger logger)
            : base(
                client,
                repository,
                logger,
                "Autoblow VacuGlide 2")
        {
        }
    }
}
