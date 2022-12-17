using Edi.Core.Device;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Services
{
    public static class ServicesRegistration
    {
        public static void AddEdi(this IServiceCollection builder)
        {

            builder.AddSingleton<IDeviceProvider, ButtplugProvider>();
            builder.AddSingleton<IDeviceProvider, HandyProvider>();
            //TODO: Arquiere other from external dll

            builder.AddSingleton<ProviderManager>();

            builder.AddSingleton<IDeviceManager, DeviceManager>();
            builder.AddSingleton<IGalleryRepository, GalleryRepository>();

            builder.AddSingleton<iEdi, Edi>();

        }
             
    }
}
