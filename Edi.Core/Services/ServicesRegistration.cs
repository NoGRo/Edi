using Edi.Core.Device;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;
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
        public static void AddEdi(this IServiceCollection builder)
        {
            #region Devices
            
            builder.AddSingleton<IDeviceProvider, ButtplugProvider>();
            builder.AddSingleton<IDeviceProvider, HandyProvider>();
            //TODO: Arquiere other from external dll

            builder.AddSingleton<IDeviceManager, DeviceManager>();
            
            #endregion

            #region Repositories

            builder.AddSingleton<GalleryBundler>();
            builder.AddSingleton<DefinitionRepository>();
            builder.AddSingleton<FunscriptRepository>();
            builder.AddSingleton<IndexRepository>();
            
            #endregion


            builder.AddSingleton<IEdi, Edi>();
            
        }
             
    }
}
