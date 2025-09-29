using System.Collections.ObjectModel;
using System.ComponentModel;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.OSR;
using Edi.Core;
using System.Collections.Generic;
using Edi.Core.Gallery;

namespace Edi.Forms
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private EdiConfig _config;
        public EdiConfig config
        {
            get => _config;
            set
            {
                if (_config != null && _config is INotifyPropertyChanged oldConfig)
                    oldConfig.PropertyChanged -= ConfigPropertyChanged;

                _config = value;

                if (_config != null && _config is INotifyPropertyChanged newConfig)
                    newConfig.PropertyChanged += ConfigPropertyChanged;

                OnPropertyChanged(nameof(config));
            }
        }

        private GamesConfig _gamesConfig;
        public GamesConfig gamesConfig
        {
            get => _gamesConfig;
            set { _gamesConfig = value; OnPropertyChanged(nameof(gamesConfig)); }
        }

        private GalleryConfig _galleryConfig;
        public GalleryConfig galleryConfig
        {
            get => _galleryConfig;
            set { _galleryConfig = value; OnPropertyChanged(nameof(galleryConfig)); }
        }

        private HandyConfig _handyConfig;
        public HandyConfig handyConfig
        {
            get => _handyConfig;
            set { _handyConfig = value; OnPropertyChanged(nameof(handyConfig)); }
        }

        private ButtplugConfig _buttplugConfig;
        public ButtplugConfig buttplugConfig
        {
            get => _buttplugConfig;
            set { _buttplugConfig = value; OnPropertyChanged(nameof(buttplugConfig)); }
        }

        private EStimConfig _estimConfig;
        public EStimConfig estimConfig
        {
            get => _estimConfig;
            set { _estimConfig = value; OnPropertyChanged(nameof(estimConfig)); }
        }

        private OSRConfig _osrConfig;
        public OSRConfig osrConfig
        {
            get => _osrConfig;
            set { _osrConfig = value; OnPropertyChanged(nameof(osrConfig)); }
        }

        public ObservableCollection<IDevice> devices { get; set; }
        public List<string> channels { get; set; }
        private List<Core.Gallery.Definition.DefinitionGallery> _galleries;
        public List<Core.Gallery.Definition.DefinitionGallery> galleries
        {
            get => _galleries;
            set { _galleries = value; OnPropertyChanged(nameof(galleries)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void ConfigPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(config));
        }

        public void UpdateChannels(List<string> newChannels)
        {
            channels = newChannels;
            OnPropertyChanged(nameof(channels));
        }
    }
}
