using Edi.Core.Device;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Gallery.Index;

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
            var configuration = new ConfigurationManager(ConfigurationPath);

            #endregion

            #region Repositories

            var definitionRepository = new DefinitionRepository(configuration);
            var funscriptRepository = new FunscriptRepository(definitionRepository);
            var indexRepository = new IndexRepository(configuration, new GalleryBundler(configuration), funscriptRepository, definitionRepository);
            var audioRepository = new AudioRepository( definitionRepository);

            #endregion

            var deviceManager = new DeviceManager(configuration);

            #region Device Provides 

            deviceManager.Providers.Add(new ButtplugProvider(funscriptRepository, configuration, deviceManager));
            deviceManager.Providers.Add(new HandyProvider(indexRepository, configuration, deviceManager));
            deviceManager.Providers.Add(new EStimProvider(audioRepository, configuration, deviceManager));

            #endregion

            return new Edi(deviceManager, definitionRepository,new IRepository[] { definitionRepository, funscriptRepository, indexRepository, audioRepository }, configuration);

        }

    }
}
