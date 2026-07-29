using CsvHelper;
using CsvHelper.Configuration;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Players;
using Edi.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using NAudio.Wave.SampleProviders;
using PropertyChanged;
using Serilog.Core;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Edi.Core
{
    [AddINotifyPropertyChangedInterface]
    public class Edi : IEdi
    {
        public ConfigurationManager ConfigurationManager { get; set; }
        public DeviceCollector DeviceCollector { get; private set; }
        public DeviceConfiguration DeviceConfiguration { get; private set; }
        public IPlayerChannels Player { get; private set; }
        public string GalleryPath { get; private set; }


        private readonly RepositoryManager _repositoryManager;
        public IEnumerable<IRepository> repos
            => _repositoryManager.CreatedRepositories;
        private readonly PlayerLogService _logService;

        public Edi(
            DeviceCollector deviceCollector,
            IPlayerChannels player,
            DefinitionRepository repository,
            RepositoryManager repositoryManager,
            ConfigurationManager configuration,
            DeviceConfiguration deviceConfiguration,
            PlayerLogService rfgLogService = null)
        {
            if (!Directory.Exists(OutputDir))
                Directory.CreateDirectory(OutputDir);

            DeviceCollector = deviceCollector;
            Player = player;

            _repository = repository;
            _repositoryManager = repositoryManager;

            ConfigurationManager = configuration;
            Config = configuration.Get<EdiConfig>();
            DeviceConfiguration = deviceConfiguration;
            _logService = rfgLogService;

            _logService.OnLogReceived += (msg) => OnChangeStatus?.Invoke(msg);
            
        }

        /// <summary>
        /// Resuelve el path de la galería interpretando si el argumento es un folder o un archivo de configuración.
        /// Escapa en el primer caso válido.
        /// </summary>
        public string ResolveGallery(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                // Primer escape: configuración actual
                return ConfigurationManager.Get<GalleryConfig>()?.GalleryPath ?? "./";
            }

            if (!Path.GetFileName(path).Equals("EdiConfig.json", StringComparison.OrdinalIgnoreCase))
            {
                // Tercer escape: path directo
                ConfigurationManager.SetGamePath(path);
                return path;
            }
            
            // Segundo escape: archivo de configuración
            ConfigurationManager.SetGamePath(path);
            var configGalleryPath = ConfigurationManager.Get<GalleryConfig>()?.GalleryPath;
            if (string.IsNullOrWhiteSpace(configGalleryPath))
            {
                return "./";
            }
            if (Path.IsPathRooted(configGalleryPath))
            {
                return configGalleryPath;
            }
            
            var configDirectory = Path.GetDirectoryName(path);
            return Path.GetFullPath(Path.Combine(configDirectory, configGalleryPath));
            
            
        }
        public async Task<GameInfo> SelectGame(GameInfo game)
        {
            ArgumentNullException.ThrowIfNull(game);

            await Init(game.Path);

            var resolvedGameInfo = game with
            {
                Path = ConfigurationManager.GamePathConfig
            };
            var gamesConfig = ConfigurationManager.Get<GamesConfig>();
            resolvedGameInfo = gamesConfig.UpsertGame(
                resolvedGameInfo,
                game);
            gamesConfig.SelectedGameinfo = resolvedGameInfo;

            return resolvedGameInfo;

        }
        public async Task Init(string path)
        {
            string galleryPath = ResolveGallery(path);
            await Player.Stop();
            await DeviceCollector.Reload(async () =>
            {
                GalleryPath = galleryPath;
                await _repositoryManager.ChangePath(galleryPath);
            });
            Player.ResetChannels(Config.Channels.ToList());
        }

        public async Task InitDevices()
        {
            await DeviceCollector.Refresh();
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

        public static string OutputDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Edi");

        public ObservableCollection<IDevice> Devices => new ObservableCollection<IDevice>(DeviceCollector.Devices);


        public EdiConfig Config { get; set; }

        

        private DefinitionRepository _repository { get; set; }
        public IEnumerable<DefinitionGallery> Definitions => _repository.GetAll();
        public event IEdi.ChangeStatusHandler OnChangeStatus;
        



    }
}
