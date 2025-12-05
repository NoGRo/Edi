using Edi.Core.Device;
using Edi.Core.Device.AutoBlow;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR;
using Edi.Core.Device.Simulator;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Gallery.Index;
using Edi.Core.Players;
using Edi.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Enums;

namespace Edi.Core
{
    public static class EdiRegistration
    {
        public static void AddEdi(this IServiceCollection services, string configPath)
        {

            services.AddSingleton<ConfigurationManager>(x => new(configPath));

            services.AddSingleton<AudioEngine>(x => new MiniAudioEngine([MiniAudioBackend.WinMm, MiniAudioBackend.PulseAudio]));

            services.AddSingleton<DefinitionRepository>(); ;
            services.AddSingleton<FunscriptRepository>();
            services.AddSingleton<IndexRepository>();
            services.AddSingleton<AudioRepository>();
            services.AddSingleton<IRepository>(sp => sp.GetRequiredService<DefinitionRepository>());
            services.AddSingleton<IRepository>(sp => sp.GetRequiredService<FunscriptRepository>());
            services.AddSingleton<IRepository>(sp => sp.GetRequiredService<IndexRepository>());
            services.AddSingleton<IRepository>(sp => sp.GetRequiredService<AudioRepository>());

            services.AddSingleton<GalleryBundler>();

            // Registrar Device Manager y Providers
            services.AddSingleton<DeviceCollector>();

            services.AddPlayers();

            services.AddSingleton<DeviceConfiguration>();

            services.AddSingleton<IDeviceProvider, ButtplugProvider>();
            services.AddSingleton<IDeviceProvider, AutoBlowProvider>();
            services.AddHandy();
            services.AddSingleton<IDeviceProvider, OSRProvider>();
            services.AddSingleton<IDeviceProvider, EStimProvider>();
            services.AddSingleton<IDeviceProvider, RecorderProvider>();
            // Integra Serilog con el sistema de logging de Microsoft.Extensions.Logging

            services.AddSingleton<IEdi, Edi>();

            services.AddHostedService<EdiHostedService>();
        }


    }
}
