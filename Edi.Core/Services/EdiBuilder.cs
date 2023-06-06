using Edi.Core.Device;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Index;
using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core
{
    public static class EdiBuilder
    {

        public static IEdi Create(string ConfigurationPath)
        {
            #region Configuration
            
            var builder = new ConfigurationBuilder()
                //                .SetBasePath(Directory.GetCurrentDirectory()) // Definimos la ruta de donde se tomará el archivo de configuración
                .AddJsonFile(ConfigurationPath, optional: true, reloadOnChange: true); // Agregamos el archivo de configuración en formato json
            var configuration = builder.Build();

            #endregion

            #region Repositories

            var definitionRepository = new DefinitionRepository(configuration);
            var funscriptRepository = new FunscriptRepository(configuration, definitionRepository);
            var indexRepository = new IndexRepository(configuration, new GalleryBundler(configuration), funscriptRepository);

            #endregion

            var deviceManager = new DeviceManager();

            #region Device Provides 

            deviceManager.Providers.Add(new ButtplugProvider(funscriptRepository, configuration, deviceManager));
            deviceManager.Providers.Add(new HandyProvider(indexRepository, configuration, deviceManager));

            #endregion
            
            return new Edi(deviceManager, definitionRepository,new IRepository[] { definitionRepository, funscriptRepository, indexRepository }, configuration);

        }

    }
}
