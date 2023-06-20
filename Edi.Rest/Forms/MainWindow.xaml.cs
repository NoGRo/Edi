﻿using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public MainWindow()
        {
            config = edi.ConfigurationManager.Get<EdiConfig>();
            handyConfig = edi.ConfigurationManager.Get<HandyConfig>();
            buttplugConfig = edi.ConfigurationManager.Get<ButtplugConfig>();
            this.DataContext = new
            {
                config = config,
                handyConfig = handyConfig,
                buttplugConfig = buttplugConfig,
                devices = edi.DeviceManager.Devices,


            };
            InitializeComponent();

            edi.DeviceManager.OnloadDevice += DeviceManager_OnloadDeviceAsync;
            edi.DeviceManager.OnUnloadDevice += DeviceManager_OnUnloadDevice;
            edi.OnChangeStatus += Edi_OnChangeStatus    ;
            LoadForm();
        }

        private void Edi_OnChangeStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                lblStatus.Content = message;
            });
        }

        private void DeviceManager_OnUnloadDevice(Core.Device.Interfaces.IDevice device)
        {
            Thread.Sleep(500);
            Dispatcher.Invoke(() =>
            {
                DevicesGrid.ItemsSource = ((dynamic)this.DataContext).devices; ;
                
                DevicesGrid.Items.Refresh();
            });

        }

        private async void DeviceManager_OnloadDeviceAsync(Core.Device.Interfaces.IDevice device)
        {
            Thread.Sleep(500);

            await Dispatcher.InvokeAsync(async () =>
            {
                
                DevicesGrid.Items.Refresh();
            });
        }

        private void LoadForm()
        {
            DevicesGrid.ItemsSource = edi.DeviceManager.Devices;
        }

        private void Variants_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (sender as ComboBox);
            
            var device = comboBox.DataContext as IDevice;
            edi.DeviceManager.SelectVariant(device.Name, (string)comboBox.SelectedValue);
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
                await edi.DeviceManager.Init();
            });
        }

   
    }
}