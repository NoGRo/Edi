using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Edi.Avalonia.Models;
using Edi.Core;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR;
using Edi.Core.Gallery;

namespace Edi.Avalonia.Views;

public partial class MainWindow : Window
{
    private static SimulateGame? simulateGame;

    private readonly EdiConfig config;
    private readonly IEdi edi = App.Edi;
    private readonly GamesConfig gamesConfig;
    private readonly MainWindowViewModel viewModel;

    private DataGridColumn? channelColumn;
    private bool launched;
    private Timer timer;

    public MainWindow()
    {
        InitializeComponent();

        config = edi.ConfigurationManager.Get<EdiConfig>();
        gamesConfig = edi.ConfigurationManager.Get<GamesConfig>();
        var galleries = ReloadGalleries();

        viewModel = new MainWindowViewModel
        {
            ButtplugConfig = edi.ConfigurationManager.Get<ButtplugConfig>(),
            Channels = edi.Player.Channels,
            Config = config,
            Devices = edi.Devices,
            EStimConfig = edi.ConfigurationManager.Get<EStimConfig>(),
            Galleries = galleries,
            GalleryConfig = edi.ConfigurationManager.Get<GalleryConfig>(),
            GamesConfig = gamesConfig,
            HandyConfig = edi.ConfigurationManager.Get<HandyConfig>(),
            OsrConfig = edi.ConfigurationManager.Get<OSRConfig>(),
        };
        DataContext = viewModel;

        // Add column visibility control after InitializeComponent
        DevicesGrid.Loaded += (s, e) =>
        {
            channelColumn = DevicesGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Channel");
            UpdateChannelColumnVisibility();
        };

        // Add property change handler for viewModel
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(viewModel.Config))
            {
                UpdateChannelColumnVisibility();
            }
        };

        edi.DeviceCollector.OnloadDevice += DeviceCollector_OnLoadDeviceAsync;
        edi.DeviceCollector.OnUnloadDevice += DeviceCollector_OnUnloadDevice;
        edi.OnChangeStatus += Edi_OnChangeStatus;

        timer = new Timer(RefreshGrid);
        timer.Change(3000, 3000);

        Closing += MainWindow_Closing;

        edi.Player.ChannelsChanged += channels => viewModel.Channels = channels;

        LoadForm();
    }

    public override async void EndInit()
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            await edi.Player.Pause();
        });
        await Task.Delay(1000);
        base.EndInit();
    }

    private void LoadForm()
    {
        var audios = new List<AudioDevice> { new(-1, "None") };
        // TODO
        // for (int i = 0; i < WaveOut.DeviceCount; i++)
        // {
        //     audios.Add(new AudioDevice(i, WaveOut.GetCapabilities(i).ProductName));
        // }
        viewModel.AudioDevices = audios;
        LoadOsrPorts();
        DevicesGrid.ItemsSource = edi.Devices;
    }

    private void LoadOsrPorts()
    {
        var comPorts = new HashSet<ComPort> { new("None", null) };
        try
        {
            foreach (var port in SerialPort.GetPortNames())
            {
                comPorts.Add(new ComPort(port, port));
            }
        }
        catch (Exception)
        {
        }

        viewModel.ComPorts = comPorts;
    }

    private void RefreshGrid(object? o)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (edi.DeviceCollector.Devices.Any(x => x.IsReady)
                && !launched
                && !string.IsNullOrEmpty(config.ExecuteOnReady)
                && File.Exists(config.ExecuteOnReady))
            {
                launched = true;
                LblStatus.Content = "launched: " + config.ExecuteOnReady;
                await GetTopLevel(this)!.Launcher.LaunchFileInfoAsync(new FileInfo(config.ExecuteOnReady));
            }
        });
    }

    private List<Core.Gallery.Definition.DefinitionGallery> ReloadGalleries()
    {
        var galleries = edi.Definitions.Where(x => x.Type != "filler").ToList();

        galleries.Insert(0, new Core.Gallery.Definition.DefinitionGallery { Name = "" });
        galleries.Insert(1, new Core.Gallery.Definition.DefinitionGallery { Name = "(Random)" });
        galleries.InsertRange(2, edi.Definitions.Where(x => x.Type == "filler"));
        return galleries;
    }

    private void UpdateChannelColumnVisibility()
    {
        if (viewModel.Config != null && channelColumn != null)
        {
            channelColumn.IsVisible = viewModel.Config.UseChannels;
        }
    }

    private void BtnOpenOutput_OnClick(object? sender, RoutedEventArgs e)
    {
        GetTopLevel(this)!.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Core.Edi.OutputDir));
    }

    private async void BtnPause_OnClick(object? sender, RoutedEventArgs e)
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            await edi.Player.Pause();
        });
    }

    private async void BtnPlay_OnClick(object? sender, RoutedEventArgs e)
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            var selected = CmbGallery.Text;
            if (selected == "(Random)")
            {
                selected = edi.Definitions.OrderBy(x => Guid.NewGuid()).FirstOrDefault()?.Name ?? "";
            }

            await edi.Player.Play(selected, 0);
        });
    }

    private async void BtnResume_OnClick(object? sender, RoutedEventArgs e)
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            await edi.Player.Resume(false);
        });
    }

    private void BtnSimulator_OnClick(object? sender, RoutedEventArgs e)
    {
        if (simulateGame is not { IsLoaded: true })
        {
            simulateGame = new SimulateGame();
            simulateGame.Closed += (s, args) => simulateGame = null;
            simulateGame.Show();
            simulateGame.Activate();
        }
        else
        {
            simulateGame.Close();
        }
    }

    private async void BtnStop_OnClick(object? sender, RoutedEventArgs e)
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            await edi.Player.Stop();
        });
    }

    private async void BtnSwagger_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton link)
        {
            await GetTopLevel(this)!.Launcher.LaunchUriAsync(new Uri(link.Content as string));
        }
    }

    private void Channels_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var comboBox = sender as ComboBox;
        var device = comboBox.DataContext as IDevice;
        _ = edi.DeviceConfiguration.SelectChannel(device, (string)comboBox.SelectedValue);
    }

    private async void DeviceCollector_OnLoadDeviceAsync(IDevice device, List<IDevice> devices)
    {
        await Task.Delay(500);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DevicesGrid.ItemsSource = edi.Devices;
        });
    }

    private async void DeviceCollector_OnUnloadDevice(IDevice device, List<IDevice> devices)
    {
        await Task.Delay(1000);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DevicesGrid.ItemsSource = edi.Devices;
        });
    }

    private void Edi_OnChangeStatus(string message)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            LblStatus.Content = message;
        });
    }

    private async void GamesComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            if (GamesComboBox.SelectedItem is GameInfo selectedGame)
            {
                await edi.SelectGame(selectedGame);
                viewModel.Galleries = ReloadGalleries();
            }
        });
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            await edi.Player.Pause();
        });
        await Task.Delay(1000);
    }

    private async void ReconnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            LoadOsrPorts();
            await edi.InitDevices();
        });
    }

    private async void ReloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectedConfig = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select EdiConfig.json or Definition.csv file",
                FileTypeFilter =
                [
                    new FilePickerFileType("EdiConfig.json") { Patterns = ["EdiConfig.json"] },
                    new FilePickerFileType("Definition.csv") { Patterns = ["Definition.json"] }
                ],
                AllowMultiple = false,
            });
        if (selectedConfig.Count < 1)
        {
            return;
        }

        var configPath = selectedConfig[0].Path.AbsolutePath;
        var game = new GameInfo(configPath, configPath);
        if (gamesConfig.GamesInfo.All(x => x.Path != configPath))
        {
            gamesConfig.GamesInfo.Add(game);
        }

        await edi.SelectGame(game);
    }

    private async void SliderIntensity_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        await Dispatcher.UIThread.Invoke(async () =>
        {
            await edi.Player.Intensity(Convert.ToInt32(e.NewValue));
        });
    }

    private void Variants_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var comboBox = sender as ComboBox;
        var device = comboBox.DataContext as IDevice;
        _ = edi.DeviceConfiguration.SelectVariant(device, (string)comboBox.SelectedValue);
    }
}