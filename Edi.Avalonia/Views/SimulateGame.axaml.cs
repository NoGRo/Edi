using Avalonia.Controls;
using Avalonia.Interactivity;
using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Simulator;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edi.Avalonia.Views;

public partial class SimulateGame : Window
{
    private readonly IEdi edi = App.Edi;
    private readonly DeviceCollector deviceCollector;
    private PreviewDevice? simulatorDevice;

    public SimulateGame()
    {
        InitializeComponent();
        deviceCollector = edi.DeviceCollector;
        simulatorDevice = new PreviewDevice(
            App.ServiceProvider.GetRequiredService<FunscriptRepository>(),
            App.ServiceProvider.GetRequiredService<ILogger<PreviewDevice>>());
        DataContext = new SimulateGameViewModel { SimulatorDevice = simulatorDevice };

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    internal void OnIsAlwaysOnTopChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk)
        {
            Topmost = chk.IsChecked == true;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        simulatorDevice?.StopGallery();
        if (simulatorDevice != null)
        {
            deviceCollector.UnloadDevice(simulatorDevice);
        }

        simulatorDevice = null;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        deviceCollector.LoadDevice(simulatorDevice);
    }
}