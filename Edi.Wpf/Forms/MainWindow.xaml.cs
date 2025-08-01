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

namespace Edi.Forms
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IEdi edi =  App.Edi;
        public EdiConfig config;
        public HandyConfig handyConfig;
        public ButtplugConfig buttplugConfig;
        public EStimConfig estimConfig;
        public OSRConfig osrConfig;
        private Timer timer;
        private bool launched;
        private void RefrehGrid(object? o)
        {
            Dispatcher.Invoke(async () =>
            {
                if (edi.DeviceCollector.Devices.Any(x => x.IsReady))
                {

                    if (!launched
                        && !string.IsNullOrEmpty(config.ExecuteOnReady)
                        && File.Exists(config.ExecuteOnReady))
                    {
                        launched = true;
                        lblStatus.Content = "launched: " + config.ExecuteOnReady;
                        Process.Start(new ProcessStartInfo(new FileInfo(config.ExecuteOnReady).FullName) { UseShellExecute = true});
                        
                    }

                    
                }
            });

        }

        private record AudioDevice(int id, string name);
        private record ComPort(string name, string? value);
        public MainWindow()
        {
            config = edi.ConfigurationManager.Get<EdiConfig>();
            handyConfig = edi.ConfigurationManager.Get<HandyConfig>();
            buttplugConfig = edi.ConfigurationManager.Get<ButtplugConfig>();
            estimConfig = edi.ConfigurationManager.Get<EStimConfig>();
            osrConfig = edi.ConfigurationManager.Get<OSRConfig>();

            var galleries = edi.Definitions.Where(x=> x.Type != "filler" ).ToList();

            galleries.Insert(0, new Core.Gallery.Definition.DefinitionGallery { Name = "" });
            galleries.Insert(1, new Core.Gallery.Definition.DefinitionGallery { Name = "(Random)" });
            galleries.InsertRange(2, edi.Definitions.Where(x=> x.Type == "filler" ));
            
            this.DataContext = new
            {
                config = config,
                handyConfig = handyConfig,
                buttplugConfig = buttplugConfig,
                estimConfig = estimConfig,
                osrConfig = osrConfig,

                devices = edi.Devices,
                channels = edi.Player.Channels,
                galleries = galleries,
            };
            InitializeComponent();

            edi.DeviceCollector.OnloadDevice += DeviceCollector_OnloadDeviceAsync;
            edi.DeviceCollector.OnUnloadDevice += DeviceCollector_OnUnloadDevice;
            edi.OnChangeStatus += Edi_OnChangeStatus;

            timer = new Timer(RefrehGrid);
            timer.Change(3000, 3000);
            
            Closing += MainWindow_Closing;
            LoadForm();
        }

        private void LoadForm()
        {
            var audios = new List<AudioDevice>() { new AudioDevice(-1, "None") };
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                audios.Add(new AudioDevice(i, WaveOut.GetCapabilities(i).ProductName));
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
            await Dispatcher.Invoke(async () =>
            {
                await edi.Init();
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
