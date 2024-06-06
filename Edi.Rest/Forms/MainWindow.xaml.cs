﻿using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Globalization;
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

        private record AudioDevice(int id, string name);
        public MainWindow()
        {
            config = edi.ConfigurationManager.Get<EdiConfig>();
            handyConfig = edi.ConfigurationManager.Get<HandyConfig>();
            buttplugConfig = edi.ConfigurationManager.Get<ButtplugConfig>();
            estimConfig = edi.ConfigurationManager.Get<EStimConfig>();

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

                devices = edi.DeviceManager.Devices,
                
                galleries = galleries,


            };
            InitializeComponent();

            edi.DeviceManager.OnloadDevice += DeviceManager_OnloadDeviceAsync;
            edi.DeviceManager.OnUnloadDevice += DeviceManager_OnUnloadDevice;
            edi.OnChangeStatus += Edi_OnChangeStatus;



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
            DevicesGrid.ItemsSource = edi.DeviceManager.Devices;
        }
        private void Edi_OnChangeStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                lblStatus.Content = message;
            });
        }

        private async void DeviceManager_OnUnloadDevice(IDevice device, List<IDevice> devices)
        {
            Thread.Sleep(1000);
            await Dispatcher.InvokeAsync(async () =>
            {
                DevicesGrid.ItemsSource =  devices;

                DevicesGrid.Items.Refresh();
            });

        }

        private async void DeviceManager_OnloadDeviceAsync(IDevice device, List<IDevice> devices)
        {
            Thread.Sleep(500);

            await Dispatcher.InvokeAsync(async () =>
            {
                DevicesGrid.ItemsSource = devices;
                DevicesGrid.Items.Refresh();
            });
        }


        private void Variants_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (sender as ComboBox);
            
            var device = comboBox.DataContext as IDevice;
            edi.DeviceManager.SelectVariant(device, (string)comboBox.SelectedValue);
        }


        private async void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Init();
            });
           
        }    
        private async void RePackButton_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Repack();
            });
           
        }

        private async void ReconnectButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.DeviceManager.Init();
            });
        }

        private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c start http://localhost:5000/swagger/index.html") { CreateNoWindow = true });
           
        }

        private async void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            


            await Dispatcher.Invoke(async () =>
            {
                var selected = cmbGallerie.Text;
                if (selected == "(Random)")
                    selected = edi.Definitions.OrderBy(x => Guid.NewGuid()).FirstOrDefault()?.Name ?? "";

                await edi.Play(selected, 0);
            });
        }

        private async void btnStop_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Stop();
            });
        }

        private async void btnPause_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Pause();
            });
        }

        private async void btnResume_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Resume(false);
            });
        }

        
        
        public override async void EndInit()
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Pause();
            });
            await Task.Delay(1000); 
            base.EndInit();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Pause();
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
