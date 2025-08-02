using System.Collections.ObjectModel;
using System.ComponentModel;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.OSR;
using Edi.Core;
using System.Collections.Generic;

namespace Edi.Forms
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        public EdiConfig config { get; set; }
        public HandyConfig handyConfig { get; set; }
        public ButtplugConfig buttplugConfig { get; set; }
        public EStimConfig estimConfig { get; set; }
        public OSRConfig osrConfig { get; set; }

        public ObservableCollection<IDevice> devices { get; set; }
        public List<string> channels { get; set; }
        
        public List<Core.Gallery.Definition.DefinitionGallery> galleries { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void UpdateChannels(List<string> newChannels)
        {
            channels = newChannels;
            OnPropertyChanged(nameof(channels));
        }
    }
}
