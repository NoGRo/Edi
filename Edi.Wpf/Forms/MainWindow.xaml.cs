using Edi.Core;
using Edi.Core.Device;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR;
using Edi.Core.Gallery;
using Microsoft.AspNetCore.Components;
using SoundFlow.Abstracts;
using Path = System.IO.Path;

namespace Edi.Forms
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IEdi edi = App.Edi;
        private readonly AudioEngine engine;
        public EdiConfig config;
        public GalleryConfig galleryConfig;
        public HandyConfig handyConfig;
        public ButtplugConfig buttplugConfig;
        public EStimConfig estimConfig;
        public OSRConfig osrConfig;
        private Timer timer;
        private bool launched;
        private record AudioDevice(int id, string name);
        private record ComPort(string name, string? value);
        private record ChannelsNames(string name, string? value);
        private DataGridColumn _channelColumn;

        public MainWindow()
        {
            engine = App.ServiceProvider.GetRequiredService<AudioEngine>();

            config = edi.ConfigurationManager.Get<EdiConfig>();
            handyConfig = edi.ConfigurationManager.Get<HandyConfig>();
            galleryConfig = edi.ConfigurationManager.Get<GalleryConfig>();
            buttplugConfig = edi.ConfigurationManager.Get<ButtplugConfig>();
            estimConfig = edi.ConfigurationManager.Get<EStimConfig>();
            osrConfig = edi.ConfigurationManager.Get<OSRConfig>();
            gamesConfig = edi.ConfigurationManager.Get<GamesConfig>();
            List<Core.Gallery.Definition.DefinitionGallery> galleries = ReloadGalleries();

            viewModel = new MainWindowViewModel
            {
                config = config,
                handyConfig = handyConfig,
                buttplugConfig = buttplugConfig,
                galleryConfig = galleryConfig,
                estimConfig = estimConfig,
                osrConfig = osrConfig,
                gamesConfig = gamesConfig,
                devices = edi.Devices,
                channels = edi.Player.Channels,
                galleries = galleries,
            };
            this.DataContext = viewModel;
            InitializeComponent();

            // Add column visibility control after InitializeComponent
            DevicesGrid.Loaded += (s, e) =>
            {
                _channelColumn = DevicesGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Channel");
                UpdateChannelColumnVisibility();
            };

            // Add property change handler for viewModel
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.config))
                {
                    UpdateChannelColumnVisibility();
                }
            };

            edi.DeviceCollector.OnloadDevice += DeviceCollector_OnloadDeviceAsync;
            edi.DeviceCollector.OnUnloadDevice += DeviceCollector_OnUnloadDevice;
            edi.OnChangeStatus += Edi_OnChangeStatus;

            timer = new Timer(RefrehGrid);
            timer.Change(3000, 3000);

            Closing += MainWindow_Closing;

            edi.Player.ChannelsChanged += (channels) => viewModel.UpdateChannels(channels);

            LoadForm();
        }

        private void UpdateChannelColumnVisibility()
        {

            if (_channelColumn != null && viewModel?.config != null)
            {
                _channelColumn.Visibility = viewModel.config.UseChannels ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private List<Core.Gallery.Definition.DefinitionGallery> ReloadGalleries()
        {
            var galleries = edi.Definitions.Where(x => x.Type != "filler").ToList();

            galleries.Insert(0, new Core.Gallery.Definition.DefinitionGallery { Name = "" });
            galleries.Insert(1, new Core.Gallery.Definition.DefinitionGallery { Name = "(Random)" });
            galleries.InsertRange(2, edi.Definitions.Where(x => x.Type == "filler"));
            return galleries;
        }
        private void RefrehGrid(object? o)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (edi.DeviceCollector.Devices.Any(x => x.IsReady)
                    && !launched
                    && !string.IsNullOrEmpty(config.ExecuteOnReady)
                    && File.Exists(config.ExecuteOnReady))
                {
                    launched = true;
                    lblStatus.Content = "launched: " + config.ExecuteOnReady;
                    Process.Start(new ProcessStartInfo(new FileInfo(config.ExecuteOnReady).FullName) { UseShellExecute = true });

                }
            });
        }
        private void LoadForm()
        {
            var audios = new List<AudioDevice>() { new AudioDevice(-1, "None") };
            engine.UpdateAudioDevicesInfo();
            for (int i = 0; i < engine.PlaybackDevices.Length; i++)
            {
                audios.Add(new AudioDevice(i, engine.PlaybackDevices[i].Name));
            }
            audioDevicesComboBox.ItemsSource = audios;
            loadOSRPorts();
            DevicesGrid.ItemsSource = edi.Devices;
        }

        private void loadOSRPorts()
        {
            var comPorts = new HashSet<ComPort>() { new ComPort("None", null) };
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

            comPortsComboBox.ItemsSource = comPorts;
        }
        private void Edi_OnChangeStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                lblStatus.Content = message;
            });
        }

        private async void DeviceCollector_OnUnloadDevice(IDevice device, List<IDevice> devices)
        {
            await Task.Delay(1000);
            await Dispatcher.InvokeAsync(() =>
            {
                DevicesGrid.ItemsSource = edi.Devices;

                //DevicesGrid.Items.Refresh();
            });

        }

        private async void DeviceCollector_OnloadDeviceAsync(IDevice device, List<IDevice> devices)
        {
            await Task.Delay(500);

            await Dispatcher.InvokeAsync(() =>
            {
                DevicesGrid.ItemsSource = edi.Devices;
                //DevicesGrid.Items.Refresh();
            });
        }


        private void Variants_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox? comboBox = sender as ComboBox;

            var device = comboBox.DataContext as IDevice;
            _ = edi.DeviceConfiguration.SelectVariant(device, (string)comboBox.SelectedValue);
        }

        private void Channels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox? comboBox = sender as ComboBox;

            var device = comboBox.DataContext as IDevice;

            _ = edi.DeviceConfiguration.SelectChannel(device, (string)comboBox.SelectedValue);
        }


        private async void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Title = "Select EdiConfig.json or Definition.csv file";
                dialog.Filter = "EdiConfig.json|EdiConfig.json|Definitions.csv|Definitions.csv";
                dialog.FilterIndex = 1;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string configPath = dialog.FileName;

                    var game = new GameInfo(configPath, configPath);
                    if (!gamesConfig.GamesInfo.Any(x => x.Path == configPath))
                    {
                        gamesConfig.GamesInfo.Add(game);
                    }
                    await edi.SelectGame(game);

                }
            }
        }
        public async void GamesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                if (GamesComboBox.SelectedItem is GameInfo selectedGame)
                {
                    // gamesConfig.SelectedGameinfo = selectedGame;
                    await edi.SelectGame(selectedGame);
                    viewModel.galleries = ReloadGalleries();
                }
            });
        }

        private async void ReconnectButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                loadOSRPorts();
                await edi.InitDevices();
            });
        }

        private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c start http://localhost:5000/swagger/index.html") { CreateNoWindow = true });

        }

        private async void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                var selected = cmbGallerie.Text;
                if (selected == "(Random)")
                    selected = edi.Definitions.OrderBy(x => Guid.NewGuid()).FirstOrDefault()?.Name ?? "";

                await edi.Player.Play(selected, 0);
            });
        }

        private async void btnStop_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Stop();
            });
        }

        private async void btnPause_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Pause();
            });
        }

        private async void btnResume_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Resume(false);
            });
        }

        private static SimulateGame _simulateGame; // Quitamos readonly y la inicialización inmediata
        private MainWindowViewModel viewModel;
        private GamesConfig gamesConfig;
        // ...

        private void btnSimulator_Click(object sender, RoutedEventArgs e)
        {
            if (_simulateGame == null || !_simulateGame.IsLoaded)
            {
                _simulateGame = new SimulateGame();
                _simulateGame.Closed += (s, args) => _simulateGame = null;
                _simulateGame.Show();
                _simulateGame.Activate();
            }
            else
            {
                _simulateGame.Close();
            }
        }


        public override async void EndInit()
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Pause();
            });
            await Task.Delay(1000);
            base.EndInit();
        }

        private async void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Intensity(Convert.ToInt32(sliderIntensity.Value));
            });
        }

        private void btnOpenOutput_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("explorer.exe",Edi.Core.Edi.OutputDir) { UseShellExecute = true });
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Pause();
            });
            await Task.Delay(1000);
        }
    }
    public class BoolToReadyIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool && (bool)value) ? "✅" : "🚫";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
