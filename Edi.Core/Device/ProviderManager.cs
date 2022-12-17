using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Interfaces;
using Microsoft.Extensions.DependencyInjection;
namespace Edi.Core.Device
{
    public class ProviderManager : IDeviceProvider
    {
        public ProviderManager(IServiceProvider service)
        {
            Service = service;
            Providers = Service.GetServices<IDeviceProvider>()
                        .Where(x=> x is not ProviderManager);
        }

        
        public IServiceProvider Service { get; }
        public IEnumerable<IDeviceProvider> Providers { get; }

        public async Task Init(ILoadDevice loadDevice)
        {
            Providers.AsParallel().ForAll(async x => await x.Init(loadDevice));
        }

    }
}
