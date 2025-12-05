using System.Collections.Generic;
using System.Collections.ObjectModel;
using Edi.Avalonia.Models;
using Edi.Core;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR;
using Edi.Core.Gallery;
using PropertyChanged.SourceGenerator;

namespace Edi.Avalonia.Views;

public partial class MainWindowViewModel
{
    [Notify]
    private List<AudioDevice> audioDevices = [];

    [Notify]
    private ButtplugConfig? buttplugConfig;

    [Notify]
    private List<string> channels = [];

    [Notify]
    private HashSet<ComPort> comPorts = [];

    [Notify]
    private EdiConfig? config;

    [Notify]
    private ObservableCollection<IDevice> devices = [];

    [Notify]
    private EStimConfig? eStimConfig;

    [Notify]
    private GalleryConfig? galleryConfig;

    [Notify]
    private List<Core.Gallery.Definition.DefinitionGallery> galleries = [];

    [Notify]
    private GamesConfig? gamesConfig;

    [Notify]
    private HandyConfig? handyConfig;

    [Notify]
    private OSRConfig? osrConfig;
}