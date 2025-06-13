using System;
using System.Threading.Tasks;
using System.Timers;
using Edi.Core.Gallery;
using Timer = System.Timers.Timer;
using Edi.Core.Gallery.Definition;
using NAudio.Wave.SampleProviders;
using CsvHelper;
using CsvHelper.Configuration;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Funscript;
using PropertyChanged;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Serilog.Core;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Players;

namespace Edi.Core
{
    [AddINotifyPropertyChangedInterface]
    public class Edi : IEdi
    {
        public ConfigurationManager ConfigurationManager { get; set; }
        public DeviceCollector DeviceCollector { get; private set; }
        public DeviceConfiguration DeviceConfiguration { get; private set; }
        public IPlayBackChannels Player { get; private set; }
        public Edi(DeviceCollector deviceCollector, IPlayBackChannels player, IEnumerable<IRepository> repos, ConfigurationManager configuration, DeviceConfiguration deviceConfiguration)
        {
            if (!Directory.Exists(OutputDir))
                Directory.CreateDirectory(OutputDir);

            DeviceCollector = deviceCollector;
            Player = player;

            _repository = (DefinitionRepository)repos.First(x => x is DefinitionRepository);
            this.repos = repos;


            ConfigurationManager = configuration;
            Config = configuration.Get<EdiConfig>();
            DeviceConfiguration = deviceConfiguration;
        }


        public async Task Init(string path)
        {
            path = path ?? ConfigurationManager.Get<GalleryConfig>()?.GalleryPath ?? "./";
            foreach (var repo in repos)
            {
                await repo.Init(path);
            }

            _ = Task.Run(InitDevices);
        }
        public async Task InitDevices()
        {
            await DeviceCollector.Init();
        }

        public void CleanDirectory()
        {
            if (Directory.Exists(OutputDir))
            {
                Directory.Delete(OutputDir, true);
            }
            Directory.CreateDirectory(Path.Combine(OutputDir));
            Directory.CreateDirectory(Path.Combine(OutputDir, "Upload"));
        }

        private readonly IEnumerable<IRepository> repos;
        public static string OutputDir => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/Edi";

        public ObservableCollection<IDevice> Devices => new ObservableCollection<IDevice>(DeviceCollector.Devices);


        public EdiConfig Config { get; set; }

        

        private DefinitionRepository _repository { get; set; }
        public IEnumerable<DefinitionGallery> Definitions => _repository.GetAll();
        public event IEdi.ChangeStatusHandler OnChangeStatus;
        



    }
}
