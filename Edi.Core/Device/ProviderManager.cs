using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Device.Buttplug;
using Microsoft.Extensions.DependencyInjection;
namespace Edi.Core.Device
{
    public class ProviderManager
    {
        public ProviderManager(IServiceProvider service)
        {
            Service = service;
            Providers = Service.GetServices<IDeviceProvider>();
        }

        
        public IServiceProvider Service { get; }
        public IEnumerable<IDeviceProvider> Providers { get; }

        private async Task Init()
        {
            Providers.AsParallel().ForAll(async x => await x.Init());
        }

    }
}
