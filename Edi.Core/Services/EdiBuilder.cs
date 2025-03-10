using Edi.Core.Device;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Gallery.Index;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Device.AutoBlow;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Edi.Core
{
    public static class EdiBuilder
    {

        public static IEdi Create(string ConfigurationPath)
        {
            #region Configuration
            var configuration = new ConfigurationManager(ConfigurationPath);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File("./Edilog.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Integra Serilog con ILogger
            using var loggerFactory = LoggerFactory.Create(builder => { builder.AddSerilog(); });
            var logger = loggerFactory.CreateLogger<Edi>();
            #endregion

            #region Repositories

            var definitionRepository = new DefinitionRepository(configuration);
            var funscriptRepository = new FunscriptRepository(definitionRepository, logger);
            var indexRepository = new IndexRepository(configuration, new GalleryBundler(configuration), funscriptRepository, definitionRepository);
            var audioRepository = new AudioRepository( definitionRepository, logger);

            #endregion

            var deviceManager = new DeviceManager(configuration);

            #region Device Provides 

            deviceManager.Providers.Add(new ButtplugProvider(funscriptRepository, configuration, deviceManager, logger));
            deviceManager.Providers.Add(new AutoBlowProvider(indexRepository, configuration, deviceManager, logger));
            deviceManager.Providers.Add(new HandyProvider(indexRepository, configuration, deviceManager, logger));
            deviceManager.Providers.Add(new OSRProvider(funscriptRepository, configuration, deviceManager, logger));
            deviceManager.Providers.Add(new EStimProvider(audioRepository, configuration, deviceManager,logger));

            #endregion
            var edi  = new Edi(deviceManager, definitionRepository,new IRepository[] { definitionRepository, funscriptRepository, indexRepository, audioRepository }, configuration, logger);

            return edi;

        }

    }
}
