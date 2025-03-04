using Edi.Core.Device;
using Edi.Core.Device.AutoBlow;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Gallery.Index;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core
{
    public static class ServicesRegistration
    {
        public static void AddEdi(this IServiceCollection services)
        {


            services.AddSingleton<IRepository, DefinitionRepository>();
            services.AddSingleton<IRepository, FunscriptRepository>();
            services.AddSingleton<IRepository, IndexRepository>();
            services.AddSingleton<IRepository, AudioRepository>();
            services.AddSingleton<GalleryBundler>();

            // Registrar Device Manager y Providers
            services.AddSingleton<DeviceManager>();
            services.AddSingleton<IDeviceProvider, ButtplugProvider>();
            services.AddSingleton<IDeviceProvider, AutoBlowProvider>();
            services.AddSingleton<IDeviceProvider, HandyProvider>();
            services.AddSingleton<IDeviceProvider, OSRProvider>();
            services.AddSingleton<IDeviceProvider, EStimProvider>();




            services.AddSingleton<IEdi, Edi>();
            
        }
             
    }
}
